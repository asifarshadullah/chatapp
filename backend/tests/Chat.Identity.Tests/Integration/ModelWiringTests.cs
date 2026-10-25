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
    public void TheConfiguredDailyLimit_ReachesTheMongoCounter()
    {
        using var app = Start(("Chat:DailyMessageLimit", "3"));
        using var scope = app.Services.CreateScope();

        var quota = scope.ServiceProvider.GetRequiredService<IDailyMessageQuota>();

        quota.Should().BeOfType<MongoDailyMessageQuota>();
        quota.Limit.Should().Be(3);
    }

    [Fact]
    public void TheRealProvider_IsBuiltFromTheModelOptions()
    {
        using var app = Start();

        app.Services.GetRequiredService<IAiProvider>().Should().BeOfType<OpenAiCompatibleProvider>();
    }
}
