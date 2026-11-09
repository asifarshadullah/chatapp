namespace Chat.Billing.Infrastructure.Configuration;

/// <summary>The plans a user can be on, read from the "Billing" section.</summary>
public class BillingSettings
{
    public BillingPlans Plans { get; set; } = new();
}

/// <summary>Free and Pro, the only two plans offered.</summary>
public class BillingPlans
{
    public PlanSettings Free { get; set; } = new() { Name = "Free", PricePerMonth = 0m, DailyMessages = 10 };
    public PlanSettings Pro { get; set; } = new() { Name = "Pro", PricePerMonth = 19m, DailyMessages = 100 };
}

/// <summary>What one plan is called, costs and allows.</summary>
public class PlanSettings
{
    public string Name { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }

    /// <summary>Messages a user on this plan may send per UTC day.</summary>
    public int DailyMessages { get; set; }
}
