using Chat.Identity.Application.Interfaces;

namespace Chat.Identity.Infrastructure.Configuration;

/// <summary>
/// Refresh-token policy, bound from the "RefreshToken" configuration section.
/// Implements the IRefreshTokenSettings contract defined in Application, so the service
/// layer never sees the Options machinery.
/// </summary>
public class RefreshTokenSettings : IRefreshTokenSettings
{
    /// <summary>How long a refresh token stays exchangeable.</summary>
    public int LifetimeDays { get; set; } = 14;

    /// <summary>
    /// How close to expiry an access token may get before the client renews it. Exposed to
    /// the frontend so the two agree on when a token counts as stale.
    /// </summary>
    public int RenewalMarginSeconds { get; set; } = 300;

    public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);
}
