
using CustomCodeFramework.Api.DependencyInjection;
using CustomCodeFramework.Api.Swagger;
using CustomCodeFramework.Core.Abstractions;
using Dhole.AI.Api.Endpoints;
using Dhole.AI.Api.BackgroundServices;
using Dhole.AI.Api.Grpc;
using Dhole.AI.Api.Middleware;
using Dhole.AI.Api.Services;
using Dhole.AI.Application.DependencyInjection;
using Dhole.AI.Infrastructure.DependencyInjection;
using Dhole.AI.Infrastructure.Time;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Persistence.DependencyInjection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicyName = "DholeWebCors";

var maxGrpcMessageSizeBytes = ReadPositiveInt(
    builder.Configuration["Grpc:Server:MaxMessageSizeBytes"],
    64 * 1024 * 1024
);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxGrpcMessageSizeBytes;
});

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddCustomCodeApiWithSwagger(title: "Dhole AI Service", version: "v1");

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        CorsPolicyName,
        policy =>
        {
            policy
                .WithOrigins(
                    "http://localhost:5173",
                    "http://127.0.0.1:5173",
                    "http://192.168.1.193:5173"
                )
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    );
});

builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = maxGrpcMessageSizeBytes;
    options.MaxSendMessageSize = maxGrpcMessageSizeBytes;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AiDefaultProfilesProvisioningService>();

builder.Services.AddHttpClient("ai-file-processing", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient("nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DholeAIService/1.0 (+https://customcodecr.com)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddHttpClient("overpass", client =>
{
    client.BaseAddress = new Uri("https://overpass-api.de/api/");
    client.Timeout = TimeSpan.FromSeconds(35);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DholeAIService/1.0 (+https://customcodecr.com)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddHttpClient("overpass-kumi", client =>
{
    client.BaseAddress = new Uri("https://overpass.kumi.systems/api/");
    client.Timeout = TimeSpan.FromSeconds(35);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DholeAIService/1.0 (+https://customcodecr.com)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<AiFileChatService>();

var app = builder.Build();

app.UseCustomCodeApi();

app.UseCors(CorsPolicyName);

if (app.Environment.IsDevelopment())
{
    app.UseCustomCodeSwagger();
}

app.MapGet(
        "/health",
        () =>
        {
            return Results.Ok(
                new
                {
                    service = "DholeAIService",
                    status = "Healthy",
                    timestamp = DateTimeOffset.UtcNow,
                }
            );
        }
    )
    .AllowAnonymous();

app.UseAuthentication();
app.UseMiddleware<AuditExecutionContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<AuditEndpointMiddleware>();

app.MapGrpcService<AiExecutionGrpcService>();

app.MapAiConnectionEndpoints();
app.MapAiModelEndpoints();
app.MapAiProfileEndpoints();
app.MapAiPromptTemplateEndpoints();
app.MapAiExecutionEndpoints();
app.MapAiFileExecutionEndpoints();
app.MapAiLogisticsV2Endpoints();
app.MapAiLogisticsNewsEndpoints();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();

    await dbContext.Database.MigrateAsync();
}

app.Run();

static int ReadPositiveInt(string? value, int fallback)
{
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}

public partial class Program { }
