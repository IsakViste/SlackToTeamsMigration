using System;
// Copyright (c) Isak Viste. All rights reserved.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using STMigration.Models;
using STMigration.Utils;

using STMMigration.Utils;

namespace STMigration;

class Program {
    #region Main Program
    static async Task Main(string[] args) {
        Console.WriteLine("[Migration] Slack -> Teams");

        // Initialization
        var settings = AppSettings.LoadSettings();

        s_httpClient = new HttpClient();
        s_messageSlackToTeamsIDs = LoadSerializedIDs();

        s_graphClient = InitializeGraph(settings);

        // Greet user
        //await GreetUserAsync(s_graphClient);

        // Choose team to migrate too
        // string teamID = await GetTeamToMigrateToo();
        string teamID = await CreateTeam(s_graphClient, "Test Team X", "Test X Team");

        // Choose channel to migrate too
        // var (channelID, channelName) = await GetChannelToMigrateToo(teamID);
        var (channelID, channelName) = await CreateChannel(s_graphClient, teamID, "Test 233", "new test channel");

        // Scan and send messages in Teams
        await ScanAndHandleMessages(s_graphClient, s_httpClient, args, teamID, channelID, channelName);
    }
    #endregion

    #region Initialization
    // The Microsoft Graph permission scopes used by the app
    private static readonly string[] s_scopes = { "User.Read", "Mail.Read" };

    // Graph client
    private static GraphServiceClient? s_graphClient;

    private static Dictionary<string, string> s_messageSlackToTeamsIDs = new();
    private static HttpClient? s_httpClient;

    static GraphServiceClient InitializeGraph(AppSettings settings) {
        Console.WriteLine();

        if (string.IsNullOrEmpty(settings.ClientId)) {
            Console.Error.WriteLine($"Settings Client ID cannot be null");
            Environment.Exit(1);
        }

        var authProvider = new DeviceCodeAuthProvider(
                settings.ClientId, s_scopes);

        return new GraphServiceClient(authProvider);
    }
    #endregion

    #region Message Handling
    static async Task ScanAndHandleMessages(GraphServiceClient graphClient, HttpClient httpClient, string[] args, string teamID, string channelID, string channelName) {
        string directory = System.IO.Directory.GetCurrentDirectory();
        string slackArchiveBasePath = GetSlackArchiveBasePath(directory, args.Length > 0 ? args[0] : string.Empty);
        string slackChannelPath = GetSlackChannelPath(slackArchiveBasePath);
        string slackUsersPath = GetSlackUsersPath(slackArchiveBasePath);

        List<SimpleUser> slackUsers = Users.ScanUsers(slackUsersPath);

        foreach (var file in Messages.GetFilesForChannel(slackChannelPath)) {
            foreach (var message in Messages.GetMessagesForDay(file, slackUsers)) {
                if (!message.AttachedFiles.IsNullOrEmpty()) {
                    foreach (var attachment in message.AttachedFiles) {
                        await UploadFileToPath(graphClient, httpClient, teamID, channelName, attachment);
                    }
                }

                if (!message.IsInThread) {
                    await SendMessageToTeamChannel(graphClient, teamID, channelID, message, false);
                    continue;
                }

                if (message.IsParentThread) {
                    await SendMessageToTeamChannel(graphClient, teamID, channelID, message, true);
                    continue;
                }

                await SendMessageToChannelThread(graphClient, teamID, channelID, message);
            }
        }
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

    static string GetSlackChannelPath(string slackArchiveBasePath) {
        string slackChannelPath;

        bool isValidChannel;
        do {
            Console.WriteLine();
            Console.Write("Name of Slack Channel to migrate from: ");
            var userInput = Console.ReadLine() ?? string.Empty;
            slackChannelPath = Path.Combine(slackArchiveBasePath, userInput);
            isValidChannel = System.IO.Directory.Exists(slackChannelPath);
            if (!isValidChannel) {
                Console.WriteLine($"{slackChannelPath} is not a valid path! Try again...");
            }
        } while (!isValidChannel);

        Console.WriteLine($"Successfully retrieved: {slackChannelPath}");

        return slackChannelPath;
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
    static async Task GreetUserAsync(GraphServiceClient graphClient) {
        try {
            var user = await GraphHelper.GetUserAsync(graphClient);
            Console.WriteLine();
            Console.WriteLine($"Hello, {user?.DisplayName}!");
            // For Work/school accounts, email is in Mail property
            // Personal accounts, email is in UserPrincipalName
            Console.WriteLine($"Email: {user?.Mail ?? user?.UserPrincipalName ?? ""}");
        } catch (Exception ex) {
            Console.WriteLine($"Error getting user: {ex.Message}");
        }
    }

    static async Task<string> CreateTeam(GraphServiceClient graphClient, string name, string description) {
        string teamID = string.Empty;
        try {
            teamID = await GraphHelper.CreateTeamAsync(graphClient, name, description);
        } catch (Exception ex) {
            Console.WriteLine($"Error creating Team: {ex.Message}");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(teamID)) {
            Console.WriteLine($"Error creating Team, ID came back null!");
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.WriteLine($"Created Team '{name}' [{teamID}]");
        return teamID;
    }

    static async Task<(string, string)> CreateChannel(GraphServiceClient graphClient, string teamID, string name, string description) {
        string channelID = string.Empty;
        string channelName = string.Empty;

        try {
            (channelID, channelName) = await GraphHelper.CreateChannelAsync(graphClient, teamID, name, description);
        } catch (Exception ex) {
            Console.WriteLine($"Error creating Team: {ex.Message}");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(channelID)) {
            Console.WriteLine($"Error creating Channel, ID came back null!");
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(channelName)) {
            Console.WriteLine($"Error creating Channel, Name came back null!");
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.WriteLine($"Created Channel '{channelName}' [{channelID}]");
        return (channelID, channelName);
    }

    static async Task SendMessageToChannelThread(GraphServiceClient graphClient, string teamID, string channelID, STMessage message) {
        try {
            bool result = s_messageSlackToTeamsIDs.TryGetValue(message.ThreadDate ?? message.Date, out string? threadID);
            if (result && !string.IsNullOrEmpty(threadID)) {
                await GraphHelper.SendMessageToChannelThreadAsync(graphClient, teamID, channelID, threadID, message);
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    static async Task SendMessageToTeamChannel(GraphServiceClient graphClient, string teamID, string channelID, STMessage message, bool isParentThread) {
        try {
            var teamsMessage = await GraphHelper.SendMessageToChannelAsync(graphClient, teamID, channelID, message);
            if (isParentThread) {
                if (s_messageSlackToTeamsIDs.TryAdd(message.ThreadDate ?? message.Date, teamsMessage.Id)) {
                    SaveSerializeIDs(s_messageSlackToTeamsIDs);
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    static async Task UploadFileToPath(GraphServiceClient graphClient, HttpClient httpClient, string teamID, string channelName, SimpleAttachment attachment) {
        try {
            await GraphHelper.UploadFileToTeamChannel(graphClient, httpClient, teamID, channelName, attachment);
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