using Chat.Application.Interfaces;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Api.Tests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that replaces MongoDB with InMemoryChatRepository
/// so API integration tests run without a Docker dependency.
/// </summary>
public class ChatApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove any existing IChatRepository registration (MongoDB or InMemory)
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IChatRepository));
            if (descriptor is not null)
                services.Remove(descriptor);

            // Replace with fast in-memory repository — no Docker needed
            services.AddSingleton<IChatRepository, InMemoryChatRepository>();
        });
    }
}
