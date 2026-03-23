using System.Text;
using Chat.Api.Hubs;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Infrastructure.Configuration;
using Chat.Identity.Infrastructure.Services;
using Chat.Identity.Infrastructure.Stores;
using Chat.Infrastructure.AI;
using Chat.Infrastructure.Configuration;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// MongoDB setup
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDB"));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddScoped<IMongoDatabase>(sp =>
{
    var client = sp.GetRequiredService<IMongoClient>();
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return client.GetDatabase(settings.DatabaseName);
});

builder.Services.AddScoped<IChatRepository, MongoChatRepository>();

// Ollama AI provider
builder.Services.Configure<OllamaSettings>(
    builder.Configuration.GetSection("Ollama"));
builder.Services.AddScoped<IAiProvider, OllamaAiProvider>();

// Application layer services
builder.Services.AddScoped<IChatService, ChatService>();

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection("Google"));

builder.Services.AddScoped<IUserStore, MongoUserStore>();
builder.Services.AddScoped<ITokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? string.Empty;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "chatapp";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "chatapp";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep 'sub' as 'sub', not mapped to NameIdentifier
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddCookie("ExternalCookie") // temporary cookie for the Google OAuth round-trip
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"] ?? string.Empty;
        options.SignInScheme = "ExternalCookie";
    });

builder.Services.AddAuthorization();

// CORS — allow frontend dev server (Vite default port)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for SignalR
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

app.Run();

// Make Program accessible to integration tests (WebApplicationFactory<Program>)
public partial class Program { }
