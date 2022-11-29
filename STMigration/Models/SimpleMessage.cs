using System.Text;

namespace STMigration.Models;

public class SimpleMessage {
    public string User { get; set; }
    public string Date { get; set; }
    public string Text { get; set; }
    public List<SimpleAttachment> AttachedFiles { get; set; }

    public SimpleMessage(string user, string date, string text, List<SimpleAttachment> attachedFiles) {
        User = user;
        Date = FormattedDate(date);
        Text = FormattedText(text);
        AttachedFiles = attachedFiles;
    }

    public string FormattedMessage() {
        return $"<strong>[{Date}] {User}</strong><br><blockquote>{Text}</blockquote>{FormattedAttachments()}";
    }

    public static string FormattedText(string text) {
        string formattedText = text.TrimEnd().Replace("\n", "<br>");

        return formattedText;
    }

    public static string FormattedDate(string date) {
        DateTime dateTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(date.Split(".")[0])).LocalDateTime;

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
