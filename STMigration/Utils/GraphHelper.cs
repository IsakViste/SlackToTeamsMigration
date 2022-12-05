using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Newtonsoft.Json;
using STMigration.Models;

namespace STMigration.Utils;

public class GraphHelper {
    AuthenticationConfig Config { get; set; }
    IConfidentialClientApplication App { get; set; }

    // With client credentials flows the scopes is ALWAYS of the shape "resource/.default",
    // as the application permissions need to be set statically (in the portal or by PowerShell),
    // and then granted by a tenant administrator. 
    string[] Scopes { get; set; }

    public GraphHelper(AuthenticationConfig config) {
        Config = config;
        App = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                    .WithClientSecret(config.ClientSecret)
                    .WithAuthority(new Uri(config.Authority))
                    .Build();
        Scopes = new string[] { $"{config.ApiUrl}.default" }; // Generates a scope -> "https://graph.microsoft.com/.default"
    }

    /// <summary>
    /// Authenticate the Microsoft Graph SDK using the MSAL library
    ///</summary>
    private GraphServiceClient GraphClient => new("https://graph.microsoft.com/V1.0/", new DelegateAuthenticationProvider(async (requestMessage) => {
        // Retrieve an access token for Microsoft Graph (gets a fresh token if needed).
        AuthenticationResult result = await App.AcquireTokenForClient(Scopes)
            .ExecuteAsync();

        // Add the access token in the Authorization header of the API request.
        requestMessage.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", result.AccessToken);
    }));

    #region API Handling
    public async Task<JsonNode?> GetFromMSGraph(string apiCall) {
        AuthenticationResult? result = null;
        try {
            result = await App.AcquireTokenForClient(Scopes)
                .ExecuteAsync();
        } catch (MsalServiceException ex) when (ex.Message.Contains("AADSTS70011")) {
            // Invalid scope. The scope has to be of the form "https://resourceurl/.default"
            // Mitigation: change the scope to be as expected
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Scope provided is not supported");
            Console.ResetColor();
        }

        // The following example uses a Raw Http call 
        if (result != null) {
            var httpClient = new HttpClient();
            var apiCaller = new ProtectedApiCallHelper(httpClient);
            return await apiCaller.GetWebApiCall($"{Config.ApiUrl}v1.0/{apiCall}", result.AccessToken);
        }

        return null;
    }

    public async Task<HttpResponseMessage?> PostToMSGraph(string apiCall, HttpContent content) {
        AuthenticationResult? result = null;
        try {
            result = await App.AcquireTokenForClient(Scopes)
                .ExecuteAsync();
        } catch (MsalServiceException ex) when (ex.Message.Contains("AADSTS70011")) {
            // Invalid scope. The scope has to be of the form "https://resourceurl/.default"
            // Mitigation: change the scope to be as expected
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Scope provided is not supported");
            Console.ResetColor();
        }

        // The following example uses a Raw Http call 
        if (result != null) {
            var httpClient = new HttpClient();
            var apiCaller = new ProtectedApiCallHelper(httpClient);
            return await apiCaller.PostWebApiCall($"{Config.ApiUrl}v1.0/{apiCall}", result.AccessToken, content);
        }

        return null;
    }
    #endregion

    #region Team Handling
    public async Task<string> CreateTeamAsync() {
        using StreamReader reader = new("Data/team.json");
        string json = reader.ReadToEnd();
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostToMSGraph("teams", content);

        if (response == null) {
            throw new ArgumentNullException("response", "cannot be null");
        }

        if (response.Headers.TryGetValues("Location", out IEnumerable<string>? values)) {
            Regex regex = new(@"\'([^'']+)\'*");
            return regex.Match(values.First()).Groups[1].ToString();
        }

        throw new ArgumentNullException("response", "must contain Location with team id");
    }

    public async Task CompleteTeamMigrationAsync(string teamID) {
        var apiCall = $"teams/{teamID}/completeMigration";

        _ = await PostToMSGraph(apiCall, new StringContent(""));

        // Add owner to the new team
        var ownerUser = new AadUserConversationMember {
            Roles = new List<string>() {
                "owner"
            },
            AdditionalData = new Dictionary<string, object>() {
                {"user@odata.bind", $"https://graph.microsoft.com/v1.0/users('{Config.OwnerUserId}')"}
            }
        };

        await GraphClient.Teams[teamID].Members
            .Request()
            .AddAsync(ownerUser);
    }
    #endregion

