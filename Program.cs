using FrostBot.Data;
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

var builder = Host.CreateApplicationBuilder(args);
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
    // Views
    .AddSingleton<ModerationViewEngine>();

var host = builder.Build();

using var db = host.Services
    .GetRequiredService<IDbContextFactory<BotDbContext>>()
    .CreateDbContext();
await db.Database.MigrateAsync();

host.AddModules(typeof(Program).Assembly);
await host.RunAsync();
