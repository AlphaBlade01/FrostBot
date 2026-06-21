using FrostBot.Data.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace FrostBot.Data.Models;

public class Warning : IModItem
{
    [Key]
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public ulong ModeratorUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
