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

    /// <summary>
    /// The same, for a user who asked to stay signed in. Longer because that user has
    /// weighed the exposure against the nuisance of signing in on their own device and
    /// chosen; rotation and replay detection guard the window either way.
    /// </summary>
    public int PersistentLifetimeDays { get; set; } = 30;

    /// <summary>
    /// How long a consumed credential may still be presented before that counts as a replay.
    /// Two seconds because the collision being tolerated is two exchanges overlapping in
    /// flight, which is a matter of milliseconds — the clients share one credential store, so
    /// none of them can hold a stale credential for longer than a response takes to arrive.
    /// </summary>
    public int GraceWindowSeconds { get; set; } = 2;

    public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);

    public TimeSpan GraceWindow => TimeSpan.FromSeconds(GraceWindowSeconds);

    public TimeSpan PersistentLifetime => TimeSpan.FromDays(PersistentLifetimeDays);
}
