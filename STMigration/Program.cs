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

        InitializeIDLookUpTable();
        InitializeHttpClient();
        InitializeGraph(settings);

        // Greet user
        await GreetUserAsync();

        // Choose team to migrate too
        string teamID = await GetTeamToMigrateToo();

        // Choose channel to migrate too
        var (channelID, channelName) = await GetChannelToMigrateToo(teamID);

        // Scan and send messages in Teams
        await ScanAndHandleMessages(args, teamID, channelID, channelName);
    }
    #endregion

    #region Helpers
    static async Task<(string, string)> GetChannelToMigrateToo(string teamID) {
        Console.WriteLine();
        Console.WriteLine($"Which Channel would you like to send a message too?");
        var channels = await ListTeamChannelsAsync(teamID);

        int choice;
        do {
            choice = UserInputIndexOfList();
            if (choice < 0 || choice >= channels.Count) {
                Console.WriteLine($"Not a valid selection, must be between 0 and {channels.Count}");
            }
        } while (choice < 0 || choice >= channels.Count);
        string channelName = channels[choice].DisplayName;
        Console.WriteLine($"You have chosen: {channelName}");

        return (channels[choice].Id, channelName);
    }

    static async Task<string> GetTeamToMigrateToo() {
        Console.WriteLine();
        Console.WriteLine("Which Team would you like to migrate too?");
        var joinedTeams = await ListJoinedTeamsAsync();

        int choice;
        do {
            choice = UserInputIndexOfList();
            if (choice < 0 || choice >= joinedTeams.Count) {
                Console.WriteLine($"Not a valid selection, must be between 0 and {joinedTeams.Count}");
            }
        } while (choice < 0 || choice >= joinedTeams.Count);

        Console.WriteLine($"You have chosen: {joinedTeams[choice].DisplayName}");

        return joinedTeams[choice].Id;
    }

    static int UserInputIndexOfList() {
        var choice = -1;

        Console.Write("Select: ");
        try {
            choice = int.Parse(Console.ReadLine() ?? string.Empty);
        } catch (FormatException ex) {
            Console.WriteLine(ex.Message);
        }
        return choice;
    }
    #endregion

    #region Initialization
    private static Dictionary<string, string> s_messageSlackToTeamsIDs = new();
    private static HttpClient? s_httpClient;

    static void InitializeHttpClient() {
        s_httpClient = new HttpClient();
    }

    static void InitializeIDLookUpTable() {
        s_messageSlackToTeamsIDs = LoadSerializedIDs();
    }

    static void InitializeGraph(AppSettings settings) {
        Console.WriteLine();

        GraphHelper.InitializeGraphForUserAuth(settings,
            (info, cancel) => {
                // Display the device code message to
                // the user. This tells them
                // where to go to sign in and provides the
                // code to use.
                Console.WriteLine(info.Message);
                return Task.FromResult(0);
            });
    }
    #endregion

    #region Message Handling
    static async Task ScanAndHandleMessages(string[] args, string teamID, string channelID, string channelName) {
        string directory = System.IO.Directory.GetCurrentDirectory();
        string slackArchiveBasePath = GetSlackArchiveBasePath(directory, args.Length > 0 ? args[0] : string.Empty);
        string slackChannelPath = GetSlackChannelPath(slackArchiveBasePath);
        string slackUsersPath = GetSlackUsersPath(slackArchiveBasePath);

        List<SimpleUser> slackUsers = Users.ScanUsers(slackUsersPath);

        foreach (var file in Messages.GetFilesForChannel(slackChannelPath)) {
            foreach (var message in Messages.GetMessagesForDay(file, slackUsers)) {
                if (!message.AttachedFiles.IsNullOrEmpty()) {
                    foreach (var attachment in message.AttachedFiles) {
                        await UploadFileToPath(teamID, channelName, attachment);
                    }
                }

                if (!message.IsInThread) {
                    await SendMessageToTeamChannel(teamID, channelID, message, false);
                    continue;
                }

                if (message.IsParentThread) {
                    await SendMessageToTeamChannel(teamID, channelID, message, true);
                    continue;
                }

                await SendMessageToChannelThread(teamID, channelID, message);
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
    static async Task GreetUserAsync() {
        try {
            var user = await GraphHelper.GetUserAsync();
            Console.WriteLine();
            Console.WriteLine($"Hello, {user?.DisplayName}!");
            // For Work/school accounts, email is in Mail property
            // Personal accounts, email is in UserPrincipalName
            Console.WriteLine($"Email: {user?.Mail ?? user?.UserPrincipalName ?? ""}");
        } catch (Exception ex) {
            Console.WriteLine($"Error getting user: {ex.Message}");
        }
    }

    static async Task SendMessageToChannelThread(string teamID, string channelID, STMessage message) {
        try {
            bool result = s_messageSlackToTeamsIDs.TryGetValue(message.ThreadDate ?? message.Date, out string? threadID);
            if (result && !string.IsNullOrEmpty(threadID)) {
                await GraphHelper.SendMessageToChannelThreadAsync(teamID, channelID, threadID, message);
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    static async Task SendMessageToTeamChannel(string teamID, string channelID, STMessage message, bool isParentThread) {
        try {
            var teamsMessage = await GraphHelper.SendMessageToChannelAsync(teamID, channelID, message);
            if (isParentThread) {
                if (s_messageSlackToTeamsIDs.TryAdd(message.ThreadDate ?? message.Date, teamsMessage.Id)) {
                    SaveSerializeIDs(s_messageSlackToTeamsIDs);
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    static async Task<ITeamChannelsCollectionPage> ListTeamChannelsAsync(string teamID) {
        try {
            var channels = await GraphHelper.GetTeamChannelsAsync(teamID);

            int index = 0;
            foreach (var channel in channels) {
                Console.WriteLine($"[{index}] {channel.DisplayName} ({channel.Id})");
                index++;
            }
            return channels;
        } catch (Exception ex) {
            Console.WriteLine($"Error getting user's team channels: {ex.Message}");
            Console.WriteLine($"");
            throw;
        }
    }

    static async Task<IUserJoinedTeamsCollectionPage> ListJoinedTeamsAsync() {
        try {
            var joinedTeams = await GraphHelper.GetJoinedTeamsAsync();

            int index = 0;
            foreach (var team in joinedTeams) {
                Console.WriteLine($"[{index}] {team.DisplayName} ({team.Id})");
                index++;
            }
            return joinedTeams;
        } catch (Exception ex) {
            Console.WriteLine($"Error getting user's teams: {ex.Message}");
            Console.WriteLine($"");
            throw;
        }
    }

    static async Task UploadFileToPath(string teamID, string channelName, SimpleAttachment attachment) {
        // Ensure client isn't null
        _ = s_httpClient ??
            throw new NullReferenceException("HTTP Client has not been initialized");

        try {
            await GraphHelper.UploadFileToTeamChannel(s_httpClient, teamID, channelName, attachment);
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