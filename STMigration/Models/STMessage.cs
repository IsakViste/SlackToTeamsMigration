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

    public STMessage(STUser? user, string date, string? threadDate, string text, List<STAttachment> attachedFiles) {
        User = user;

        Date = date;
        ThreadDate = threadDate;
        IsInThread = !string.IsNullOrEmpty(threadDate);
        IsParentThread = IsInThread && ThreadDate == Date;

        Text = text;
        AttachedFiles = attachedFiles;
    }

    public string FormattedMessage(bool includeUser) {
        if (includeUser) {
            if (User != null) {
                return $"<strong>{User.DisplayName}</strong><br><blockquote>{FormattedText()}</blockquote>{FormattedAttachments()}";
            }

            return $"<strong>Unknown User</strong><br><blockquote>{FormattedText()}</blockquote>{FormattedAttachments()}";
        }

        return $"<blockquote>{FormattedText()}</blockquote>{FormattedAttachments()}";
    }

    public string FormattedText() {
        string formattedText = Text.TrimEnd().Replace("\n", "<br>");

        return formattedText;
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
