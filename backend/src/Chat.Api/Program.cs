using System.Text;
using Chat.Api.Hubs;
using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Billing.Application.Interfaces;
using Chat.Billing.Application.Services;
using Chat.Billing.Infrastructure.Configuration;
using Chat.Billing.Infrastructure.Payment;
using Chat.Billing.Infrastructure.Repositories;
using Chat.Billing.Infrastructure.Services;
using Chat.Identity.Application.Interfaces;
using Chat.Identity.Infrastructure.Authorization;
using Chat.Identity.Infrastructure.Configuration;
using Chat.Identity.Infrastructure.Data;
using Chat.Identity.Infrastructure.Services;
using Chat.Identity.Infrastructure.Stores;
using Chat.Infrastructure.AI;
using Chat.Infrastructure.Configuration;
using Chat.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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

// ── Billing ────────────────────────────────────────────────────────────────────
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<ISubscriptionRepository, MongoSubscriptionRepository>();
builder.Services.AddScoped<IPlanRepository, MongoPlanRepository>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPaymentGateway, StripePaymentGateway>();
builder.Services.AddScoped<IWebhookHandler, StripeWebhookHandler>();
builder.Services.AddScoped<IPlanFeatureService, RealPlanFeatureService>();

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Refresh-token policy. Registered as the Application-layer interface as well, so
// IdentityService depends on the contract rather than on the Options type.
// Both lifetimes are validated at startup: a remembered session that is no longer than an
// ordinary one would make the user's "keep me signed in" choice meaningless or harmful, and
// the only place that is cheap to notice is before the app serves a request.
builder.Services.AddOptions<RefreshTokenSettings>()
    .Bind(builder.Configuration.GetSection("RefreshToken"))
    .Validate(s => s.LifetimeDays > 0, "RefreshToken:LifetimeDays must be greater than zero.")
    .Validate(s => s.PersistentLifetimeDays > s.LifetimeDays,
        "RefreshToken:PersistentLifetimeDays must be greater than RefreshToken:LifetimeDays.")
    .Validate(s => s.GraceWindowSeconds > 0,
        "RefreshToken:GraceWindowSeconds must be greater than zero.")
    .Validate(s => s.GraceWindow < s.Lifetime,
        "RefreshToken:GraceWindowSeconds must be shorter than RefreshToken:LifetimeDays.")
    .ValidateOnStart();
builder.Services.AddSingleton<IRefreshTokenSettings>(sp =>
    sp.GetRequiredService<IOptions<RefreshTokenSettings>>().Value);
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration.GetSection("Google"));

builder.Services.AddScoped<IUserStore, MongoUserStore>();
builder.Services.AddScoped<IRefreshTokenStore, MongoRefreshTokenStore>();
builder.Services.AddScoped<ITokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ── Authorization (RBAC) ──────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IRoleStore, MongoRoleStore>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionRequirementHandler>();

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
        // SignalR sends the token as ?access_token= on WebSocket/SSE connections
        // because browsers cannot set custom headers on those transports.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/chatHub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddCookie("ExternalCookie") // temporary cookie for the Google OAuth round-trip
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Google:ClientId"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["Google:ClientSecret"] ?? string.Empty;
        options.SignInScheme = "ExternalCookie";
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanChat",              p => p.AddRequirements(new PermissionRequirement("conversation:create")));
    options.AddPolicy("CanShareConversation", p => p.AddRequirements(new PermissionRequirement("conversation:share")));
    options.AddPolicy("CanInviteUsers",       p => p.AddRequirements(new PermissionRequirement("user:invite")));
    options.AddPolicy("AdminOnly",            p => p.AddRequirements(new PermissionRequirement("*")));
});

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

// Seed default roles into MongoDB if the collection is empty
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
    await RoleSeeder.SeedAsync(db);
}

app.Run();

// Make Program accessible to integration tests (WebApplicationFactory<Program>)
public partial class Program { }
