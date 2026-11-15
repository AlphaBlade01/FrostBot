using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FrostBot.Data.Models;

// Checkpoint of the YouTube upload poll: the newest feed entry that has already been handled.
// Persisted so a restart neither re-announces recent uploads nor forgets ones seen while running.
public class YoutubePollState
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public string ChannelId { get; set; } = string.Empty;
    public string? LastVideoId { get; set; }
    public DateTimeOffset? LastPublishedAt { get; set; }
}
