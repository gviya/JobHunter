namespace RenewableEnergy.Application.Features.Auth.Commands.Login
{
    /// <summary>DTO de salida del login: el token y sus metadatos.</summary>
    public sealed record LoginResponse
    {
        public string Token { get; init; } = string.Empty;
        public DateTime ExpiraEnUtc { get; init; }
        public string Email { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
    }
}
