namespace RenewableEnergy.API.Dto.Auth
{
    /// <summary>DTO de entrada del POST /api/v1/auth/login.</summary>
    public sealed record LoginRequest(string Email, string Password);
}
