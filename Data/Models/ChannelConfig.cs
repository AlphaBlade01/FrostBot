using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FrostBot.Data.Models;

public class ChannelConfig
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong ChannelId { get; set; }

    [ForeignKey(nameof(GuildId))]
    public ulong GuildId { get; set; }
    public bool IsXpDisabled { get; set; }
}
