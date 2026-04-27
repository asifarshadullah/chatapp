namespace Chat.Billing.Application.DTOs;

/// <summary>Result of creating a Stripe Checkout session.</summary>
public record CheckoutSessionDto(string CheckoutUrl, string SessionId);
