using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using STMigration.Models;

namespace STMigration;

public static class GraphHelper {
    /// <summary>
    /// Calls MS Graph REST API using an authenticated Http client
    /// </summary>
    /// <param name="config"></param>
    /// <param name="app"></param>
    /// <param name="scopes"></param>
    /// <param name="apiCall"></param>
    /// <returns></returns>
    public static async Task<JsonNode?> GetFromMSGraph(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string apiCall) {
        AuthenticationResult? result = null;
        try {
            result = await app.AcquireTokenForClient(scopes)
                .ExecuteAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Token acquired");
            Console.ResetColor();
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
            return await apiCaller.GetWebApiCall($"{config.ApiUrl}v1.0/{apiCall}", result.AccessToken);
        }

        return null;
    }

    /// <summary>
    /// Calls MS Graph REST API using an authenticated Http client
    /// </summary>
    /// <param name="config"></param>
    /// <param name="app"></param>
    /// <param name="scopes"></param>
    /// <param name="apiCall"></param>
    /// <param name="content"></param>
    /// <returns></returns>
    public static async Task<HttpResponseMessage?> PostToMSGraph(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string apiCall, HttpContent content) {
        AuthenticationResult? result = null;
        try {
            result = await app.AcquireTokenForClient(scopes)
                .ExecuteAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Token acquired");
            Console.ResetColor();
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
            return await apiCaller.PostWebApiCall($"{config.ApiUrl}v1.0/{apiCall}", result.AccessToken, content);
        }

        return null;
    }

    #region Team Handling
    public static async Task<string> CreateTeamAsync(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes) {
        using StreamReader reader = new("Data/team.json");
        string json = reader.ReadToEnd();
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostToMSGraph(config, app, scopes, "teams", content);

        if (response == null) {
            throw new ArgumentNullException("response", "cannot be null");
        }

        if (response.Headers.TryGetValues("Location", out IEnumerable<string>? values)) {
            Regex regex = new(@"\'([^'']+)\'*");
            return regex.Match(values.First()).Groups[1].ToString();
        }

        throw new ArgumentNullException("response", "must contain Location with team id");
    }

    public static async Task CompleteTeamMigrationAsync(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string teamID) {
        var apiCall = $"teams/{teamID}/completeMigration";

        _ = await PostToMSGraph(config, app, scopes, apiCall, new StringContent(""));
    }

    public static async Task<(string, string)> CreateChannelAsync(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string teamID, string dirName) {
        Channel channel = new(dirName, "2020-04-14T11:22:17.047Z");
        string json = JsonConvert.SerializeObject(channel);
        json = json.Replace("}", ", \"@microsoft.graph.channelCreationMode\": \"migration\"}");
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostToMSGraph(config, app, scopes, $"teams/{teamID}/channels", content);

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

    public static async Task CompleteChannelMigrationAsync(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string teamID, string channelID) {
        var apiCall = $"teams/{teamID}/channels/{channelID}/completeMigration";

        _ = await PostToMSGraph(config, app, scopes, apiCall, new StringContent(""));
    }

    public static async Task<(string, string)> GetGeneralChannelAsync(GraphServiceClient gsc, string teamID) {
        var channels = await gsc.Teams[teamID].Channels
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

        var generalChannel = await gsc.Teams[teamID].Channels[generalID]
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
    public static async Task SendMessageToChannelThreadAsync(GraphServiceClient gsc, string teamID, string channelID, string threadID, string ownerUserId, STMessage message) {
        var msg = MessageToSend(ownerUserId, message);

        // Send the message
        _ = await gsc.Teams[teamID].Channels[channelID].Messages[threadID].Replies
            .Request()
            .AddAsync(msg);
    }

    public static async Task<ChatMessage> SendMessageToChannelAsync(GraphServiceClient gsc, string teamID, string channelID, string ownerUserId, STMessage message) {
        var msg = MessageToSend(ownerUserId, message);

        // Send the message
        return await gsc.Teams[teamID].Channels[channelID].Messages
            .Request()
            .AddAsync(msg);
    }

    private static ChatMessage MessageToSend(string ownerUserId, STMessage message) {
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
            From = new ChatMessageFromIdentitySet {
                User = new Identity {
                    Id = ownerUserId,
                    DisplayName = message.User
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
    public static async Task UploadFileToTeamChannel(GraphServiceClient gsc, string teamID, string channelName, SimpleAttachment attachment) {
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