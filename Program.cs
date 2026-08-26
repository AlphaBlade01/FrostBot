using FrostBot.Data;
using FrostBot.Endpoints;
using FrostBot.Logic.Jobs.Registrations;
using FrostBot.Logic.Services;
using FrostBot.Logic.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;
using Quartz;

var builder = WebApplication.CreateBuilder(args);
var token = builder.Configuration["Discord:BotToken"] ?? throw new InvalidOperationException("Discord:BotToken configuration value is not set.");

builder.Services.AddDiscordGateway(options =>
    {
        options.Token = token;
        options.Intents = GatewayIntents.All;
    })
    .AddGatewayHandlers(typeof(Program).Assembly)
    .AddApplicationCommands()
    .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>()
    .AddDbContextFactory<BotDbContext>()
    .AddQuartz(q =>
    {
        q.UsePersistentStore(s =>
        {
            var dbConn = builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'Default' is not set.");
            s.UseMicrosoftSQLite(dbConn);
            s.UseNewtonsoftJsonSerializer();
            s.UseProperties = true;
        });
    })
    .AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    })
    .AddSingleton<IScheduler>(provider => provider.GetRequiredService<ISchedulerFactory>().GetScheduler().GetAwaiter().GetResult())
    // Services
    .AddSingleton<LogService>()
    .AddSingleton<ModerationService>()
    .AddSingleton<LevelService>()
    .AddSingleton<NotificationService>()
    // Views
    .AddSingleton<ModerationViewEngine>();

// Pooled HTTP client (used by NotificationService) so repeated subscribe attempts don't leak sockets
builder.Services.AddHttpClient("youtube-hub", client => client.DefaultRequestHeaders.UserAgent.ParseAdd("FrostBot"));

// Discover all endpoint types so new endpoints are registered automatically
var endpointTypes = typeof(Program).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IEndpoint).IsAssignableFrom(t))
    .ToArray();
foreach (var type in endpointTypes)
    builder.Services.AddSingleton(type);

// Discover all job registrations so new scheduled jobs are registered automatically
var jobRegistrationTypes = typeof(Program).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IJobRegistration).IsAssignableFrom(t))
    .ToArray();
foreach (var type in jobRegistrationTypes)
    builder.Services.AddSingleton(type);

var app = builder.Build();

using var db = app.Services
    .GetRequiredService<IDbContextFactory<BotDbContext>>()
    .CreateDbContext();
await db.Database.MigrateAsync();

app.AddModules(typeof(Program).Assembly);

// Map all discovered endpoints
foreach (var type in endpointTypes)
    ((IEndpoint)app.Services.GetRequiredService(type)).Map(app);

// Register all scheduled jobs — each IJobRegistration describes its own job and triggers
var scheduler = app.Services.GetRequiredService<IScheduler>();
foreach (var type in jobRegistrationTypes)
    await ((IJobRegistration)app.Services.GetRequiredService(type)).RegisterAsync(scheduler);

await app.RunAsync();
