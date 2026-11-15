using FrostBot.Data;
using FrostBot.Logic.Jobs;
using FrostBot.Logic.Jobs.Registrations;
using FrostBot.Logic.Services;
using FrostBot.Logic.Views;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Timestamps on console output (default console formatter omits them entirely)
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

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
    .AddSingleton<ChannelConfigService>()
    .AddSingleton<NotificationService>()
    // Views
    .AddSingleton<ModerationViewEngine>();

// Pooled client for the YouTube upload poll and classification checks (channel feed, Data API, player API) so repeated polls don't leak sockets
builder.Services.AddHttpClient("youtube-check", client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(15);
    });

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

// Register all scheduled jobs — each IJobRegistration describes its own job and triggers
var scheduler = app.Services.GetRequiredService<IScheduler>();

// Remove all stale Quartz job definitions that point at types the assembly no longer contains
try
{
    await QuartzJobStoreCleanup.RemoveUnloadableJobsAsync(scheduler, app.Logger);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not purge stale Quartz job definitions; continuing startup.");
}

foreach (var type in jobRegistrationTypes)
    await ((IJobRegistration)app.Services.GetRequiredService(type)).RegisterAsync(scheduler);

await app.RunAsync();
