using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Caso de uso central: ejecuta el pipeline diario completo — buscar en
/// todas las fuentes, deduplicar (exacto + semántico), analizar con IA,
/// puntuar y generar el informe.
/// </summary>
public static partial class RunDailyPipeline
{
    public sealed record Command : IRequest<DailyReport>;

    internal sealed partial class Handler(
        IEnumerable<IJobProvider> providers,
        IJobRepository repository,
        IAiAnalysisService aiAnalysisService,
        IEmbeddingService embeddingService,
        UserProfile profile,
        ILogger<Handler> logger) : IRequestHandler<Command, DailyReport>
    {
        private const float DuplicateThreshold = 0.92f;

        // Fallback si el perfil no define keywords: sin ellas no habría
        // búsqueda. Pocas y de backend para no agotar la cuota gratuita de
        // JSearch (200 peticiones/mes, 1 por keyword al día ≈ 90/mes).
        private static readonly string[] FallbackKeywords =
            ["backend .NET", "C# backend developer", "desarrollador backend .NET"];

        private SearchFilter BuildSearchFilter() => new(
            Keywords: profile.SearchKeywords is { Length: > 0 } ? profile.SearchKeywords : FallbackKeywords,
            Location: profile.SearchLocation,
            WorkMode: null,
            MinSalary: null,
            MaxResults: profile.MaxResultsPerProvider);

        public async Task<DailyReport> Handle(Command request, CancellationToken cancellationToken)
        {
            var report = new DailyReport();

            // ── 1. BUSCAR (todos los providers en paralelo) ─────────────
            var found = await SearchAllAsync(cancellationToken);
            report.TotalFound = found.Count;
            logger.LogInformation("Búsqueda: {Total} ofertas de {Providers} providers.",
                found.Count, providers.Count());

            // ── 1b. FILTRAR POR UBICACIÓN (España o remoto sin especificar) ──
            var localOffers = LocationFilter.Apply(found);
            logger.LogInformation("Filtro de ubicación: {Kept}/{Total} compatibles con España/remoto.",
                localOffers.Count, found.Count);

            // ── 1c. FILTRAR POR DESPLAZAMIENTO (remoto pasa; híbrido/presencial
            //        solo si son conmutables según el perfil) ─────────────
            var commutableOffers = CommuteFilter.Apply(localOffers, profile);
            logger.LogInformation("Filtro de desplazamiento: {Kept}/{Total} remotas o conmutables.",
                commutableOffers.Count, localOffers.Count);

            // ── 2. DEDUP EXACTO + guardar nuevas ────────────────────────
            var newOffers = await SaveNewOffersAsync(commutableOffers, cancellationToken);
            report.TotalNew = newOffers.Count;

            // ── 3. DEDUP SEMÁNTICO (embeddings de Ollama) ───────────────
            report.TotalDuplicates = await MarkSemanticDuplicatesAsync(newOffers, cancellationToken);

            // ── 4 + 5. ANALIZAR + PUNTUAR ───────────────────────────────
            report.TotalAnalyzed = await AnalyzeAndScoreAsync(newOffers, cancellationToken);

            // ── 6. INFORME (top 15 + HTML) ──────────────────────────────
            var top = await repository.GetTopScoredAsync(15, cancellationToken);
            report.TopOffers = [.. top];
            report.HtmlContent = BuildHtml(report);

            await repository.SaveReportAsync(report, cancellationToken);

            logger.LogInformation(
                "Pipeline completado: {Found} encontradas, {New} nuevas, {Dup} duplicadas, {Analyzed} analizadas.",
                report.TotalFound, report.TotalNew, report.TotalDuplicates, report.TotalAnalyzed);

            return report;
        }

        private async Task<List<JobOffer>> SearchAllAsync(CancellationToken ct)
        {
            var filter = BuildSearchFilter();
            var tasks = providers.Select(p => SearchSafeAsync(p, filter, ct));
            var results = await Task.WhenAll(tasks);
            return [.. results.SelectMany(r => r)];
        }

        private async Task<IReadOnlyList<JobOffer>> SearchSafeAsync(IJobProvider provider, SearchFilter filter, CancellationToken ct)
        {
            try
            {
                return await provider.SearchAsync(filter, ct);
            }
            catch (Exception ex)
            {
                // Un provider que falla NUNCA tumba el pipeline.
                logger.LogWarning("Provider {Provider} falló: {Error}", provider.ProviderName, ex.Message);
                return [];
            }
        }

        private async Task<List<JobOffer>> SaveNewOffersAsync(IReadOnlyList<JobOffer> found, CancellationToken ct)
        {
            var newOffers = new List<JobOffer>();
            var seen = new HashSet<string>();

            foreach (var offer in found)
            {
                offer.CompanyNormalized = NormalizeCompany(offer.CompanyName);

                // Dedup dentro del propio lote (mismo provider + externalId).
                var key = $"{offer.SourceProvider}::{offer.ExternalId}";
                if (!seen.Add(key))
                {
                    continue;
                }

                // Dedup contra lo ya conocido en BD.
                if (await repository.ExistsAsync(offer.ExternalId, offer.SourceProvider, ct))
                {
                    continue;
                }

                await repository.SaveAsync(offer, ct);
                newOffers.Add(offer);
            }

            return newOffers;
        }

        private async Task<int> MarkSemanticDuplicatesAsync(List<JobOffer> newOffers, CancellationToken ct)
        {
            var duplicates = 0;

            foreach (var offer in newOffers)
            {
                try
                {
                    var text = $"{offer.Title} {offer.CompanyName} {offer.Description}";
                    var embedding = await embeddingService.GetEmbeddingAsync(text, ct);
                    offer.Embedding = embedding;

                    var recent = await repository.GetRecentByCompanyAsync(offer.CompanyNormalized!, 30, ct);

                    foreach (var other in recent)
                    {
                        if (other.Id == offer.Id || other.Embedding is not { Length: > 0 })
                        {
                            continue;
                        }

                        if (embeddingService.CosineSimilarity(embedding, other.Embedding) > DuplicateThreshold)
                        {
                            offer.IsDuplicate = true;
                            duplicates++;
                            break;
                        }
                    }

                    await repository.UpdateAsync(offer, ct);
                }
                catch (Exception ex)
                {
                    // Ollama caído: se salta TODO el paso (no bloquea el pipeline).
                    logger.LogWarning("Dedup semántico omitido (Ollama no disponible): {Error}", ex.Message);
                    break;
                }
            }

            return duplicates;
        }

        private async Task<int> AnalyzeAndScoreAsync(List<JobOffer> newOffers, CancellationToken ct)
        {
            var analyzed = 0;

            foreach (var offer in newOffers.Where(o => !o.IsDuplicate))
            {
                try
                {
                    var analysis = await aiAnalysisService.AnalyzeAsync(offer, profile, ct);

                    offer.Score = JobScoreCalculator.Calculate(offer, analysis, profile);
                    offer.IsAnalyzed = true;

                    await repository.UpdateAsync(offer, ct);
                    analyzed++;
                }
                catch (Exception ex)
                {
                    // Un error analizando una oferta no detiene al resto.
                    logger.LogWarning("Error analizando la oferta {OfferId}: {Error}", offer.Id, ex.Message);
                }
            }

            return analyzed;
        }

        // ── Normalización de empresa ────────────────────────────────────
        public static string NormalizeCompany(string company)
        {
            var lower = company.ToLowerInvariant().Trim();

            // Quitar acentos.
            var decomposed = lower.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            var text = sb.ToString().Normalize(NormalizationForm.FormC);

            // Quitar formas societarias (S.L., S.A., S.L.U., Inc, Ltd...).
            text = LegalSuffixRegex().Replace(text, " ");

            // Colapsar a caracteres simples.
            text = NonAlphanumericRegex().Replace(text, " ").Trim();

            return text;
        }

        [GeneratedRegex(@"\b(s\.?l\.?u?\.?|s\.?a\.?u?\.?|sociedad limitada|sociedad anonima|inc\.?|ltd\.?|llc|gmbh)\b")]
        private static partial Regex LegalSuffixRegex();

        [GeneratedRegex(@"[^a-z0-9]+")]
        private static partial Regex NonAlphanumericRegex();

        // ── Generación del HTML del informe ─────────────────────────────
        // internal (no private) para poder verificar desde los tests que el
        // HTML incluye el filtro interactivo y las tarjetas bien formadas.
        internal static string BuildHtml(DailyReport report)
        {
            var sb = new StringBuilder();

            sb.Append("""
                <!DOCTYPE html><html lang="es"><head><meta charset="utf-8">
                <style>
                  body{font-family:Segoe UI,Arial,sans-serif;background:#f4f5f7;color:#1f2933;margin:0;padding:24px}
                  .header{background:#111827;color:#fff;padding:20px 24px;border-radius:10px}
                  .metrics{display:flex;gap:16px;margin:16px 0;flex-wrap:wrap}
                  .metric{background:#fff;border-radius:8px;padding:12px 18px;box-shadow:0 1px 3px rgba(0,0,0,.08)}
                  .metric b{display:block;font-size:22px}
                  .card{background:#fff;border-radius:10px;padding:16px 18px;margin:12px 0;box-shadow:0 1px 3px rgba(0,0,0,.08)}
                  .card h3{margin:0 0 6px}
                  .stars{color:#f5a623;font-size:18px}
                  .meta{color:#52606d;font-size:14px;margin:6px 0}
                  .tags{color:#3b5bdb;font-size:13px}
                  .reqs{background:#f8fafc;border-left:3px solid #3b5bdb;padding:8px 12px;margin:8px 0;font-size:13px;color:#334155}
                  .reqs b{color:#1e293b}
                  .src{display:inline-block;background:#eef2ff;color:#3730a3;font-size:12px;padding:2px 8px;border-radius:999px;vertical-align:middle}
                  a{color:#2563eb;text-decoration:none}
                  .filterbar{background:#fff;border-radius:10px;padding:14px 18px;margin:12px 0;box-shadow:0 1px 3px rgba(0,0,0,.08)}
                  .filterbar label{display:block;font-size:13px;color:#52606d;margin:8px 0 4px}
                  .filterbar input{width:100%;max-width:520px;padding:8px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:14px;box-sizing:border-box}
                  .chip{display:inline-block;cursor:pointer;background:#eef2ff;color:#3730a3;font-size:12px;padding:3px 10px;border-radius:999px;margin:3px 4px 0 0;user-select:none}
                  .chip.active{background:#dc2626;color:#fff;text-decoration:line-through}
                  .hidden{display:none}
                  #filter-count{color:#52606d;font-size:13px;margin-top:10px}
                </style></head><body>
                """);

            sb.Append($"""
                <div class="header"><h1>JobHunter AI</h1>
                <div>Informe del {report.GeneratedAt:yyyy-MM-dd HH:mm} UTC</div></div>
                <div class="metrics">
                  <div class="metric"><b>{report.TotalFound}</b>encontradas</div>
                  <div class="metric"><b>{report.TotalNew}</b>nuevas</div>
                  <div class="metric"><b>{report.TotalDuplicates}</b>duplicadas</div>
                  <div class="metric"><b>{report.TotalAnalyzed}</b>analizadas</div>
                </div>
                """);

            if (report.TopOffers.Count == 0)
            {
                sb.Append("<p>No hay ofertas puntuadas todavía.</p>");
            }
            else
            {
                // Filtro interactivo del lado del navegador (JS embebido, sin
                // dependencias). Nota: en clientes de email el script no corre;
                // el filtro solo funciona viendo el informe en /reports/latest.
                sb.Append("""
                    <div class="filterbar">
                      <label for="filter-exclude">Excluir ofertas que mencionen (separado por comas):</label>
                      <input id="filter-exclude" type="text" placeholder="inglés, angular, java...">
                      <label for="filter-require">Mostrar solo ofertas que mencionen:</label>
                      <input id="filter-require" type="text" placeholder="remoto, sql server...">
                      <div id="filter-chips"></div>
                      <div id="filter-count"></div>
                    </div>
                    """);
            }

            foreach (var offer in report.TopOffers)
            {
                var score = offer.Score;
                var stars = Stars(score?.TotalScore ?? 0);
                var salary = (offer.SalaryMin, offer.SalaryMax) switch
                {
                    (null, null) => "salario no indicado",
                    (var min, null) => $"desde {min:N0} €",
                    (null, var max) => $"hasta {max:N0} €",
                    var (min, max) => $"{min:N0} – {max:N0} €",
                };

                var requirements = string.IsNullOrWhiteSpace(score?.AiRequirements)
                    ? ""
                    : $"""<div class="reqs"><b>Requisitos que piden:</b> {Encode(score!.AiRequirements!)}</div>""";

                sb.Append($"""
                    <div class="card" data-techs="{Encode(score?.AiTechList ?? "")}">
                      <div class="stars">{stars} <span style="color:#52606d;font-size:13px">({score?.TotalScore ?? 0} pts)</span></div>
                      <h3>{Encode(offer.Title)} <span class="src">{Encode(offer.SourceProvider)}</span></h3>
                      <div class="meta">{Encode(offer.CompanyName)} · {WorkModeLabel(offer.WorkMode)} · {Encode(offer.Location ?? "—")} · {salary} · publicada el {offer.PublishedAt:yyyy-MM-dd}</div>
                      <div class="tags">{Encode(score?.AiTechList ?? "")}</div>
                      <p>{Encode(score?.AiSummary ?? "")}</p>
                      {requirements}
                      <p><i>{Encode(score?.AiRecommendation ?? "")}</i></p>
                      <a href="{Encode(offer.Url)}">Ver oferta →</a>
                    </div>
                    """);
            }

            // El script del filtro: normaliza acentos en ambos lados (puedes
            // escribir "ingles" y casa con "inglés") y busca sobre TODO el
            // texto visible de cada tarjeta (título, techs, requisitos,
            // resumen, recomendación, empresa, localización).
            sb.Append("""
                <script>
                (function () {
                  var norm = function (s) {
                    return s.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
                  };
                  var cards = Array.prototype.slice.call(document.querySelectorAll('.card'));
                  var excludeInput = document.getElementById('filter-exclude');
                  var requireInput = document.getElementById('filter-require');
                  var chipsBox = document.getElementById('filter-chips');
                  var countBox = document.getElementById('filter-count');
                  if (!excludeInput || cards.length === 0) { return; }

                  var excludedChips = {};

                  var parseTerms = function (value) {
                    return value.split(',')
                      .map(function (t) { return norm(t.trim()); })
                      .filter(function (t) { return t.length > 0; });
                  };

                  var apply = function () {
                    var exclude = parseTerms(excludeInput.value);
                    for (var key in excludedChips) { if (excludedChips[key]) { exclude.push(key); } }
                    var require = parseTerms(requireInput.value);
                    var visible = 0;

                    cards.forEach(function (card) {
                      var text = norm(card.textContent);
                      var hide =
                        exclude.some(function (t) { return text.indexOf(t) !== -1; }) ||
                        require.some(function (t) { return text.indexOf(t) === -1; });
                      card.classList.toggle('hidden', hide);
                      if (!hide) { visible++; }
                    });

                    countBox.textContent = 'Mostrando ' + visible + ' de ' + cards.length + ' ofertas.';
                  };

                  // Chips con las techs detectadas por la IA (las más frecuentes):
                  // clic = excluir ofertas que la mencionen, otro clic = quitar.
                  var freq = {};
                  cards.forEach(function (card) {
                    (card.getAttribute('data-techs') || '').split(',').forEach(function (tech) {
                      var label = tech.trim();
                      if (!label) { return; }
                      var key = norm(label);
                      if (!freq[key]) { freq[key] = { label: label, count: 0 }; }
                      freq[key].count++;
                    });
                  });
                  Object.keys(freq)
                    .sort(function (a, b) { return freq[b].count - freq[a].count; })
                    .slice(0, 15)
                    .forEach(function (key) {
                      var chip = document.createElement('span');
                      chip.className = 'chip';
                      chip.textContent = freq[key].label + ' (' + freq[key].count + ')';
                      chip.title = 'Clic para excluir ofertas que mencionen esta tecnología';
                      chip.addEventListener('click', function () {
                        excludedChips[key] = !excludedChips[key];
                        chip.classList.toggle('active', !!excludedChips[key]);
                        apply();
                      });
                      chipsBox.appendChild(chip);
                    });

                  excludeInput.addEventListener('input', apply);
                  requireInput.addEventListener('input', apply);
                  apply();
                })();
                </script>
                """);

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string WorkModeLabel(WorkMode mode) => mode switch
        {
            WorkMode.Remote => "Remoto",
            WorkMode.Hybrid => "Híbrido",
            WorkMode.OnSite => "Presencial",
            _ => "modo n/d",
        };

        private static string Stars(int totalScore)
        {
            var count = Math.Clamp((int)Math.Round(totalScore / 20.0), 0, 5);
            return new string('★', count) + new string('☆', 5 - count);
        }

        private static string Encode(string value) =>
            System.Net.WebUtility.HtmlEncode(value);
    }
}
