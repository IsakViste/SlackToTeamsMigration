using System.Text.RegularExpressions;
using Microsoft.Graph;
using STMigration.Models;

namespace STMigration;

public static class GraphHelper {
    #region Team Handling
    public static async Task<string> CreateTeamAsync(GraphServiceClient gsc, string name, string description) {
        var team = new Team {
            DisplayName = name,
            Description = description,
            AdditionalData = new Dictionary<string, object>()
                {
                    {"template@odata.bind", "https://graph.microsoft.com/v1.0/teamsTemplates('standard')"},
                    {"@microsoft.graph.teamCreationMode", "migration"}
                }
        };

        var createdTeam = await gsc.Teams
            .Request()
            .AddAsync(team);

        return createdTeam.Id;
    }

    public static async Task<(string, string)> CreateChannelAsync(GraphServiceClient gsc, string teamID, string name, string description) {
        var channel = new Channel {
            DisplayName = name,
            Description = description,
            MembershipType = ChannelMembershipType.Standard,
            AdditionalData = new Dictionary<string, object>()
                {
                    {"@microsoft.graph.channelCreationMode", "migration"}
                }
        };

        var createdChannel = await gsc.Teams[teamID].Channels
            .Request()
            .AddAsync(channel);

        return (createdChannel.Id, createdChannel.DisplayName);
    }
    #endregion

    #region Sending Messages
    public static async Task SendMessageToChannelThreadAsync(GraphServiceClient gsc, string teamID, string channelID, string threadID, STMessage message) {
        var msg = MessageToSend(message);

        // Send the message
        _ = await gsc.Teams[teamID].Channels[channelID].Messages[threadID].Replies
            .Request()
            .AddAsync(msg);
    }

    public static async Task<ChatMessage> SendMessageToChannelAsync(GraphServiceClient gsc, string teamID, string channelID, STMessage message) {
        var msg = MessageToSend(message);

        // Send the message
        return await gsc.Teams[teamID].Channels[channelID].Messages
            .Request()
            .AddAsync(msg);
    }

    private static ChatMessage MessageToSend(STMessage message) {
        var attachments = new List<ChatMessageAttachment>();
        foreach (var attachment in message.AttachedFiles) {
            attachments.Add(new ChatMessageAttachment {
                Id = attachment.TeamsGUID,
                ContentType = "reference",
                ContentUrl = attachment.TeamsURL,
                Name = attachment.Name
            });
        }

        // Create a new message
        return new ChatMessage {
            Body = new ItemBody {
                Content = message.FormattedMessage(),
                ContentType = BodyType.Html,
            },
            Attachments = attachments,
            CreatedDateTime = message.FormattedLocalTime()
        };
    }
    #endregion

    #region Getters
    public static Task<User> GetUserAsync(GraphServiceClient gsc) {
        return gsc.Me
            .Request()
            .Select(u => new {
                // Only request specific properties
                u.DisplayName,
                u.Mail,
                u.UserPrincipalName
            })
            .GetAsync();
    }
    #endregion

    #region Upload Files
    private static readonly Regex s_regexGUID = new(@"\{([^{}]+)\}*");

    private static readonly DriveItemUploadableProperties s_uploadSettings = new() {
        AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "rename" }
            }
    };
    public static async Task UploadFileToTeamChannel(GraphServiceClient gsc, HttpClient client, string teamID, string channelName, SimpleAttachment attachment) {
        // Create the upload session
        // itemPath does not need to be a path to an existing item
        string pathToItem = $"/{channelName}/{attachment.Date}/{attachment.Name}";
        var uploadSession = await gsc
            .Groups[teamID]
            .Drive
            .Root
            .ItemWithPath(pathToItem)
            .CreateUploadSession(s_uploadSettings)
            .Request()
            .PostAsync();

        var response = await client.GetAsync($"{attachment.SlackURL}");
        _ = response.EnsureSuccessStatusCode();
        await using var fileStream = await response.Content.ReadAsStreamAsync();
        _ = fileStream.Seek(0, SeekOrigin.Begin);

        // Max slice size must be a multiple of 320 KiB
        //int maxSliceSize = 320 * 1024;
        var fileUploadTask =
            new LargeFileUploadTask<DriveItem>(uploadSession, fileStream); //, maxSliceSize);

        // Create a callback that is invoked after each slice is uploaded
        //var totalLength = fileStream.Length;
        IProgress<long> progress = new Progress<long>();

        try {
            // Upload the file
            var uploadResult = await fileUploadTask.UploadAsync(progress);

            if (!uploadResult.UploadSucceeded) {
                Console.WriteLine($"Upload failed: {attachment.SlackURL}");
            }

            attachment.TeamsURL = uploadResult.ItemResponse.WebUrl;
            attachment.TeamsGUID = s_regexGUID.Match(uploadResult.ItemResponse.ETag).Groups[1].ToString();
            attachment.Name = uploadResult.ItemResponse.Name;
        } catch (ServiceException ex) {
            Console.WriteLine($"Error uploading: {ex}");
        }
    }
    #endregion
}