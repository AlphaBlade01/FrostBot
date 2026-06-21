using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace FrostBot.Data.Models;

public class GuildConfig
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong GuildId { get; set; }
    public ulong? LogChannelId { get; set; }
    public ulong? WelcomeChannelId { get; set; }
    public ulong? LevelUpChannelId { get; set; }
    public ulong? AutoRoleId { get; set; }
    public string? WelcomeMessage { get; set; }
    public string? LevelUpMessage { get; set; }


    // Moderation settings
    public bool DmOnWarn { get; set; }
    public bool DmOnKick { get; set; }
    public bool DmOnBan { get; set; }
    public bool DmOnMute { get; set; }
}
