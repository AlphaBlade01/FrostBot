using FrostBot.Data.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace FrostBot.Data.Models;

public class Mute : IModItem
{
    [Key]
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public ulong ModeratorUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UnmutedAt { get; set; }
    public bool IsActive { get; set; }
}
