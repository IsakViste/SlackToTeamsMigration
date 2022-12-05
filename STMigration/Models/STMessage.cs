using System.Text;

namespace STMigration.Models;

public class STMessage {
    public STUser? User { get; private set; }

    public string Date { get; private set; }
    public string? ThreadDate { get; private set; }
    public bool IsInThread { get; private set; }
    public bool IsParentThread { get; private set; }

    public string Text { get; private set; }
    public List<STAttachment> AttachedFiles { get; private set; }

    // Team Message IDs are the Timestamp followed by 000
    public string? TeamID => $"{ThreadDate?.Split(".")[0]}000";

    public STMessage(STUser? user, string date, string? threadDate, string text, List<STAttachment> attachedFiles) {
        User = user;

        Date = date;
        ThreadDate = threadDate;
        IsInThread = !string.IsNullOrEmpty(threadDate);
        IsParentThread = IsInThread && ThreadDate == Date;

        Text = text;
        AttachedFiles = attachedFiles;
    }

    public string FormattedMessage() {
        return $"<blockquote>{FormattedText()}</blockquote>{FormattedAttachments()}";
    }

    public string FormattedText() {
        StringBuilder stringBuilder = new(Text.TrimEnd());

        stringBuilder.Replace("\n", "<br>");

        return stringBuilder.ToString();
    }

    public DateTime FormattedLocalTime() {
        return DateTimeOffset.FromUnixTimeSeconds(long.Parse(Date.Split(".")[0])).LocalDateTime;
    }

    public string FormattedAttachments() {
        StringBuilder formattedText = new();
        foreach (var att in AttachedFiles) {
            _ = formattedText.Append($"<attachment id='{att.TeamsGUID}'></attachment>");
        }

        return formattedText.ToString();
    }
}
