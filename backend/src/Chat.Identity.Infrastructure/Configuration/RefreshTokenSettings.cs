using Chat.Identity.Application.Interfaces;

namespace Chat.Identity.Infrastructure.Configuration;

/// <summary>
/// Refresh-token policy, bound from the "RefreshToken" configuration section.
/// Implements the IRefreshTokenSettings contract defined in Application, so the service
/// layer never sees the Options machinery.
/// </summary>
public class RefreshTokenSettings : IRefreshTokenSettings
{
    /// <summary>
    /// How long a refresh token stays exchangeable, and so how long a session can be
    /// continued without signing in again. Short because a refresh token is the one
    /// long-lived credential in the system: it is revocable, but only once its theft has
    /// been noticed.
    /// </summary>
    public int LifetimeDays { get; set; } = 1;

    public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);
}
