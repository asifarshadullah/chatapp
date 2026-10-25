using Microsoft.AspNetCore.Hosting;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// ChatApiFactory with a web root holding a stand-in for the built UI, as the image has.
/// </summary>
public class BuiltUiApiFactory : ChatApiFactory
{
    public const string EntryPage = "<!doctype html><title>chat-ui entry page</title>";
    public const string AssetPath = "/assets/app-abc123.js";
    public const string AssetContent = "console.log('chat-ui')";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"chat-ui-{Guid.NewGuid():N}");

    public BuiltUiApiFactory()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), EntryPage);
        File.WriteAllText(Path.Combine(_webRoot, AssetPath.TrimStart('/')), AssetContent);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_webRoot))
            Directory.Delete(_webRoot, recursive: true);
    }
}
