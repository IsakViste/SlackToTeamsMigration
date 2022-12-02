using System.Collections.Specialized;
// Copyright (c) Isak Viste. All rights reserved.
// Licensed under the MIT license.

using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using STMigration.Models;

namespace STMigration;

class GraphHelper {

    #region Initialization
    private static DeviceCodeCredential? s_deviceCodeCredential;
    private static GraphServiceClient? s_userClient;

    private static readonly string[] s_scopes = new string[] {
      "user.read"
    };

    private static readonly DriveItemUploadableProperties s_uploadSettings = new() {
        AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "rename" }
            }
    };

    public static void InitializeGraphForUserAuth(AppSettings settings,
        Func<DeviceCodeInfo, CancellationToken, Task> deviceCodePrompt) {
        s_deviceCodeCredential = new DeviceCodeCredential(deviceCodePrompt,
            settings.AuthTenant, settings.ClientId);

        s_userClient = new GraphServiceClient(s_deviceCodeCredential, s_scopes);
    }

    public static async Task<string> GetUserTokenAsync() {
        // Ensure credential isn't null
        _ = s_deviceCodeCredential ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        // Ensure scopes isn't null
        _ = s_scopes ?? throw new ArgumentNullException("Argument 'scopes' cannot be null");

        // Request token with given scopes
        var context = new TokenRequestContext(s_scopes);
        var response = await s_deviceCodeCredential.GetTokenAsync(context);
        return response.Token;
    }
    #endregion

    #region Sending Messages
    public static async Task SendMessageToChannelThreadAsync(string teamID, string channelID, string threadID, STMessage message) {
        // Ensure client isn't null
        _ = s_userClient ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        var msg = MessageToSend(message);

        // Send the message
        _ = await s_userClient.Teams[teamID].Channels[channelID].Messages[threadID].Replies
            .Request()
            .AddAsync(msg);
    }

    public static async Task<ChatMessage> SendMessageToChannelAsync(string teamID, string channelID, STMessage message) {
        // Ensure client isn't null
        _ = s_userClient ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        var msg = MessageToSend(message);

        // Send the message
        return await s_userClient.Teams[teamID].Channels[channelID].Messages
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
            Attachments = attachments
        };
    }
    #endregion

    #region Getters
    public static Task<ITeamChannelsCollectionPage> GetTeamChannelsAsync(string teamID) {
        _ = s_userClient ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        return s_userClient.Teams[teamID].Channels
            .Request()
            .Select(c => new {
                c.Id,
                c.DisplayName
            })
            .GetAsync();
    }

    public static Task<IUserJoinedTeamsCollectionPage> GetJoinedTeamsAsync() {
        _ = s_userClient ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        return s_userClient.Me
            .JoinedTeams
            .Request()
            .Select(t => new {
                t.Id,
                t.DisplayName
            })
            .GetAsync();
    }

    public static Task<User> GetUserAsync() {
        // Ensure client isn't null
        _ = s_userClient ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        return s_userClient.Me
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

    public static async Task UploadFileToTeamChannel(HttpClient client, string teamID, string channelName, SimpleAttachment attachment) {
        // Ensure client isn't null
        _ = s_userClient ??
            throw new NullReferenceException("Graph has not been initialized for user auth");

        // Create the upload session
        // itemPath does not need to be a path to an existing item
        string pathToItem = $"/{channelName}/{attachment.Date}/{attachment.Name}";
        var uploadSession = await s_userClient
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
