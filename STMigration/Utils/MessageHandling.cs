using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using STMigration.Models;

namespace STMigration.Utils;

public class MessageHandling {
    public static IEnumerable<string> GetFilesForChannel(string channelPath) {
        foreach (var file in Directory.GetFiles(channelPath)) {
            yield return file;
        }
    }

    public static IEnumerable<STMessage> GetMessagesForDay(string messagesPath, List<STUser> users) {
        Console.WriteLine($"File {messagesPath}");

        using FileStream fs = new(messagesPath, FileMode.Open, FileAccess.Read);
        using StreamReader sr = new(fs);
        using JsonTextReader reader = new(sr);

        while (reader.Read()) {
            if (reader.TokenType == JsonToken.StartObject) {
                JObject obj = JObject.Load(reader);

                string? messageTS = obj.SelectToken("ts")?.ToString();
                if (string.IsNullOrEmpty(messageTS)) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"{messageTS} is not valid in");
                    Console.Error.WriteLine($"{obj}");
                    Console.ResetColor();
                    Console.WriteLine();
                    continue;
                }

                STUser? messageSender = FindMessageSender(obj, users);
                string messageText = GetFormattedText(obj, users);

                List<STAttachment> attachments = GetFormattedAttachments(obj);

                string? threadTS = obj.SelectToken("thread_ts")?.ToString();

                STMessage message = new(messageSender, messageTS, threadTS, messageText, attachments);

                yield return message;
            }
        }
    }

    static string GetFormattedText(JObject obj, List<STUser> userList) {
        var richTextArray = obj.SelectTokens("blocks[0].elements[0].elements[*]").ToList();

        // Simple text, get it directly from text field
        if (richTextArray == null || !richTextArray.Any()) {
            string? text = obj.SelectToken("text")?.ToString();
            return text ?? string.Empty;

        }

        StringBuilder formattedText = new();
        foreach (JToken token in richTextArray) {
            string? type = token.SelectToken("type")?.ToString();
            //Console.Write($"[{type}] - ");
            switch (type) {
                case "text":
                    string? text = token.SelectToken("text")?.ToString();

                    if (string.IsNullOrEmpty(text)) {
                        break;
                    }

                    _ = formattedText.Append(text);
                    //Console.Write($"{text}\n");
                    break;
                case "link":
                    string? link = token.SelectToken("url")?.ToString();
                    string? linkText = token.SelectToken("text")?.ToString();

                    if (string.IsNullOrEmpty(link)) {
                        break;
                    }

                    if (string.IsNullOrEmpty(linkText)) {
                        _ = formattedText.Append($"<a href='{link}'>{link}</a>");
                        break;
                    }

                    _ = formattedText.Append($"<a href='{link}'>{linkText}</a>");
                    //Console.Write($"{link}\n");
                    break;
                case "user":
                    string? userID = token.SelectToken("user_id")?.ToString();

                    if (string.IsNullOrEmpty(userID)) {
                        break;
                    }

                    string displayName = DisplayNameFromUserID(userList, userID);

                    _ = formattedText.Append($"@{displayName}");
                    //Console.Write($"{user}\n");
                    break;
                case "usergroup":
                    // TODO: Figure out user group display name
                    // In the meantime, just use a temporary placeholder
                    //string? userGroup = token.SelectToken("usergroup_id")?.ToString();
                    _ = formattedText.Append($"@TEAM");
                    //Console.Write($"{userGroup}\n");
                    break;
                case "emoji":
                    //Console.WriteLine();
                    break;
                default:
                    break;
            }
        }

        return formattedText.ToString();
    }

    static STUser? FindMessageSender(JObject obj, List<STUser> userList) {
        var userID = obj.SelectToken("user")?.ToString();

        if (!string.IsNullOrEmpty(userID)) {
            if (userID == "USLACKBOT") {
                return STUser.SLACK_BOT;
            }

            return userList.FirstOrDefault(user => user.SlackUserID == userID);
        }

        return null;
    }

    static string DisplayNameFromUserID(List<STUser> userList, string userID) {
        if (userID != "USLACKBOT") {
            var simpleUser = userList.FirstOrDefault(user => user.SlackUserID == userID);
            if (simpleUser != null) {
                return simpleUser.DisplayName;
            }

            return "Unknown User";
        }

        return "SlackBot";
    }

    static List<STAttachment> GetFormattedAttachments(JObject obj) {
        var attachmentsArray = obj.SelectTokens("files[*]").ToList();

        List<STAttachment> formattedAttachments = new();
        int index = 0;
        foreach (var attachment in attachmentsArray) {
            string? url = attachment.SelectToken("url_private_download")?.ToString();
            string? fileType = attachment.SelectToken("filetype")?.ToString();
            string? title = attachment.SelectToken("title")?.ToString();
            string? date = attachment.SelectToken("timestamp")?.ToString();

            if (string.IsNullOrEmpty(url)) {
                continue;
            }
            if (string.IsNullOrEmpty(fileType) && string.IsNullOrEmpty(title)) {
                continue;
            }

            formattedAttachments.Add(new STAttachment(url, fileType, title, date));
            index++;
        }

        return formattedAttachments;
    }
}
