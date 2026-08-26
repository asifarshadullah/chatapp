namespace Chat.Identity.Application.Interfaces;

/// <summary>
/// How long a refresh token stays exchangeable. Defined here because the identity service
/// needs it; Infrastructure binds it from configuration.
///
/// Two lifetimes rather than one because the user chooses at sign-in how long their session
/// may be continued; which of these applies is a property of the session, not of the
/// deployment.
/// </summary>
public interface IRefreshTokenSettings
{
    /// <summary>The lifetime for a session the user did not ask to be remembered.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>The lifetime for a session the user asked to stay signed in to.</summary>
    TimeSpan PersistentLifetime { get; }

    /// <summary>The lifetime that applies to a session, given the user's choice.</summary>
    TimeSpan LifetimeFor(bool persistent) => persistent ? PersistentLifetime : Lifetime;
}
