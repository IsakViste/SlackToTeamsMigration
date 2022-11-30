using System.Text;

namespace STMigration.Models;

public class SimpleMessage {
    public string User { get; set; }
    public string? Date { get; set; }
    public string Text { get; set; }
    public List<SimpleAttachment> AttachedFiles { get; set; }

    public SimpleMessage(string user, string? date, string text, List<SimpleAttachment> attachedFiles) {
        User = user;
        Date = date;
        Text = text;
        AttachedFiles = attachedFiles;
    }

    public string FormattedMessage() {
        return $"<strong>[{FormattedDate()}] {User}</strong><br><blockquote>{FormattedText()}</blockquote>{FormattedAttachments()}";
    }

    public string FormattedText() {
        string formattedText = Text.TrimEnd().Replace("\n", "<br>");

        return formattedText;
    }

    public string FormattedDate() {
        if (string.IsNullOrEmpty(Date)) {
            return "Unknown";
        }

        DateTime dateTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Date.Split(".")[0])).LocalDateTime;

        return dateTime.ToString();
    }

    public string FormattedAttachments() {
        StringBuilder formattedText = new();
        foreach (var att in AttachedFiles) {
            _ = formattedText.Append($"<attachment id='{att.TeamsGUID}'></attachment>");
        }

        return formattedText.ToString();
    }
}
