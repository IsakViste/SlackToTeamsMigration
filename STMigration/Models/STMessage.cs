using System.Text;

namespace STMigration.Models;

public class STMessage {
    public string User { get; private set; }

    public string Date { get; private set; }
    public string? ThreadDate { get; private set; }
    public bool IsInThread { get; private set; }
    public bool IsParentThread { get; private set; }

    public string Text { get; private set; }
    public List<SimpleAttachment> AttachedFiles { get; private set; }

    public STMessage(string user, string date, string? threadDate, string text, List<SimpleAttachment> attachedFiles) {
        User = user;

        Date = date;
        ThreadDate = threadDate;
        IsInThread = !string.IsNullOrEmpty(threadDate);
        IsParentThread = IsInThread && ThreadDate == Date;

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