    #region Channel Handling
    public async Task<(string, string)> CreateChannelAsync(string teamID, string dirName) {
        STChannel channel = new(dirName, "2020-04-14T11:22:17.047Z");
        string json = JsonConvert.SerializeObject(channel);
        json = json.Replace("}", ", \"@microsoft.graph.channelCreationMode\": \"migration\"}");
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostToMSGraph($"teams/{teamID}/channels", content);

        if (response == null) {
            throw new ArgumentNullException("response", "cannot be null");
        }

        string responseContent = await response.Content.ReadAsStringAsync();
        JsonNode? jsonNode = JsonNode.Parse(responseContent);

        if (jsonNode == null) {
            throw new ArgumentNullException("jsonNode", "cannot be null or empty");
        }

        string? channelID = jsonNode["id"]?.GetValue<string>();
        string? channelName = jsonNode["displayName"]?.GetValue<string>();

        if (string.IsNullOrEmpty(channelID)) {
            throw new ArgumentNullException("channelID", "cannot be null");
        }

        if (string.IsNullOrEmpty(channelName)) {
            throw new ArgumentNullException("channelName", "cannot be null");
        }

        return (channelID, channelName);
    }

    public async Task CompleteChannelMigrationAsync(string teamID, string channelID) {
        var apiCall = $"teams/{teamID}/channels/{channelID}/completeMigration";

        _ = await PostToMSGraph(apiCall, new StringContent(""));
    }
    #endregion

    #region Getters
    public async Task<IGraphServiceUsersCollectionPage> GetTeamUsers() {
        return await GraphClient.Users
        .Request()
        .Select(u => new {
            u.Id,
            u.Mail
        })
        .GetAsync();
    }

    public async Task<(string, string)> GetGeneralChannelAsync(string teamID) {
        var channels = await GraphClient.Teams[teamID].Channels
            .Request()
            .Select(c => new {
                // Only request specific properties
                c.DisplayName,
                c.Id,
            })
            .GetAsync();

        string generalID = string.Empty;
        foreach (var channel in channels) {
            if (channel.DisplayName.ToLower() == "general") {
                generalID = channel.Id;
                break;
            }
        }

        if (string.IsNullOrEmpty(generalID)) {
            throw new ArgumentNullException("generalID", "cannot be null");
        }

        var generalChannel = await GraphClient.Teams[teamID].Channels[generalID]
            .Request()
            .Select(c => new {
                c.DisplayName,
                c.Id,
            })
            .GetAsync();

        if (string.IsNullOrEmpty(generalChannel.Id) || string.IsNullOrEmpty(generalChannel.DisplayName)) {
            throw new ArgumentNullException("generalChannel", "must contain ID and DisplayName");
        }

        return (generalChannel.Id, generalChannel.DisplayName);
    }
    #endregion

    #region Sending Messages
    public async Task<ChatMessage> SendMessageToChannelThreadAsync(string teamID, string channelID, string threadID, STMessage message) {
        var msg = MessageToSend(message);

        // Send the message
        return await GraphClient.Teams[teamID].Channels[channelID].Messages[threadID].Replies
            .Request()
            .AddAsync(msg);
    }

    public async Task<ChatMessage> SendMessageToChannelAsync(string teamID, string channelID, STMessage message) {
        var msg = MessageToSend(message);

        // Send the message
        return await GraphClient.Teams[teamID].Channels[channelID].Messages
            .Request()
            .AddAsync(msg);
    }

    private static ChatMessage MessageToSend(STMessage message) {
        var attachments = new List<ChatMessageAttachment>();
        // foreach (var attachment in message.AttachedFiles) {
        //     attachments.Add(new ChatMessageAttachment {
        //         Id = attachment.TeamsGUID,
        //         ContentType = "reference",
        //         ContentUrl = attachment.TeamsURL,
        //         Name = attachment.Name
        //     });
        // }

        if (message.User != null && !string.IsNullOrEmpty(message.User.TeamsUserID)) {
            // Message that has a team user equivalent
            return new ChatMessage {
                Body = new ItemBody {
                    Content = message.FormattedMessage(),
                    ContentType = BodyType.Html,
                },
                From = new ChatMessageFromIdentitySet {
                    User = new Identity {
                        Id = message.User.TeamsUserID,
                        DisplayName = message.User.DisplayName
                    }
                },
                Attachments = attachments,
                CreatedDateTime = message.FormattedLocalTime()
            };
        }

        // Message that doesn't have team user equivalent
        return new ChatMessage {
            Body = new ItemBody {
                Content = message.FormattedMessage(),
                ContentType = BodyType.Html,
            },
            From = new ChatMessageFromIdentitySet {
                User = new Identity {
                    DisplayName = message.User?.DisplayName ?? "Unknown"
                }
            },
            Attachments = attachments,
            CreatedDateTime = message.FormattedLocalTime()
        };
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

    public async Task UploadFileToTeamChannel(string teamID, string channelName, STAttachment attachment) {
        // Create the upload session
        // itemPath does not need to be a path to an existing item
        string pathToItem = $"/{channelName}/{attachment.Date}/{attachment.Name}";
        var uploadSession = await GraphClient
            .Groups[teamID]
            .Drive
            .Root
            .ItemWithPath(pathToItem)
            .CreateUploadSession(s_uploadSettings)
            .Request()
            .PostAsync();

        HttpClient client = new();

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