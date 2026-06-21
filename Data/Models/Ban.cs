using FrostBot.Data.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace FrostBot.Data.Models;

public class Ban : IModItem
{
    [Key]
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public ulong ModeratorUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? UnbannedAt { get; set; }
    public bool IsActive { get; set; }
}
