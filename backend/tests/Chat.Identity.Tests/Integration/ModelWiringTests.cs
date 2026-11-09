using Chat.Api.Billing;
using Chat.Application.Interfaces;
using Chat.Infrastructure.AI;
using Chat.Infrastructure.Quota;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// The other tests replace the provider and the daily counter with fakes, so these check, on the
/// real app, that configuration reaches the real ones.
/// </summary>
public class ModelWiringTests
{
    private static WebApplicationFactory<Program> Start(params (string Key, string Value)[] settings)
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Ai:Models:groq:ApiKey", string.Empty);
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });
        // Creating the client is what starts the host.
        app.CreateClient();
        return app;
    }

    [Fact]
    public void TheDailyLimit_IsCountedInMongo_AgainstTheUsersPlan()
    {
        using var app = Start();
        using var scope = app.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDailyMessageQuota>().Should().BeOfType<MongoDailyMessageQuota>();
        scope.ServiceProvider.GetRequiredService<IDailyMessageAllowance>().Should().BeOfType<PlanDailyMessageAllowance>();
    }

    [Fact]
    public void TheRealProvider_IsBuiltFromTheModelOptions()
    {
        using var app = Start();

        app.Services.GetRequiredService<IAiProvider>().Should().BeOfType<OpenAiCompatibleProvider>();
    }
}
