using Chat.Application.Interfaces;
using Chat.Application.Services;
using Chat.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Infrastructure — Singleton so in-memory conversations persist across requests
builder.Services.AddSingleton<IChatRepository, InMemoryChatRepository>();

// Application layer services
builder.Services.AddScoped<IChatService, ChatService>();

// CORS — allow frontend dev server (Vite default port)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.Run();

// Make Program accessible to integration tests (WebApplicationFactory<Program>)
public partial class Program { }
