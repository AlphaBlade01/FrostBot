using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FrostBot.Data.Models;

public class UserInfo
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong UserId { get; set; }
    public int TotalXp { get; set; }
    public int Level { get; set; }
}
