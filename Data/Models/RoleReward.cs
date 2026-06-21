using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FrostBot.Data.Models;

public class RoleReward
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public ulong RoleId { get; set; }
    public int LevelRequired { get; set; }
}
