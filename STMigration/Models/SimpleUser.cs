namespace STMigration.Models;

public class SimpleUser {
    public string UserId { get; set; }
    public string DisplayName { get; set; }
    public string Email { get; set; }
    public bool IsBot { get; set; } = false;

    public SimpleUser(string userId, string displayName, string email, bool isBot) {
        UserId = userId;
        DisplayName = displayName;
        Email = email;
        IsBot = isBot;
    }
}

