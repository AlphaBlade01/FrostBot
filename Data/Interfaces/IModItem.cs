namespace FrostBot.Data.Interfaces;

public interface IModItem
{
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public ulong ModeratorUserId { get; set; }
    public string Reason { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
