using Microsoft.Extensions.Primitives;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FrostBot.Data.Models;

public class LogMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong MessageId { get; set; }
    public ulong AuthorId { get; set; }
    public ulong ChannelId { get; set; }
    public string Content { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
