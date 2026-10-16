using JobHunter.Domain.Entities;
using JobHunter.Domain.Interfaces;

namespace JobHunter.Application.Features.Pipeline;

/// <summary>
/// Vuelca sobre la oferta las señales que la IA deduce leyendo el texto
/// (cliente final y modo de trabajo real). Lo que pone cada provider depende
/// de la FUENTE, no de la oferta: Adzuna/JSearch/Jooble nunca marcan cliente
/// final (−10 fijo frente a +20, 30 puntos decididos por el origen), algunas
/// "empresas objetivo" de Greenhouse son en realidad consultoras, y el modo
/// de trabajo sale de una heurística por palabras clave que falla con
/// frases como "no es remoto".
///
/// Un valor definido de la IA manda; si la IA no lo sabe (null o Unknown)
/// se conserva el del provider. Se aplica ANTES de puntuar para que la
/// puntuación, la oferta persistida y las etiquetas del informe muestren el
/// valor corregido.
/// </summary>
internal static class AiSignals
{
    public static void Apply(JobOffer offer, AiAnalysisResult analysis)
    {
        if (analysis.WorkMode is { } workMode && workMode != WorkMode.Unknown)
        {
            offer.WorkMode = workMode;
        }

        if (analysis.IsClientFinal is { } isClientFinal)
        {
            offer.IsClientFinal = isClientFinal;
        }
    }
}
