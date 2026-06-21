using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.SQLite;
using FrostBot.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FrostBot.Data;

public class BotDbContext : DbContext
{
    private IConfiguration _configuration;
    
    // Tables
    public DbSet<GuildConfig> GuildConfigs { get; set; }
    public DbSet<Mute> Mutes { get; set; }
    public DbSet<Warning> Warnings { get; set; }
    public DbSet<Ban> Bans { get; set; }
    public DbSet<Kick> Kicks { get; set; }
    public DbSet<ChannelConfig> ChannelConfigs { get; set; }
    public DbSet<UserInfo> UserInfo { get; set; }
    public DbSet<RoleReward> RoleRewards { get; set; }

    public BotDbContext(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Connect to database through connection string
        var connectionString = _configuration.GetConnectionString("Default");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Default' is not set.");
        }
        optionsBuilder.UseSqlite(connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddQuartz(builder => builder.UseSqlite());
        base.OnModelCreating(modelBuilder);
    }
}
