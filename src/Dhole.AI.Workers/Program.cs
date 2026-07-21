
using CustomCodeFramework.Core.Abstractions;
using Dhole.AI.Infrastructure.Time;
using Dhole.AI.Persistence.DependencyInjection;
using Dhole.AI.Worker.DependencyInjection;
using Dhole.AI.Workers.Security;

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

Console.WriteLine($"Postgres: {builder.Configuration["Postgres:ConnectionString"]}");

builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddScoped<ICurrentUser, WorkerCurrentUser>();

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddAiWorker(builder.Configuration);

var host = builder.Build();

await host.RunAsync();
