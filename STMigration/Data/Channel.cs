namespace STMigration;

#pragma warning disable IDE1006

public class Channel {
    public string displayName { get; set; }
    public string description { get; set; }
    public string createdDateTime { get; set; }
    public string membershipType { get; set; } = "standard";

    public Channel(string displayName, string description, string createdDateTime) {
        this.displayName = displayName;
        this.description = description;
        this.createdDateTime = createdDateTime;
    }

    public Channel(string dirName, string createdDateTime) {
        displayName = dirName;
        description = $"Description for {dirName}";
        this.createdDateTime = createdDateTime;
    }
}