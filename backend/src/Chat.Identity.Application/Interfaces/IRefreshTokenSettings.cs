namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// How long a refresh token stays exchangeable. Defined here because the identity service
/// needs it; Infrastructure binds it from configuration.
/// </summary>
public interface IRefreshTokenSettings
{
    TimeSpan Lifetime { get; }
}
