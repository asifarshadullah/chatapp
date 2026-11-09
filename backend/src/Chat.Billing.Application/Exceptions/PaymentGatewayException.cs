namespace Chat.Billing.Application.Exceptions;

/// <summary>The payment provider refused a request or could not be reached.</summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
