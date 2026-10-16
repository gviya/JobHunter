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
/// todas las fuentes, deduplicar (exacto, por empresa + título y
/// semántico), analizar con IA, puntuar y generar el informe.
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

        // Días hacia atrás en los que una oferta de la misma empresa cuenta
        // como "ya vista" para los dos dedups (por título y semántico).
        private const int DuplicateLookbackDays = 30;

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

            // Ventana del informe y de los reintentos de análisis: lo
            // descubierto antes se da por visto (o caducado).
            var windowDays = Math.Max(1, profile.ReportWindowDays);
            var since = report.GeneratedAt.AddDays(-windowDays);

            // ── 1. BUSCAR (todos los providers en paralelo; los que no
            //        filtran por keyword pasan antes el filtro de título) ──
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

            // ── 2. DEDUP EXACTO + POR EMPRESA Y TÍTULO + guardar nuevas ─
            var (newOffers, titleDuplicates) = await SaveNewOffersAsync(commutableOffers, since, cancellationToken);
            report.TotalNew = newOffers.Count;

            // ── 3. DEDUP SEMÁNTICO (embeddings de Ollama) ───────────────
            var semanticDuplicates = await MarkSemanticDuplicatesAsync(newOffers, since, cancellationToken);
            report.TotalDuplicates = titleDuplicates + semanticDuplicates;

            // ── 4 + 5. ANALIZAR + PUNTUAR (nuevas + pendientes de
            //           ejecuciones anteriores) ────────────────────────────
            report.TotalAnalyzed = await AnalyzeAndScoreAsync(newOffers, since, cancellationToken);

            // ── 6. INFORME (top 15 de la ventana + HTML) ────────────────
            var top = await repository.GetTopScoredAsync(since, 15, cancellationToken);
            report.TopOffers = [.. top];
            report.HtmlContent = BuildHtml(report, windowDays);

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
                var offers = await provider.SearchAsync(filter, ct);

                if (provider.FiltersByKeyword || offers.Count == 0)
                {
                    return offers;
                }

                // Board completo de la empresa: se descarta lo que por título
                // no es un puesto técnico ANTES de guardarlo y analizarlo.
                var relevant = RelevanceFilter.Apply(offers, filter.Keywords, profile);
                logger.LogInformation(
                    "{Provider}: {Kept}/{Total} ofertas con título relevante (no filtra por keyword en origen).",
                    provider.ProviderName, relevant.Count, offers.Count);

                return relevant;
            }
            catch (Exception ex)
            {
                // Un provider que falla NUNCA tumba el pipeline.
                logger.LogWarning("Provider {Provider} falló: {Error}", provider.ProviderName, ex.Message);
                return [];
            }
        }

        /// <summary>
        /// Dedup exacto + dedup por empresa y título, y guarda las nuevas.
        /// Devuelve TODAS las guardadas (duplicadas incluidas) y cuántas de
        /// ellas se marcaron como duplicadas por título.
        /// </summary>
        private async Task<(List<JobOffer> Saved, int TitleDuplicates)> SaveNewOffersAsync(
            IReadOnlyList<JobOffer> found, DateTime since, CancellationToken ct)
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

                newOffers.Add(offer);
            }

            // Antes de guardar: así las duplicadas llegan a BD ya marcadas.
            var titleDuplicates = await MarkSameTitleDuplicatesAsync(newOffers, since, ct);

            // Las duplicadas también se guardan: mañana el dedup exacto las
            // reconoce por (ExternalId, SourceProvider) y no vuelven a pasar
            // por aquí. Con IsDuplicate = true nunca se analizan
            // (GetUnanalyzedAsync las excluye) ni salen en el top.
            foreach (var offer in newOffers)
            {
                await repository.SaveAsync(offer, ct);
            }

            return (newOffers, titleDuplicates);
        }

        // Claves normalizadas de los marcadores que ponen los providers cuando
        // la fuente no da empresa o título: "(empresa desconocida)" normaliza
        // a "empresa desconocida", que NO es vacío, así que comprobar solo la
        // longitud no basta para saber que no hay dato.
        private static readonly string UnknownCompanyKey = NormalizeCompany(JobOffer.UnknownCompany);
        private static readonly string UntitledTitleKey = NormalizeTitle(JobOffer.UntitledTitle);

        // Sin empresa no hay clave fiable para ningún dedup por empresa: dos
        // "Backend Developer" de empresa desconocida no son necesariamente la
        // misma oferta. "Sin" es tanto lo que queda vacío tras normalizar como
        // el marcador del provider; si el marcador contara como empresa,
        // todas las ofertas anónimas con un título genérico (justo los que
        // busca el candidato) se fusionarían en una, en el lote y durante los
        // días del lookback contra BD.
        private static bool HasCompanyKey(string? companyNormalized)
            => companyNormalized is { Length: > 0 } c && c != UnknownCompanyKey;

        // Solo una fila que aún representa a la oferta puede suprimir otra.
        // Una duplicada nunca se analiza (y, guardada con un DiscoveredAt
        // nuevo, alargaría la supresión copia a copia) y una sin analizar de
        // fuera de la ventana ya no se reintenta: casar contra ellas suprimía
        // la oferta para siempre sin que ninguna copia llegara a puntuarse.
        private static bool IsLiveRepresentative(JobOffer stored, DateTime since)
            => !stored.IsDuplicate && (stored.IsAnalyzed || stored.DiscoveredAt >= since);

        // "Conmutable" para los dedups: el candidato puede coger la oferta
        // por su ubicación. Una remota lo es siempre (la ubicación no importa),
        // y así las copias de la misma remota con "Madrid" en una fuente y
        // "España" en otra se reconocen. En filas guardadas el modo puede ser
        // ya el corregido por la IA.
        private bool IsCommutableForDedup(JobOffer offer)
            => offer.WorkMode == WorkMode.Remote || CommuteFilter.IsCommutable(offer.Location, profile);

        // Regla asimétrica de los dos dedups: una representante no conmutable
        // NUNCA suprime una oferta conmutable (las consultoras publican el
        // mismo título y el mismo texto por oficina, y la de Barcelona tiraba
        // la de Madrid, la única que el candidato puede coger). En cualquier
        // otro caso sí: una clave simétrica también separaba las copias entre
        // fuentes de la misma remota y cada una se analizaba y se enviaba.
        private static bool CanSuppress(bool representativeCommutable, bool offerCommutable)
            => representativeCommutable || !offerCommutable;

        /// <summary>
        /// Dedup determinista por (empresa normalizada, título normalizado):
        /// la misma oferta publicada en Adzuna + JSearch + Jooble, o
        /// republicada por la empresa con otro id, se analizaría (IA de pago)
        /// y saldría en el top dos o tres veces. El dedup semántico también
        /// la detecta, pero depende de Ollama y se salta entero si no está
        /// levantado; este paso es barato y funciona siempre.
        ///
        /// Conservador: solo casa títulos idénticos tras normalizar, y la
        /// normalización de empresa no siempre coincide entre fuentes
        /// (Greenhouse usa el token del board como nombre), así que algunos
        /// duplicados entre fuentes se escapan; para esos queda el semántico.
        /// Compromiso asumido (el mismo que ya hace el semántico): dos
        /// vacantes con el mismo título en la misma empresa dentro de
        /// <see cref="DuplicateLookbackDays"/> días cuentan como una sola...
        ///
        /// ...con la regla de <see cref="CanSuppress"/>: en el lote se queda
        /// como representante la conmutable (<see cref="IsCommutableForDedup"/>)
        /// antes que la más informativa, y contra BD una fila no conmutable no
        /// suprime una oferta conmutable. Así la vacante de Madrid nunca se
        /// pierde frente a su gemela de Barcelona, y las copias de una misma
        /// remota con ubicaciones distintas siguen fusionándose.
        /// </summary>
        private async Task<int> MarkSameTitleDuplicatesAsync(
            List<JobOffer> newOffers, DateTime since, CancellationToken ct)
        {
            // Sin empresa o sin título (vacío o el marcador "(sin título)")
            // no hay clave fiable: esas ofertas no entran en este dedup.
            var keyed = newOffers
                .Where(o => HasCompanyKey(o.CompanyNormalized))
                .Select(o => (Offer: o, Title: NormalizeTitle(o.Title), Commutable: IsCommutableForDedup(o)))
                .Where(x => x.Title.Length > 0 && x.Title != UntitledTitleKey)
                .ToList();

            // 1) Dentro del lote, en memoria (nada del lote está aún en BD).
            //    Primero la conmutable: si queda alguna, la representante lo
            //    es y suprimir al resto cumple la regla asimétrica. Después la
            //    más informativa: la descripción completa del ATS vale más que
            //    el snippet de un agregador. OrderBy es estable: a igualdad,
            //    gana la primera vista.
            var inBatch = 0;
            foreach (var group in keyed.GroupBy(x => (x.Offer.CompanyNormalized!, x.Title)))
            {
                var kept = group
                    .OrderByDescending(x => x.Commutable)
                    .ThenByDescending(x => x.Offer.Description?.Length ?? 0)
                    .First().Offer;

                foreach (var (offer, _, _) in group)
                {
                    if (!ReferenceEquals(offer, kept))
                    {
                        offer.IsDuplicate = true;
                        inBatch++;
                    }
                }
            }

            // 2) Contra lo reciente en BD, con una consulta por empresa.
            var recentByCompany = new Dictionary<string, List<(Guid Id, string Title, bool Commutable)>>();
            var againstDb = 0;

            foreach (var (offer, title, commutable) in keyed.Where(x => !x.Offer.IsDuplicate))
            {
                var company = offer.CompanyNormalized!;
                if (!recentByCompany.TryGetValue(company, out var recent))
                {
                    var stored = await repository.GetRecentByCompanyAsync(company, DuplicateLookbackDays, ct);
                    recent = [.. stored
                        .Where(o => IsLiveRepresentative(o, since))
                        .Select(o => (o.Id, NormalizeTitle(o.Title), IsCommutableForDedup(o)))];
                    recentByCompany[company] = recent;
                }

                if (recent.Any(r => r.Id != offer.Id && r.Title == title && CanSuppress(r.Commutable, commutable)))
                {
                    offer.IsDuplicate = true;
                    againstDb++;
                }
            }

            logger.LogInformation(
                "Dedup por empresa + título: {Total} duplicadas ({InBatch} dentro del lote, {AgainstDb} ya vistas en los últimos {Days} días).",
                inBatch + againstDb, inBatch, againstDb, DuplicateLookbackDays);

            return inBatch + againstDb;
        }

        /// <summary>
        /// Dedup semántico con las mismas reglas que el de título: sin clave
        /// de empresa no compara, solo cuentan las representantes vivas
        /// (<see cref="IsLiveRepresentative"/>) y una no conmutable nunca
        /// suprime una conmutable (<see cref="CanSuppress"/>). Sin ellas, con
        /// Ollama levantado se repetían las pérdidas que el dedup por título
        /// ya evitaba: el texto de plantilla de la vacante de Madrid se parece
        /// por encima del umbral al de su gemela de Barcelona.
        /// </summary>
        private async Task<int> MarkSemanticDuplicatesAsync(List<JobOffer> newOffers, DateTime since, CancellationToken ct)
        {
            var duplicates = 0;

            // Las conmutables primero (OrderBy es estable): como en el lote del
            // dedup por título, si dos gemelas llegan juntas la representante
            // es la conmutable y la otra se marca contra ella, no al revés.
            foreach (var offer in newOffers.OrderByDescending(IsCommutableForDedup))
            {
                // Ya marcada por el dedup de título: no hace falta gastar
                // una llamada de embeddings para confirmarlo. Sin empresa
                // tampoco: no hay con qué compararla de forma fiable.
                if (offer.IsDuplicate || !HasCompanyKey(offer.CompanyNormalized))
                {
                    continue;
                }

                try
                {
                    var text = $"{offer.Title} {offer.CompanyName} {offer.Description}";
                    var embedding = await embeddingService.GetEmbeddingAsync(text, ct);
                    offer.Embedding = embedding;

                    var commutable = IsCommutableForDedup(offer);
                    var recent = await repository.GetRecentByCompanyAsync(offer.CompanyNormalized!, DuplicateLookbackDays, ct);

                    foreach (var other in recent)
                    {
                        if (other.Id == offer.Id
                            || other.Embedding is not { Length: > 0 }
                            || !IsLiveRepresentative(other, since)
                            || !CanSuppress(IsCommutableForDedup(other), commutable))
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

        private async Task<int> AnalyzeAndScoreAsync(List<JobOffer> newOffers, DateTime since, CancellationToken ct)
        {
            // No basta con las nuevas de hoy: una oferta cuyo análisis falló
            // en otra ejecución (IA caída, créditos agotados...) ya existe en
            // BD, así que la búsqueda no la vuelve a dar por nueva y se
            // perdería. Se piden a BD todas las pendientes de la ventana (las
            // nuevas de hoy incluidas); pasada la ventana se dan por caducadas,
            // lo que acota también lo que puede costar una que falle siempre.
            var pending = await repository.GetUnanalyzedAsync(since, ct);

            var newIds = newOffers.Select(o => o.Id).ToHashSet();
            var retries = pending.Count(o => !newIds.Contains(o.Id));
            if (retries > 0)
            {
                logger.LogInformation(
                    "Análisis: {Retries} ofertas pendientes de ejecuciones anteriores se reintentan.", retries);
            }

            // "analyzed" cuenta todo lo que pasó por la IA (TotalAnalyzed del
            // informe), descartadas tras el análisis incluidas: se gastó la
            // llamada igual.
            var analyzed = 0;
            var discardedAfterAnalysis = 0;

            foreach (var offer in pending)
            {
                try
                {
                    var analysis = await aiAnalysisService.AnalyzeAsync(offer, profile, ct);

                    AiSignals.Apply(offer, analysis);

                    // CommuteFilter corrió con el modo que estimó el provider;
                    // la IA puede revelar ahora que la oferta "remota" es
                    // híbrida o presencial en un modo o sitio que el perfil no
                    // acepta. Se descarta igual que lo habría hecho el filtro
                    // previo: una penalización fija no basta para hundirla
                    // (el resto de términos puede sumar más de +100) y llegaba
                    // a encabezar el top y a disparar el email. Se marca como
                    // analizada para no reintentarla, y sin Score
                    // GetTopScoredAsync nunca la incluye.
                    if (!CommuteFilter.IsAcceptable(offer, profile))
                    {
                        offer.IsAnalyzed = true;
                        await repository.UpdateAsync(offer, ct);
                        analyzed++;
                        discardedAfterAnalysis++;

                        logger.LogInformation(
                            "Oferta {OfferId} descartada tras el análisis: la IA la lee como {WorkMode} en {Location}, que el perfil no acepta.",
                            offer.Id, offer.WorkMode, offer.Location ?? "(sin ubicación)");
                        continue;
                    }

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

            if (discardedAfterAnalysis > 0)
            {
                logger.LogInformation(
                    "Análisis: {Discarded} de {Analyzed} ofertas analizadas descartadas porque la IA reveló un modo de trabajo que el perfil no acepta.",
                    discardedAfterAnalysis, analyzed);
            }

            return analyzed;
        }

        // ── Normalización de empresa ────────────────────────────────────
        public static string NormalizeCompany(string company)
        {
            var text = RemoveDiacritics(company.ToLowerInvariant().Trim());

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

        // ── Normalización de título (clave del dedup por título) ────────
        /// <summary>
        /// Título comparable entre fuentes: minúsculas, sin acentos, sin
        /// marcadores de género (que unas fuentes ponen y otras no) y con la
        /// puntuación colapsada a espacios. Conserva '#' y '+' para no
        /// confundir C#, C++ y C. Es conservadora a propósito: el resto del
        /// contenido entre paréntesis se mantiene, porque "(Senior)" y
        /// "(Junior)" son puestos distintos.
        /// </summary>
        public static string NormalizeTitle(string title)
        {
            var text = RemoveDiacritics(title.ToLowerInvariant().Trim());

            text = GenderMarkerRegex().Replace(text, " ");
            text = NonTitleCharRegex().Replace(text, " ").Trim();

            return text;
        }

        // Se aplica sobre texto ya en minúsculas y sin acentos. Casa:
        // - letras de género entre paréntesis o corchetes: (m/f/d), (m/w/d),
        //   [h/m], (f/m/x), (m/f/div)... ("(Senior)" no: exige letras sueltas
        //   separadas por '/');
        // - las mismas de tres partes sin paréntesis ("m/w/d"), y
        //   "(all genders)" / "(gn)";
        // - el "/a", "/as", "/o", "(a)" del español, solo pegado a una palabra
        //   de 2+ letras ("desarrollador/a"; "i/o" se queda como está).
        [GeneratedRegex(
            @"[(\[]\s*[mfwhdx]\s*(?:/\s*(?:[mfwhdx]|div|nb)\s*){1,3}[)\]]|\b[mfwh]/[mfwh]/(?:[dx]|div)\b|\(\s*(?:all genders|gn\*?)\s*\)|(?<=[a-z]{2})(?:/(?:as|os|a|o)|\((?:as|os|a|o)\))(?![a-z0-9])")]
        private static partial Regex GenderMarkerRegex();

        [GeneratedRegex(@"[^a-z0-9#+]+")]
        private static partial Regex NonTitleCharRegex();

        private static string RemoveDiacritics(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // ── Generación del HTML del informe ─────────────────────────────
        // internal (no private) para poder verificar desde los tests que el
        // HTML incluye el filtro interactivo y las tarjetas bien formadas.
        internal static string BuildHtml(DailyReport report, int windowDays)
        {
            var sb = new StringBuilder();

            // Diseño pensado para los DOS destinos del informe: clientes de
            // email (CSS en <head>, sin recursos externos, degradación
            // aceptable si el cliente ignora flex) y navegador (/reports/latest,
            // donde además corre el filtro interactivo). Colores sobre tokens
            // de tinta/superficie neutros; el color de estado (verde/ámbar)
            // nunca va solo: siempre acompaña a un número o etiqueta.
            sb.Append("""
                <!DOCTYPE html><html lang="es"><head><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>JobHunter AI — informe diario</title>
                <style>
                  body{font-family:system-ui,-apple-system,'Segoe UI',sans-serif;background:#f9f9f7;color:#0b0b0b;margin:0;padding:20px 14px}
                  .wrap{max-width:880px;margin:0 auto}
                  .header{background:#111827;color:#fff;padding:22px 24px;border-radius:12px}
                  .header h1{margin:0;font-size:22px;letter-spacing:.2px}
                  .header .sub{color:#c3c2b7;font-size:13.5px;margin-top:4px}
                  .metrics{display:flex;gap:12px;margin:14px 0;flex-wrap:wrap}
                  .metric{flex:1 1 120px;background:#fcfcfb;border:1px solid rgba(11,11,11,.10);border-radius:10px;padding:12px 16px}
                  .metric b{display:block;font-size:24px;font-weight:600;line-height:1.2}
                  .metric span{color:#52514e;font-size:12.5px}
                  .card{background:#fcfcfb;border:1px solid rgba(11,11,11,.10);border-radius:12px;padding:16px 18px;margin:12px 0}
                  .cardtop{display:flex;justify-content:space-between;gap:14px;align-items:flex-start;flex-wrap:wrap}
                  .card h3{margin:0;font-size:16.5px;line-height:1.35}
                  .card h3 a{color:#0b0b0b;text-decoration:none}
                  .card h3 a:hover{color:#1c5cab;text-decoration:underline}
                  .scorebox{text-align:right;white-space:nowrap}
                  .score{display:inline-block;font-size:15px;font-weight:600;padding:3px 10px;border-radius:999px}
                  .score.high{background:rgba(12,163,12,.14);color:#006300}
                  .score.mid{background:rgba(250,178,25,.18);color:#7a5200}
                  .score.low{background:#f0efec;color:#52514e}
                  .stars{color:#c98500;font-size:14px;margin-top:3px;letter-spacing:1px}
                  .meta{color:#52514e;font-size:13.5px;margin:6px 0 10px}
                  .rowtags{margin:0 0 6px}
                  .tag{display:inline-block;font-size:12px;padding:2px 9px;border-radius:999px;margin:0 5px 5px 0}
                  .tag.mode-remote{background:rgba(12,163,12,.12);color:#006300}
                  .tag.mode-hybrid{background:rgba(250,178,25,.18);color:#7a5200}
                  .tag.mode-onsite{background:rgba(236,131,90,.18);color:#8c3512}
                  .tag.mode-unknown{background:#f0efec;color:#52514e}
                  .tag.seniority{background:rgba(42,120,214,.12);color:#1c5cab}
                  .tag.src{background:#fcfcfb;border:1px solid rgba(11,11,11,.14);color:#52514e}
                  .tag.new{background:#111827;color:#fff;font-weight:600}
                  .tech{display:inline-block;background:#eef2fb;color:#1c5cab;font-size:12px;padding:2px 9px;border-radius:6px;margin:0 4px 4px 0}
                  .reqs{background:#f5f4f1;border-left:3px solid #2a78d6;border-radius:0 8px 8px 0;padding:9px 12px;margin:10px 0;font-size:13px;color:#3d3c39}
                  .reqs b{color:#0b0b0b}
                  .summary{margin:8px 0;font-size:14px;line-height:1.55}
                  .reco{color:#52514e;font-size:13.5px;font-style:italic;margin:8px 0 4px}
                  .cta{display:inline-block;margin-top:4px;font-size:13.5px;color:#1c5cab;font-weight:600;text-decoration:none}
                  .cta:hover{text-decoration:underline}
                  .filterbar{background:#fcfcfb;border:1px solid rgba(11,11,11,.10);border-radius:12px;padding:14px 18px;margin:12px 0}
                  .filterbar .cols{display:flex;gap:14px;flex-wrap:wrap}
                  .filterbar .col{flex:1 1 240px}
                  .filterbar label{display:block;font-size:12.5px;color:#52514e;margin:2px 0 4px}
                  .filterbar input{width:100%;padding:8px 10px;border:1px solid #c3c2b7;border-radius:8px;font-size:14px;box-sizing:border-box;background:#fff;color:#0b0b0b}
                  .filterbar input:focus{outline:2px solid #2a78d6;outline-offset:0;border-color:#2a78d6}
                  .chip{display:inline-block;cursor:pointer;background:#eef2fb;color:#1c5cab;font-size:12px;padding:3px 10px;border-radius:999px;margin:6px 6px 0 0;user-select:none}
                  .chip.active{background:#d03b3b;color:#fff;text-decoration:line-through}
                  .hidden{display:none}
                  #filter-count{color:#52514e;font-size:12.5px;margin-top:8px}
                  .empty{background:#fcfcfb;border:1px dashed rgba(11,11,11,.18);border-radius:12px;padding:22px;text-align:center;color:#52514e;margin:14px 0}
                  .foot{color:#898781;font-size:12px;text-align:center;margin:18px 0 6px}
                </style></head><body><div class="wrap">
                """);

            var newCount = report.TopOffers.Count(report.IsNewInThisRun);

            sb.Append($"""
                <div class="header"><h1>JobHunter AI</h1>
                <div class="sub">Informe del {report.GeneratedAt:yyyy-MM-dd HH:mm} UTC · top {report.TopOffers.Count} ofertas de los últimos {windowDays} días · {newCount} nuevas</div></div>
                <div class="metrics">
                  <div class="metric"><b>{report.TotalFound}</b><span>Encontradas</span></div>
                  <div class="metric"><b>{report.TotalNew}</b><span>Nuevas</span></div>
                  <div class="metric"><b>{report.TotalDuplicates}</b><span>Duplicadas</span></div>
                  <div class="metric"><b>{report.TotalAnalyzed}</b><span>Analizadas</span></div>
                </div>
                """);

            if (report.TopOffers.Count == 0)
            {
                sb.Append($"""<div class="empty">No hay ofertas puntuadas en los últimos {windowDays} días.</div>""");
            }
            else
            {
                // Filtro interactivo del lado del navegador (JS embebido, sin
                // dependencias). Nota: en clientes de email el script no corre;
                // el filtro solo funciona viendo el informe en /reports/latest.
                sb.Append("""
                    <div class="filterbar">
                      <div class="cols">
                        <div class="col">
                          <label for="filter-exclude">Excluir ofertas que mencionen (separado por comas)</label>
                          <input id="filter-exclude" type="text" placeholder="inglés, angular, java...">
                        </div>
                        <div class="col">
                          <label for="filter-require">Mostrar solo ofertas que mencionen</label>
                          <input id="filter-require" type="text" placeholder="remoto, sql server...">
                        </div>
                      </div>
                      <div id="filter-chips"></div>
                      <div id="filter-count"></div>
                    </div>
                    """);
            }

            foreach (var offer in report.TopOffers)
            {
                var score = offer.Score;
                var total = score?.TotalScore ?? 0;
                var salary = (offer.SalaryMin, offer.SalaryMax) switch
                {
                    (null, null) => "salario no indicado",
                    (var min, null) => $"desde {min:N0} €",
                    (null, var max) => $"hasta {max:N0} €",
                    var (min, max) => $"{min:N0} – {max:N0} €",
                };

                var seniority = score?.AiSeniority is { Length: > 0 } s && s != "Unknown"
                    ? $"""<span class="tag seniority">{Encode(s)}</span>"""
                    : "";

                var techChips = TechChips(score?.AiTechList);
                var techRow = techChips.Length > 0 ? $"""<div class="rowtags">{techChips}</div>""" : "";

                var summary = string.IsNullOrWhiteSpace(score?.AiSummary)
                    ? ""
                    : $"""<p class="summary">{Encode(score!.AiSummary!)}</p>""";

                var requirements = string.IsNullOrWhiteSpace(score?.AiRequirements)
                    ? ""
                    : $"""<div class="reqs"><b>Requisitos que piden:</b> {Encode(score!.AiRequirements!)}</div>""";

                var recommendation = string.IsNullOrWhiteSpace(score?.AiRecommendation)
                    ? ""
                    : $"""<p class="reco">{Encode(score!.AiRecommendation!)}</p>""";

                var href = SafeHref(offer.Url);

                // Marca lo puntuado en esta ejecución: el resto ya salió en
                // informes anteriores de la ventana.
                var newTag = report.IsNewInThisRun(offer) ? """<span class="tag new">Nueva</span>""" : "";

                sb.Append($"""
                    <div class="card" data-techs="{Encode(score?.AiTechList ?? "")}">
                      <div class="cardtop">
                        <div>
                          <h3><a href="{href}" target="_blank" rel="noopener noreferrer">{Encode(offer.Title)}</a></h3>
                          <div class="meta">{Encode(offer.CompanyName)} · {Encode(offer.Location ?? "—")} · {salary} · publicada el {offer.PublishedAt:yyyy-MM-dd}</div>
                        </div>
                        <div class="scorebox"><span class="score {ScoreBand(total)}">{total} pts</span><div class="stars">{Stars(total)}</div></div>
                      </div>
                      <div class="rowtags">
                        {newTag}
                        <span class="tag mode-{ModeClass(offer.WorkMode)}">{WorkModeLabel(offer.WorkMode)}</span>
                        {seniority}
                        <span class="tag src">{Encode(offer.SourceProvider)}</span>
                      </div>
                      {techRow}
                      {summary}
                      {requirements}
                      {recommendation}
                      <a class="cta" href="{href}" target="_blank" rel="noopener noreferrer">Ver oferta →</a>
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

            sb.Append("""<div class="foot">Generado por JobHunter AI · pipeline diario</div></div></body></html>""");
            return sb.ToString();
        }

        private static string WorkModeLabel(WorkMode mode) => mode switch
        {
            WorkMode.Remote => "Remoto",
            WorkMode.Hybrid => "Híbrido",
            WorkMode.OnSite => "Presencial",
            _ => "modo n/d",
        };

        private static string ModeClass(WorkMode mode) => mode switch
        {
            WorkMode.Remote => "remote",
            WorkMode.Hybrid => "hybrid",
            WorkMode.OnSite => "onsite",
            _ => "unknown",
        };

        // Bandas de la puntuación total para colorear el badge. Los umbrales
        // salen de la escala real de JobScoreCalculator (top realista ~80-140):
        // ≥90 encaje claro, 55-89 interesante con peros, <55 flojo.
        private static string ScoreBand(int totalScore) => totalScore switch
        {
            >= 90 => "high",
            >= 55 => "mid",
            _ => "low",
        };

        /// <summary>Convierte "C#, .NET, SQL" en chips individuales encodeados.</summary>
        private static string TechChips(string? techList)
        {
            if (string.IsNullOrWhiteSpace(techList))
            {
                return "";
            }

            var chips = techList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => $"""<span class="tech">{Encode(t)}</span>""");

            return string.Concat(chips);
        }

        private static string Stars(int totalScore)
        {
            var count = Math.Clamp((int)Math.Round(totalScore / 20.0), 0, 5);
            return new string('★', count) + new string('☆', 5 - count);
        }

        private static string Encode(string value) =>
            System.Net.WebUtility.HtmlEncode(value);

        // Las URLs de las ofertas vienen de terceros (agregadores que indexan
        // fuentes arbitrarias): HtmlEncode NO neutraliza esquemas peligrosos
        // como "javascript:" en un href. Solo se aceptan http/https absolutas;
        // cualquier otra cosa se degrada a "#" para evitar XSS al abrir el
        // informe en el navegador (/reports/latest).
        private static string SafeHref(string? url)
        {
            if (!string.IsNullOrWhiteSpace(url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return Encode(url);
            }

            return "#";
        }
    }
}
