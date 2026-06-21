using NetCord;
using NetCord.Services.ApplicationCommands;

namespace FrostBot.Commands;

public class ActionsModule : ApplicationCommandModule<ApplicationCommandContext>
{
    public ActionsModule() { }

    [SlashCommand("kill", "Kill someone")]
    public string Kill(GuildUser user)
    {
        if (user.Id == Context.User.Id) return "you killed yourself";
        return $"{user.Nickname} has been ripped to shreds";
    }
}
