// Copyright (c) Isak Viste. All rights reserved.
// Licensed under the MIT license.

using System.Net.Http.Headers;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using STMigration.Models;
using STMigration.Utils;
using STMMigration.Utils;

namespace STMigration;

class Program {
    #region Main Program
    static void Main(string[] args) {
        try {
            RunAsync(args).GetAwaiter().GetResult();
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
        }

        Console.WriteLine("Press any key to exit");
        Console.ReadKey();
    }

    private static async Task RunAsync(string[] args) {
        Console.WriteLine("[Migration] Slack -> Teams");

        // Initialization
        AuthenticationConfig config = AuthenticationConfig.ReadFromJsonFile("appsettings.json");

        // Even if this is a console application here, a daemon application is a confidential client application
        IConfidentialClientApplication app;

        app = ConfidentialClientApplicationBuilder.Create(config.ClientId)
                    .WithClientSecret(config.ClientSecret)
                    .WithAuthority(new Uri(config.Authority))
                    .Build();

        // With client credentials flows the scopes is ALWAYS of the shape "resource/.default", as the 
        // application permissions need to be set statically (in the portal or by PowerShell), and then granted by
        // a tenant administrator. 
        string[] scopes = new string[] { $"{config.ApiUrl}.default" }; // Generates a scope -> "https://graph.microsoft.com/.default"

        //s_messageSlackToTeamsIDs = LoadSerializedIDs();

        // Create new migration team
        string teamID = await CreateTeam(config, app, scopes);

        await Task.Delay(5000);

        // Scan and send messages in Teams
        await ScanAndHandleMessages(config, app, scopes, args, teamID);
    }
    #endregion

    /// <summary>
    /// An example of how to authenticate the Microsoft Graph SDK using the MSAL library
    /// </summary>
    /// <returns></returns>
    private static GraphServiceClient GetAuthenticatedGraphClient(IConfidentialClientApplication app, string[] scopes) {
        GraphServiceClient graphServiceClient =
                new("https://graph.microsoft.com/V1.0/", new DelegateAuthenticationProvider(async (requestMessage) => {
                    // Retrieve an access token for Microsoft Graph (gets a fresh token if needed).
                    AuthenticationResult result = await app.AcquireTokenForClient(scopes)
                        .ExecuteAsync();

                    // Add the access token in the Authorization header of the API request.
                    requestMessage.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", result.AccessToken);
                }));

        return graphServiceClient;
    }

    #region Message Handling
    private static Dictionary<string, string> s_messageSlackToTeamsIDs = new();

    static async Task ScanAndHandleMessages(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string[] args, string teamID) {
        GraphServiceClient graphClient;
        string ownerUserId = config.OwnerUserId;

        string directory = System.IO.Directory.GetCurrentDirectory();
        string slackArchiveBasePath = GetSlackArchiveBasePath(directory, args.Length > 0 ? args[0] : string.Empty);
        // string slackChannelPath = GetSlackChannelPath(slackArchiveBasePath);
        string slackUsersPath = GetSlackUsersPath(slackArchiveBasePath);

        List<SimpleUser> slackUsers = Users.ScanUsers(slackUsersPath);

        foreach (var dir in System.IO.Directory.GetDirectories(slackArchiveBasePath)) {
            // Create migration channel
            string dirName = dir.Split("\\").Last();
            string? channelID, channelName;
            if (dirName == "xGeneral") {
                (channelID, channelName) = await GetGeneralChannel(app, scopes, teamID);
            } else {
                (channelID, channelName) = await CreateChannel(config, app, scopes, teamID, dirName);
            }

            if (string.IsNullOrEmpty(channelID) || string.IsNullOrEmpty(channelName)) {
                continue;
            }

            foreach (var file in Messages.GetFilesForChannel(dir)) {
                foreach (var message in Messages.GetMessagesForDay(file, slackUsers)) {
                    graphClient = GetAuthenticatedGraphClient(app, scopes);

                    if (!message.AttachedFiles.IsNullOrEmpty()) {
                        foreach (var attachment in message.AttachedFiles) {
                            await UploadFileToPath(graphClient, teamID, channelName, attachment);
                        }
                    }

                    if (!message.IsInThread) {
                        await SendMessageToTeamChannel(graphClient, teamID, channelID, ownerUserId, message, false);
                        continue;
                    }

                    if (message.IsParentThread) {
                        await SendMessageToTeamChannel(graphClient, teamID, channelID, ownerUserId, message, true);
                        continue;
                    }

                    await SendMessageToChannelThread(graphClient, teamID, channelID, ownerUserId, message);
                }
            }

            await GraphHelper.CompleteChannelMigrationAsync(config, app, scopes, teamID, channelID);
            Console.WriteLine($"Channel {channelName} [{channelID}] has been migrated!");
        }

        await GraphHelper.CompleteTeamMigrationAsync(config, app, scopes, teamID);

        Console.WriteLine();
        Console.WriteLine($"Team [{teamID}] has been migrated!");
    }

