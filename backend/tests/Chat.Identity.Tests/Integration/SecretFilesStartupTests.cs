using Chat.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Identity.Tests.Integration;

/// <summary>
/// Render mounts Secret Files in a directory, so the database password, the signing secret and
/// the Groq key can stay out of the process environment. Each file names the setting it holds.
/// The real app is used, not AuthApiFactory, whose own settings would mask what the files supply.
/// </summary>
public class SecretFilesStartupTests : IDisposable
{
    private const string LocalMongo =
        "mongodb://chatapp:chatapp_dev@localhost:27018/?authSource=admin&authMechanism=SCRAM-SHA-256";
    private const string ProperSecret = "a-proper-secret-that-is-comfortably-longer-than-thirty-two-bytes-0123";

    private readonly string _secrets = Path.Combine(Path.GetTempPath(), $"chatapp-secrets-{Guid.NewGuid():N}");

    public SecretFilesStartupTests() => Directory.CreateDirectory(_secrets);

    public void Dispose() => Directory.Delete(_secrets, recursive: true);

    private void SecretFile(string name, string value) => File.WriteAllText(Path.Combine(_secrets, name), value);

    private WebApplicationFactory<Program> Start(string secretsDirectory)
    {
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("MongoDB:ConnectionString", LocalMongo);
            builder.UseSetting("MongoDB:DatabaseName", "chatapp_test");
            builder.UseSetting("SecretsDirectory", secretsDirectory);
        });
        // Creating the client is what starts the host.
        app.CreateClient();
        return app;
    }

    [Fact]
    public void TheGroqKeyAndSigningSecret_CanComeFromFiles()
    {
        SecretFile("Ai__Models__groq__ApiKey", "gsk_from_a_file");
        SecretFile("Jwt__Secret", ProperSecret);

        using var app = Start(_secrets);

        app.Services.GetRequiredService<IModelCatalog>().Offered.Select(o => o.Id).Should().Equal("groq");
    }

    [Fact]
    public void ASecretFile_WinsOverAnEnvironmentVariable()
    {
        // A name no other test reads, since the environment is shared by every test running at once.
        const string variable = "SecretFilesTest__Probe";
        Environment.SetEnvironmentVariable(variable, "from-the-environment");
        try
        {
            SecretFile("Ai__Models__groq__ApiKey", "gsk_from_a_file");
            SecretFile("Jwt__Secret", ProperSecret);
            // Render's editor may leave a trailing newline; it is not part of the value.
            SecretFile(variable, "from-a-file\n");

            using var app = Start(_secrets);

            app.Services.GetRequiredService<IConfiguration>()["SecretFilesTest:Probe"].Should().Be("from-a-file");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void AMissingSecretsDirectory_IsNotAnError()
    {
        var missing = Path.Combine(_secrets, "does-not-exist");

        var act = () =>
        {
            using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("SecretsDirectory", missing);
            });
            app.CreateClient();
        };

        act.Should().NotThrow();
    }
}
