using Microsoft.Extensions.Options;

namespace Chat.Billing.Infrastructure.Configuration;

/// <summary>Refuses at startup a plan that could never be used or would pay the user.</summary>
public class BillingSettingsValidator : IValidateOptions<BillingSettings>
{
    public ValidateOptionsResult Validate(string? name, BillingSettings settings)
    {
        var failures = new List<string>();
        Check("Free", settings.Plans.Free, failures);
        Check("Pro", settings.Plans.Pro, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check(string key, PlanSettings plan, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(plan.Name))
            failures.Add($"Billing:Plans:{key}:Name must be set.");
        if (plan.DailyMessages <= 0)
            failures.Add($"Billing:Plans:{key}:DailyMessages must be greater than zero.");
        if (plan.PricePerMonth < 0)
            failures.Add($"Billing:Plans:{key}:PricePerMonth must not be negative.");
    }
}
