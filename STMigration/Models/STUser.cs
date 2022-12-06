using Newtonsoft.Json;

namespace STMigration.Models;

public class STUser {
    public string DisplayName { get; private set; }
    public string? Email { get; private set; }

    public string SlackUserID { get; private set; }
    public string TeamsUserID { get; private set; }

    public bool IsBot { get; set; } = false;

    [JsonConstructor]
    public STUser(string slackUserID, string? teamsUserID, string displayName, string? email, bool isBot) {
        SlackUserID = slackUserID;
        TeamsUserID = teamsUserID ?? string.Empty;

        DisplayName = displayName;
        Email = email;
        IsBot = isBot;
    }

    public STUser(string slackUserID, string displayName, string? email, bool isBot) : this(slackUserID, string.Empty, displayName, email, isBot) {
    }

    public static STUser BotUser(string slackUserID, string displayName) {
        return new STUser(slackUserID, displayName, string.Empty, true);
    }

    public static readonly STUser SLACK_BOT = BotUser("USLACKBOT", "Slack Bot");

    public void SetTeamUserID(string? id) {
        TeamsUserID = id ?? string.Empty;
    }
}