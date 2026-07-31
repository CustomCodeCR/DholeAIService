
using CustomCodeFramework.Core.Abstractions;
using Dhole.AI.Application.DependencyInjection;
using Dhole.AI.Infrastructure.Time;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Persistence.DependencyInjection;
using Dhole.AI.Worker.DependencyInjection;
using Dhole.AI.Workers.Security;
using Microsoft.EntityFrameworkCore;

var contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "src", "Dhole.AI.Workers");

if (!Directory.Exists(contentRoot))
    contentRoot = Directory.GetCurrentDirectory();

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot }
);

builder.Configuration.Sources.Clear();

builder
    .Configuration.SetBasePath(contentRoot)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAiWorker(builder.Configuration);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
    await dbContext.Database.MigrateAsync();
}

await host.RunAsync();
