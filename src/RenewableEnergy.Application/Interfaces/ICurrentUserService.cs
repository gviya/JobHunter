namespace RenewableEnergy.Application.Interfaces
{
    /// <summary>
    /// Abstracción de "¿quién está haciendo esta petición?".
    ///
    /// ¿POR QUÉ EXISTE?
    /// Los handlers a veces necesitan el usuario actual (p. ej. para asignar
    /// PropietarioId al crear un proyecto), pero NO pueden acceder a
    /// HttpContext: eso es tecnología web y Application debe funcionar igual
    /// desde una API, un worker o un test.
    ///
    /// La implementación real (CurrentUserService, en Infrastructure/Identity)
    /// lee los claims del JWT vía IHttpContextAccessor. En los tests se mockea
    /// con dos líneas.
    /// </summary>
    public interface ICurrentUserService
    {
        /// <summary>Id del usuario autenticado, o null si es anónimo.</summary>
        Guid? UserId { get; }

        /// <summary>Email del usuario autenticado, o null si es anónimo.</summary>
        string? Email { get; }

        /// <summary>True si la petición lleva un JWT válido.</summary>
        bool EstaAutenticado { get; }
    }
}
