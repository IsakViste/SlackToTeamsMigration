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
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine();
        Console.WriteLine("================================");
        Console.WriteLine("|| [MIGRATION] Slack -> Teams ||");
        Console.WriteLine("================================");
        Console.WriteLine();
        Console.ResetColor();

        // Initialization
        AuthenticationConfig config = AuthenticationConfig.ReadFromJsonFile("appsettings.json");
        GraphHelper graphHelper = new(config);

        // File Handling
        string directory = Directory.GetCurrentDirectory();
        string slackArchiveBasePath = GetSlackArchiveBasePath(directory, args.Length > 0 ? args[0] : string.Empty);

        /*
        ** MIGRATE TEAM
        */
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Do you want to migrate MESSAGES to a new team? [Y/n] ");
        Console.ResetColor();
        string? input = Console.ReadLine();

        string? teamID = string.Empty;
        if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {

            /*
            ** LOADING USER LIST OR CREATING NEW
            */
            bool loadCurrentUserList = false;
            if (UsersHelper.UserListExists()) {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Do you want to load the current userList? [Y/n] ");
                Console.ResetColor();
                input = Console.ReadLine();
                if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
                    loadCurrentUserList = true;
                }
            }

            List<STUser> userList = await ScanAndHandleUsers(graphHelper, slackArchiveBasePath, loadCurrentUserList);

            // Create new migration team
            teamID = await CreateTeam(graphHelper);

            // Scan and send messages in Teams
            await ScanAndHandleMessages(graphHelper, slackArchiveBasePath, userList, teamID);
        }

        /*
        ** MIGRATE ATTACHMENTS TO EXISTING TEAM
        */
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("Do you want to migrate ATTACHMENTS to a new team? [Y/n] ");
        Console.ResetColor();
        input = Console.ReadLine();

        if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
            // If we did not just migrate, we can ask the user to provide the team
            if (string.IsNullOrEmpty(teamID)) {
                var teams = await ListJoinedTeamsAsync(graphHelper);
                int index = 0;
                Console.ForegroundColor = ConsoleColor.White;
                foreach (var team in teams) {
                    Console.WriteLine($"[{index}] {team.DisplayName} ({team.Id})");
                    index++;
                }
                Console.ResetColor();

                int choice;
                do {
                    choice = UserInputIndexOfList();
                    if (choice < 0 || choice >= teams.Count) {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Not a valid selection, must be between 0 and {teams.Count}");
                        Console.ResetColor();
                    }
                } while (choice < 0 || choice >= teams.Count);

                teamID = teams[choice].Id;
            }

            await UploadAttachments(graphHelper, slackArchiveBasePath, teamID);
        }

        static int UserInputIndexOfList() {
            var choice = -1;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Select: ");
            Console.ResetColor();
            try {
                choice = int.Parse(Console.ReadLine() ?? string.Empty);
            } catch (FormatException ex) {
                Console.WriteLine(ex.Message);
            }
            return choice;
        }
    }
    #endregion

    static async Task UploadAttachments(GraphHelper graphHelper, string slackArchiveBasePath, string teamID) {
        foreach (var dir in Directory.GetDirectories(slackArchiveBasePath)) {
            string channelID = string.Empty;
            string channelName = dir.Split("\\").Last();

            if (channelName == "xGeneral") {
                channelName = "General";
            }

            channelID = await GetChannelByName(graphHelper, teamID, channelName);

            foreach (var file in MessageHandling.GetFilesForChannel(dir)) {
                foreach (var attachmentList in MessageHandling.GetAttachmentsForDay(file)) {
                    if (attachmentList == null || !attachmentList.Any()) {
                        continue;
                    }

                    foreach (var attachment in attachmentList) {
                        await UploadFileToPath(graphHelper, teamID, channelName, attachment);
                    }

                    await AddAttachmentsToMessage(graphHelper, teamID, channelID, attachmentList);
                }
            }
        }
    }

    #region User Handling
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
    #endregion

    #region Message Handling
    static async Task ScanAndHandleMessages(GraphHelper graphHelper, string slackArchiveBasePath, List<STUser> userList, string teamID) {
        foreach (var dir in Directory.GetDirectories(slackArchiveBasePath)) {
            // Create migration channel
            string dirName = dir.Split("\\").Last();
            string? channelID;
            if (dirName == "xGeneral") {
                channelID = await GetChannelByName(graphHelper, teamID, "General");
            } else {
                channelID = await CreateChannel(graphHelper, teamID, dirName);
            }

            if (string.IsNullOrEmpty(channelID)) {
                continue;
            }

            foreach (var file in MessageHandling.GetFilesForChannel(dir)) {
                foreach (var message in MessageHandling.GetMessagesForDay(file, userList)) {
                    if (!message.IsInThread || message.IsParentThread) {
                        await SendMessageToTeamChannel(graphHelper, teamID, channelID, message);
                        continue;
                    }

                    await SendMessageToChannelThread(graphHelper, teamID, channelID, message);
                }
            }

            await CompleteChannelMigrationAsync(graphHelper, teamID, channelID);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Channel {dirName} [{channelID}] has been migrated!");
            Console.ResetColor();
        }

        await CompleteTeamMigrationAsync(graphHelper, teamID);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"Team [{teamID}] has been migrated!");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine("===================================");
        Console.WriteLine("|| !! MIGRATION WAS A SUCCESS !! ||");
        Console.WriteLine("===================================");
        Console.WriteLine();
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
        Console.WriteLine();
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
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"Created Team with ID: {teamID}");
        Console.ResetColor();

        await Task.Delay(2000); // ? Wait for team to be accessible (otherwise first channel migration will fail!)

        return teamID;
    }

    static async Task<Microsoft.Graph.IUserJoinedTeamsCollectionPage> ListJoinedTeamsAsync(GraphHelper graphHelper) {
        try {
            var joinedTeams = await graphHelper.GetJoinedTeamsAsync();
            return joinedTeams;
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error getting user's teams: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    static async Task<string> GetChannelByName(GraphHelper graphHelper, string teamID, string channelName) {
        string channelID = string.Empty;

        try {
            channelID = await graphHelper.GetChannelByNameAsync(teamID, channelName);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error getting Channel {channelName}: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Got General Channel [{channelID}]");
        Console.ResetColor();
        return channelID;
    }

    static async Task<string?> CreateChannel(GraphHelper graphHelper, string teamID, string dirName) {

        string channelID = string.Empty;

        try {
            channelID = await graphHelper.CreateChannelAsync(teamID, dirName);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error creating Channel: {ex.Message}");
            Console.ResetColor();
            return channelID;
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Created Channel '{dirName}' [{channelID}]");
        Console.ResetColor();
        return channelID;
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
            await graphHelper.UploadFileToTeamChannelAsync(teamID, channelName, attachment);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error uploading file: {ex.Message}");
            Console.ResetColor();
        }
    }

    static async Task AddAttachmentsToMessage(GraphHelper graphHelper, string teamID, string channelID, List<STAttachment> attachments) {
        try {
            await graphHelper.AddAttachmentsToMessageAsync(teamID, channelID, attachments);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error adding attachment to message: {ex.Message}");
            Console.ResetColor();
        }
    }

    #endregion
}