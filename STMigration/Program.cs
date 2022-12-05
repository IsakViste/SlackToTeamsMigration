// Copyright (c) Isak Viste. All rights reserved.
// Licensed under the MIT license.

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
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("[Migration] Slack -> Teams");
        Console.ResetColor();

        // Initialization
        AuthenticationConfig config = AuthenticationConfig.ReadFromJsonFile("appsettings.json");
        GraphHelper graphHelper = new(config);

        //s_messageSlackToTeamsIDs = LoadSerializedIDs();

        string directory = Directory.GetCurrentDirectory();
        string slackArchiveBasePath = GetSlackArchiveBasePath(directory, args.Length > 0 ? args[0] : string.Empty);

        bool loadCurrentUserList = false;
        if (UsersHelper.UserListExists()) {
            Console.Write("Do you want to load the current userList? [Y/n] ");
            string? input = Console.ReadLine();
            if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
                loadCurrentUserList = true;
            }
        }

        List<STUser> userList = await ScanAndHandleUsers(graphHelper, slackArchiveBasePath, loadCurrentUserList);

        // Create new migration team
        string teamID = await CreateTeam(graphHelper);

        // Scan and send messages in Teams
        await ScanAndHandleMessages(graphHelper, slackArchiveBasePath, userList, teamID);
    }
    #endregion

    static async Task<List<STUser>> ScanAndHandleUsers(GraphHelper graphHelper, string slackArchiveBasePath, bool loadUserListInstead) {
        if (loadUserListInstead) {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Loading user from pre-computed userList!");
            Console.ResetColor();
            return UsersHelper.LoadUserList();
        }

        string slackUsersPath = GetSlackUsersPath(slackArchiveBasePath);

        List<STUser> userList = UsersHelper.ScanUsersFromSlack(slackUsersPath);
        await UsersHelper.PopulateTeamsUsers(graphHelper, userList);
        UsersHelper.StoreUserList(userList);

        return userList;
    }

    #region Message Handling
    static async Task ScanAndHandleMessages(GraphHelper graphHelper, string slackArchiveBasePath, List<STUser> userList, string teamID) {
        foreach (var dir in Directory.GetDirectories(slackArchiveBasePath)) {
            // Create migration channel
            string dirName = dir.Split("\\").Last();
            string? channelID, channelName;
            if (dirName == "xGeneral") {
                (channelID, channelName) = await GetGeneralChannel(graphHelper, teamID);
            } else {
                (channelID, channelName) = await CreateChannel(graphHelper, teamID, dirName);
            }

            if (string.IsNullOrEmpty(channelID) || string.IsNullOrEmpty(channelName)) {
                continue;
            }

            foreach (var file in MessageHandling.GetFilesForChannel(dir)) {
                foreach (var message in MessageHandling.GetMessagesForDay(file, userList)) {
                    // if (!message.AttachedFiles.IsNullOrEmpty()) {
                    //     foreach (var attachment in message.AttachedFiles) {
                    //         await UploadFileToPath(graphHelper, teamID, channelName, attachment);
                    //     }
                    // }

                    if (!message.IsInThread || message.IsParentThread) {
                        await SendMessageToTeamChannel(graphHelper, teamID, channelID, message);
                        continue;
                    }

                    await SendMessageToChannelThread(graphHelper, teamID, channelID, message);
                }
            }

            await CompleteChannelMigrationAsync(graphHelper, teamID, channelID);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Channel {channelName} [{channelID}] has been migrated!");
            Console.ResetColor();
        }

        await CompleteTeamMigrationAsync(graphHelper, teamID);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("!! MIGRATION WAS A SUCCESS !!");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Team [{teamID}] has been migrated!");
        Console.ResetColor();
    }
    #endregion

    #region Migration Handling
    static async Task CompleteChannelMigrationAsync(GraphHelper graphHelper, string teamID, string channelID) {
        try {
            await graphHelper.CompleteChannelMigrationAsync(teamID, channelID);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error finishing migration of channel: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }

    static async Task CompleteTeamMigrationAsync(GraphHelper graphHelper, string teamID) {
        try {
            await graphHelper.CompleteTeamMigrationAsync(teamID);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error finishing migration of team: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
    #endregion

    #region File Handling
    static string GetSlackArchiveBasePath(string directory, string arg) {
        string slackArchiveBasePath = string.Empty;
        bool isValidPath = false;

        if (!string.IsNullOrEmpty(arg)) {
            slackArchiveBasePath = Path.GetFullPath(Path.Combine(directory, @arg));
            isValidPath = Directory.Exists(slackArchiveBasePath);
            if (!isValidPath) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{slackArchiveBasePath} is not a valid path!");
                Console.ResetColor();
            }
        }

        while (!isValidPath) {
            Console.WriteLine();
            Console.Write("Relative path to local Slack Archive folder: ");
            var userReadPath = Console.ReadLine() ?? string.Empty;
            slackArchiveBasePath = Path.GetFullPath(Path.Combine(directory, @userReadPath));
            isValidPath = Directory.Exists(slackArchiveBasePath);
            if (!isValidPath) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{slackArchiveBasePath} is not a valid path! Try again...");
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"Successfully retrieved: {slackArchiveBasePath}");
        Console.ResetColor();

        return slackArchiveBasePath;
    }

    static string GetSlackUsersPath(string slackArchiveBasePath) {
        string slackUsersPath = Path.Combine(slackArchiveBasePath, "users.json");

        if (!File.Exists(slackUsersPath)) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Could not find users json: {slackUsersPath}");
            Console.WriteLine("Exiting...");
            Console.ResetColor();
            Environment.Exit(1);
        }

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"Successfully retrieved: {slackUsersPath}");
        Console.ResetColor();
        Console.WriteLine();

        return slackUsersPath;
    }
    #endregion

    #region Graph Callers
    static async Task<string> CreateTeam(GraphHelper graphHelper) {
        string teamID = string.Empty;

        try {
            teamID = await graphHelper.CreateTeamAsync();
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error creating Team: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        if (string.IsNullOrEmpty(teamID)) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error creating Team, ID came back null!");
            Console.ResetColor();
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Created Team with ID: {teamID}");
        Console.ResetColor();

        await Task.Delay(2000); // ? Wait for team to be accessible (otherwise first channel migration will fail!)

        return teamID;
    }

    static async Task<(string, string)> GetGeneralChannel(GraphHelper graphHelper, string teamID) {
        string channelID = string.Empty;
        string channelName = string.Empty;

        try {
            (channelID, channelName) = await graphHelper.GetGeneralChannelAsync(teamID);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error getting General Channel: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Got General Channel '{channelName}' [{channelID}]");
        Console.ResetColor();
        return (channelID, channelName);
    }

    static async Task<(string?, string?)> CreateChannel(GraphHelper graphHelper, string teamID, string dirName) {

        string channelID = string.Empty;
        string channelName = string.Empty;

        try {
            (channelID, channelName) = await graphHelper.CreateChannelAsync(teamID, dirName);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error creating Channel: {ex.Message}");
            Console.ResetColor();
            return (channelID, channelName);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Created Channel '{channelName}' [{channelID}]");
        Console.ResetColor();
        return (channelID, channelName);
    }

    static async Task SendMessageToChannelThread(GraphHelper graphHelper, string teamID, string channelID, STMessage message) {
        try {
            if (!string.IsNullOrEmpty(message.TeamID)) {
                var teamsMessage = await graphHelper.SendMessageToChannelThreadAsync(teamID, channelID, message.TeamID, message);
            }
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error sending message: {ex.Message}");
            Console.ResetColor();
        }
    }

    static async Task SendMessageToTeamChannel(GraphHelper graphHelper, string teamID, string channelID, STMessage message) {
        try {
            var teamsMessage = await graphHelper.SendMessageToChannelAsync(teamID, channelID, message);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error sending message: {ex.Message}");
            Console.ResetColor();
        }
    }

    static async Task UploadFileToPath(GraphHelper graphHelper, string teamID, string channelName, STAttachment attachment) {
        try {
            await graphHelper.UploadFileToTeamChannel(teamID, channelName, attachment);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error uploading file: {ex.Message}");
            Console.ResetColor();
        }
    }
    #endregion
}