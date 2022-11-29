using System.Text;

using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using STMigration.Models;

namespace STMigration.Utils;

public class Messages {
	static IDictionary<string, ComplexMessage> ParentThreads = new Dictionary<string, ComplexMessage>();

	public static List<ComplexMessage> ScanMessagesByChannel(string basePath, string channelName, List<SimpleUser> slackUsers) {
		var messageList = new List<ComplexMessage>();
		messageList.Clear();
		
		Console.WriteLine();

		foreach (var file in Directory.GetFiles(Path.Combine(basePath, channelName))) {
			Console.WriteLine($"File {file}");

			using (FileStream fs = new(file, FileMode.Open, FileAccess.Read))
			using (StreamReader sr = new(fs))
			using (JsonTextReader reader = new(sr)) {
				while (reader.Read()) {
					if (reader.TokenType == JsonToken.StartObject) {
						JObject obj = JObject.Load(reader);

						string messageSender = FindMessageSender(obj, slackUsers);
						string? messageTS = obj.SelectToken("ts")?.ToString();
						string messageText = GetFormattedText(obj, slackUsers);

						List<SimpleAttachment> attachments = GetFormattedAttachments(obj);

						string? threadTS = obj.SelectToken("thread_ts")?.ToString();
						bool isParentThread = false;
						bool isInThread = false;

						SimpleMessage simpleMessage = new(messageSender, messageTS ?? "INVALID", messageText, attachments);
						ComplexMessage complexMessage;

						if (string.IsNullOrEmpty(threadTS)) {
							complexMessage = new(simpleMessage, false);

							messageList.Add(complexMessage);
							continue;
						}

						if(threadTS.Equals(messageTS)) {
							isParentThread = true;
						} else {
							isInThread = true;
						}

						if(isParentThread) {
							complexMessage = new(simpleMessage, isParentThread);
							ParentThreads.Add(threadTS, complexMessage);
							messageList.Add(complexMessage);
						}

						if (isInThread) {
							if (ParentThreads.TryGetValue(threadTS, out var parentMessage)) {
								parentMessage.ThreadMessages?.Add(simpleMessage);
							} else {
								Console.WriteLine($"Could not find parent thread with TS: {threadTS}");
							}
						}						
					}
				}
			}
		}

		return messageList;
	}

	static string GetFormattedText (JObject obj, List<SimpleUser> slackUserList) {
		var richTextArray = obj.SelectTokens("blocks[0].elements[0].elements[*]").ToList();

		// Simple text, get it directly from text field
		if (richTextArray == null || !richTextArray.Any()) {
			string? text = obj.SelectToken("text")?.ToString();
			return text ?? "NO TEXT";
			
		}

		StringBuilder formattedText = new();
		foreach (JToken token in richTextArray) {
			string? type = token.SelectToken("type")?.ToString();
			//Console.Write($"[{type}] - ");
			switch (type) {
				case "text":
					string? text = token.SelectToken("text")?.ToString();

					if(string.IsNullOrEmpty(text)) {
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

					string displayName = DisplayNameFromUserID(slackUserList, userID);

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

	static List<SimpleAttachment> GetFormattedAttachments(JObject obj) {
		var attachmentsArray = obj.SelectTokens("files[*]").ToList();

		List<SimpleAttachment> formattedAttachments = new ();
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

			formattedAttachments.Add(new SimpleAttachment(url, fileType, title, date));
			index++;
		}


		return formattedAttachments;
	}

	static string DisplayNameFromUserID (List<SimpleUser> slackUserList, string userID) {
		if (userID != "USLACKBOT") {
			var simpleUser = slackUserList.FirstOrDefault(w => w.UserId == userID);
			if (simpleUser != null) {
				return simpleUser.DisplayName;
			}
		}

		return "SlackBot";
	}

	static string FindMessageSender (JObject obj, List<SimpleUser> slackUserList) {
		var userID = obj.SelectToken("user")?.ToString();

		if (!String.IsNullOrEmpty(userID)) {
			return DisplayNameFromUserID(slackUserList, userID);
		}

		string? username = obj.SelectToken("username")?.ToString();
		if (!(String.IsNullOrEmpty(username))) {
			return username;
		}

		string? bot_id = obj.SelectToken("bot_id")?.ToString();
		if (!(String.IsNullOrEmpty(bot_id))) {
			return bot_id;
		}

		return "";
	}
}
