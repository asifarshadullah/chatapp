namespace Chat.Billing.Application.Exceptions;

/// <summary>Thrown when a user who already has a paid plan starts another checkout.</summary>
public class AlreadySubscribedException : Exception
{
    public AlreadySubscribedException()
        : base("You are already on a paid plan.")
    {
    }
}
