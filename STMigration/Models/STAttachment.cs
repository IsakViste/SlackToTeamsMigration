namespace STMigration.Models;

public class STAttachment {
    public string MessageTS { get; private set; }

    public string SlackURL { get; set; }
    public string Name { get; set; }
    public string Date { get; private set; }
    public string Extension { get; set; }

    public string TeamsURL { get; set; }
    public string TeamsGUID { get; set; }

    public STAttachment(string slackURL, string? extension, string? name, string? date, string messageTS) {
        MessageTS = messageTS.Replace(".", "")[..13];

        SlackURL = slackURL;
        Name = name ?? "";
        Date = date ?? "";
        Extension = extension ?? "";
        FormatNameAndDate();

        TeamsURL = string.Empty;
        TeamsGUID = string.Empty;
    }

    public void FormatNameAndDate() {
        void FormattedName(string? timeString) {
            if (string.IsNullOrEmpty(Name)) {
                if (string.IsNullOrEmpty(timeString)) {
                    Name = $"Unknown.{Extension}";
                    return;
                }

                Name = $"{timeString}.{Extension}";
                return;
            }

            if (string.IsNullOrEmpty(timeString)) {
                return;
            }

            Name = $"{timeString} {Name}";
        }

        if (string.IsNullOrEmpty(Date)) {
            Date = "UNKNOWN";
            FormattedName(null);
            return;
        }

        DateTime dateTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Date)).LocalDateTime;
        Date = $"{dateTime.Year}/{dateTime.Month}/{dateTime.Day}-{dateTime.DayOfWeek}";

        string timeString = $"{dateTime.Hour:D2}.{dateTime.Minute:D2}.{dateTime.Second:D2}";
        FormattedName(timeString);
    }

    public override string ToString() {
        return $"[{Date}] {Name} (.{Extension})\n{TeamsURL}\n{TeamsGUID}";
    }
}

