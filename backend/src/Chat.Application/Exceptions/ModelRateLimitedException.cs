namespace Chat.Application.Exceptions;

/// <summary>
/// Thrown when the model service refuses a request because its own rate limit was reached.
/// Defined here, not next to the service's SDK, so the Api layer can answer it without
/// depending on that SDK.
/// </summary>
public class ModelRateLimitedException : Exception
{
    /// <summary>Creates the exception, keeping the service's error as the inner one.</summary>
    public ModelRateLimitedException(Exception inner)
        : base("The model service is at its rate limit.", inner)
    {
    }
}
