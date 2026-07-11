namespace RenewableEnergy.Application.Common
{
    /// <summary>
    /// Claves de caché CENTRALIZADAS.
    ///
    /// ¿POR QUÉ EXISTE?
    /// Si cada handler construyera su clave con strings sueltos
    /// ("proyecto-" + id en uno, "proyectos:" + id en otro), la invalidación
    /// fallaría silenciosamente. Con un único punto de construcción, quien
    /// escribe en caché y quien invalida usan EXACTAMENTE la misma clave.
    ///
    /// La caché física es Redis en producción y memoria en desarrollo
    /// (se decide en Infrastructure según configuración).
    /// </summary>
    public static class CacheKeys
    {
        /// <summary>Detalle de un proyecto: lo escribe ObtenerProyectoPorIdHandler
        /// y lo invalidan los commands que modifican el proyecto.</summary>
        public static string ProyectoDetalle(Guid proyectoId)
        {
            return $"proyecto:detalle:{proyectoId}";
        }
    }
}