    static string GetSlackArchiveBasePath(string directory, string arg) {
        string slackArchiveBasePath = string.Empty;
        bool isValidPath = false;

        if (!string.IsNullOrEmpty(arg)) {
            slackArchiveBasePath = Path.GetFullPath(Path.Combine(directory, @arg));
            isValidPath = System.IO.Directory.Exists(slackArchiveBasePath);
            if (!isValidPath) {
                Console.WriteLine($"{slackArchiveBasePath} is not a valid path!");
            }
        }

        while (!isValidPath) {
            Console.WriteLine();
            Console.Write("Relative path to local Slack Archive folder: ");
            var userReadPath = Console.ReadLine() ?? string.Empty;
            slackArchiveBasePath = Path.GetFullPath(Path.Combine(directory, @userReadPath));
            isValidPath = System.IO.Directory.Exists(slackArchiveBasePath);
            if (!isValidPath) {
                Console.WriteLine($"{slackArchiveBasePath} is not a valid path! Try again...");
            }
        }

        Console.WriteLine($"Successfully retrieved: {slackArchiveBasePath}");

        return slackArchiveBasePath;
    }

    static string GetSlackUsersPath(string slackArchiveBasePath) {
        string slackUsersPath = Path.Combine(slackArchiveBasePath, "users.json");

        if (!System.IO.File.Exists(slackUsersPath)) {
            Console.WriteLine($"Could not find users json: {slackUsersPath}");
            Console.WriteLine("Exiting...");
            Environment.Exit(1);
        }

        Console.WriteLine($"Successfully retrieved: {slackUsersPath}");
        Console.WriteLine();

        return slackUsersPath;
    }
    #endregion

    #region Graph Callers
    static async Task<string> CreateTeam(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes) {
        string teamID = string.Empty;

        try {
            teamID = await GraphHelper.CreateTeamAsync(config, app, scopes);
        } catch (Exception ex) {
            Console.WriteLine($"Error creating Team: {ex.Message}");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(teamID)) {
            Console.WriteLine($"Error creating Team, ID came back null!");
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.WriteLine($"Created Team with ID: {teamID}");
        return teamID;
    }

    static async Task<(string, string)> GetGeneralChannel(IConfidentialClientApplication app, string[] scopes, string teamID) {
        GraphServiceClient graphClient = GetAuthenticatedGraphClient(app, scopes);

        string channelID = string.Empty;
        string channelName = string.Empty;

        try {
            (channelID, channelName) = await GraphHelper.GetGeneralChannelAsync(graphClient, teamID);
        } catch (Exception ex) {
            Console.WriteLine($"Error getting General Channel: {ex.Message}");
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.WriteLine($"Got General Channel '{channelName}' [{channelID}]");
        return (channelID, channelName);
    }

    static async Task<(string?, string?)> CreateChannel(AuthenticationConfig config, IConfidentialClientApplication app, string[] scopes, string teamID, string dirName) {

        string channelID = string.Empty;
        string channelName = string.Empty;

        try {
            (channelID, channelName) = await GraphHelper.CreateChannelAsync(config, app, scopes, teamID, dirName);
        } catch (Exception ex) {
            Console.WriteLine($"Error creating Channel: {ex.Message}");
            return (channelID, channelName);
        }

        Console.WriteLine();
        Console.WriteLine($"Created Channel '{channelName}' [{channelID}]");
        return (channelID, channelName);
    }

    static async Task SendMessageToChannelThread(GraphServiceClient graphClient, string teamID, string channelID, string ownerUserId, STMessage message) {
        try {
            bool result = s_messageSlackToTeamsIDs.TryGetValue(message.ThreadDate ?? message.Date, out string? threadID);
            if (result && !string.IsNullOrEmpty(threadID)) {
                await GraphHelper.SendMessageToChannelThreadAsync(graphClient, teamID, channelID, threadID, ownerUserId, message);
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    static async Task SendMessageToTeamChannel(GraphServiceClient graphClient, string teamID, string channelID, string ownerUserId, STMessage message, bool isParentThread) {
        try {
            var teamsMessage = await GraphHelper.SendMessageToChannelAsync(graphClient, teamID, channelID, ownerUserId, message);
            if (isParentThread) {
                if (s_messageSlackToTeamsIDs.TryAdd(message.ThreadDate ?? message.Date, teamsMessage.Id)) {
                    //SaveSerializeIDs(s_messageSlackToTeamsIDs);
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    static async Task UploadFileToPath(GraphServiceClient graphClient, string teamID, string channelName, SimpleAttachment attachment) {
        try {
            await GraphHelper.UploadFileToTeamChannel(graphClient, teamID, channelName, attachment);
        } catch (Exception ex) {
            Console.WriteLine($"Error uploading file: {ex.Message}");
        }
    }
    #endregion

    #region Slack Teams IDs
    static readonly string s_lookupTable = "LookupTable-IDS.json";

    static void SaveSerializeIDs(Dictionary<string, string> dict) {
        using StreamWriter file = System.IO.File.CreateText(s_lookupTable);

        JsonSerializer serializer = new();
        serializer.Serialize(file, dict);
    }

    static Dictionary<string, string> LoadSerializedIDs() {
        try {
            using StreamReader file = System.IO.File.OpenText(s_lookupTable);

            JsonSerializer serializer = new();
            var serializedIDs = serializer.Deserialize(file, typeof(Dictionary<string, string>));

            return (Dictionary<string, string>?)serializedIDs ?? new();
        } catch (FileNotFoundException) {
            Console.WriteLine("No existing lookup table, will create one!");
        } catch (Exception ex) {
            Console.WriteLine(ex);
        }

        return new();
    }
    #endregion
}