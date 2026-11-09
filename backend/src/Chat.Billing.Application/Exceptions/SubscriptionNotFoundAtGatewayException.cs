namespace Chat.Billing.Application.Exceptions;

/// <summary>The payment provider has no subscription with this id, so asking again cannot help.</summary>
public class SubscriptionNotFoundAtGatewayException : Exception
{
    public SubscriptionNotFoundAtGatewayException(string stripeSubscriptionId)
        : base($"The payment provider has no subscription {stripeSubscriptionId}.")
    {
    }
}
