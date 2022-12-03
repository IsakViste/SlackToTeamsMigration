namespace STMigration;

public class User {
    public string DisplayName { get; private set; }
    public string TeamsID { get; private set; }

    public User(string displayName, string teamsID) {
        DisplayName = displayName;
        TeamsID = teamsID;
    }
}