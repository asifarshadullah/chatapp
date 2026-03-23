namespace Chat.Identity.Infrastructure.Configuration;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "chatapp";
    public string Audience { get; set; } = "chatapp";
    public int ExpiryMinutes { get; set; } = 60;
}
