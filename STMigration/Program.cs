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

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.Write("Press any key to exit ");
        Console.ResetColor();
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

        /*
        ** INITIALIZATION
        */
        AuthenticationConfig config = AuthenticationConfig.ReadFromJsonFile("Data/appsettings.json");
        GraphHelper graphHelper = new(config);

        /*
        ** FILE HANDLING
        */
        string directory = Directory.GetCurrentDirectory();
        string slackArchiveBasePath = GetSlackArchiveBasePath(directory, args.Length > 0 ? args[0] : string.Empty);

        /*
        ** LOADING USER LIST OR CREATING NEW
        */
        string? input;
        bool loadCurrentUserList = false;
        if (UsersHelper.UserListExists()) {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"We found existing User List: {UsersHelper.USER_LIST_FILE}");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Do you want to load it? [Y/n] ");
            Console.ResetColor();
            input = Console.ReadLine();
            if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
                loadCurrentUserList = true;
            }
        }

        List<STUser> userList = await ScanAndHandleUsers(graphHelper, slackArchiveBasePath, loadCurrentUserList);
        Console.WriteLine();

        /*
        ** MIGRATE TEAM
        */
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Do you want to create a new migration team and migrate MESSAGES? [Y/n] ");
        Console.ResetColor();
        input = Console.ReadLine();

        string? teamID = string.Empty;
        if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
            // Create new migration team
            teamID = await CreateTeam(graphHelper);

            // Scan and send messages in Teams
            await ScanAndHandleMessages(graphHelper, slackArchiveBasePath, userList, teamID);
        }

        /*
        ** MIGRATE ATTACHMENTS TO EXISTING TEAM
        */
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Do you want to migrate ATTACHMENTS to a team? [Y/n] ");
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
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Not a valid selection, must be between 0 and {teams.Count}");
                        Console.ResetColor();
                    }
                } while (choice < 0 || choice >= teams.Count);

                teamID = teams[choice].Id;
            }

            await UploadAttachments(graphHelper, slackArchiveBasePath, userList, teamID);
        }

        /*
        ** Finish migration of a team if necessary
        */
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Do you want to finish migrating an existing team stuck in migration? [y/N] ");
        Console.ResetColor();
        input = Console.ReadLine();

        if (!string.IsNullOrEmpty(input) && (input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true")) {
            await FinishMigrating(graphHelper);
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

    // If migration failed and you're left with a team stuck in migration mode, use this function!
    private static async Task FinishMigrating(GraphHelper graphHelper) {
        string? teamID;
        do {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Which team do you want to finish migrating? ");
            Console.ResetColor();
            teamID = Console.ReadLine();
        } while (string.IsNullOrEmpty(teamID));

        foreach (var channel in await ListJoinedTeamsAsync(graphHelper, teamID)) {
            await CompleteChannelMigrationAsync(graphHelper, teamID, channel.Id, channel.DisplayName);
        }

        await CompleteTeamMigrationAsync(graphHelper, teamID);
    }

    static async Task UploadAttachments(GraphHelper graphHelper, string slackArchiveBasePath, List<STUser> userList, string teamID) {
        foreach (var dir in Directory.GetDirectories(slackArchiveBasePath)) {
            string channelID = string.Empty;
            string channelName = dir.Split("\\").Last();

            if (channelName == "zzzGeneral") {
                channelName = "General";
            }

            channelID = await GetChannelByName(graphHelper, teamID, channelName);

            foreach (var file in MessageHandling.GetFilesForChannel(dir)) {
                foreach (var message in MessageHandling.GetMessagesForDay(file, userList)) {
                    if (message == null || !message.Attachments.Any()) {
                        continue;
                    }

                    foreach (var attachment in message.Attachments) {
                        await UploadFileToPath(graphHelper, teamID, channelName, attachment);
                    }

                    await AddAttachmentsToMessage(graphHelper, teamID, channelID, message);
                }
            }
        }
    }

    #region User Handling
    static async Task<List<STUser>> ScanAndHandleUsers(GraphHelper graphHelper, string slackArchiveBasePath, bool loadUserListInstead) {
        string? input;

        if (loadUserListInstead) {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Loading user from existing User List!");

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Do you want to populate TeamIDs? [Y/n] ");
            Console.ResetColor();
            input = Console.ReadLine();

            List<STUser> users = UsersHelper.LoadUserList();
            if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
                await UsersHelper.PopulateTeamsUsers(graphHelper, users);
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("The Users Team IDs have been updated!");
                Console.ResetColor();

                UsersHelper.StoreUserList(users);
                return users;
            } else {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("The Users Team IDS have been kept as is!");
                Console.ResetColor();
            }

            return users;
        }

        string slackUsersPath = GetSlackUsersPath(slackArchiveBasePath);

        List<STUser> userList = UsersHelper.ScanUsersFromSlack(slackUsersPath);
        await UsersHelper.PopulateTeamsUsers(graphHelper, userList);
        UsersHelper.StoreUserList(userList);

        // Ask user if he wants to reload it so he can make changes to it
        // after it has been computed and stored
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("You now have the possibility to make changes to this file if you want");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("Press any key to continue ");
        Console.ResetColor();
        Console.ReadKey();
        Console.WriteLine();
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.Write("Do you want to reload the User List from disk? [Y/n] ");
        Console.ResetColor();
        input = Console.ReadLine();

        if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
            List<STUser> users = UsersHelper.LoadUserList();
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("The User List has been reloaded!");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Do you want to populate TeamIDs? [Y/n] ");
            Console.ResetColor();
            input = Console.ReadLine();

            if (string.IsNullOrEmpty(input) || input.ToLower() == "y" || input.ToLower() == "yes" || input.ToLower() == "true") {
                await UsersHelper.PopulateTeamsUsers(graphHelper, users);
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("The Users Team IDs have been updated!");
                Console.ResetColor();

                UsersHelper.StoreUserList(users);
            } else {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("The Users Team IDS have been kept as is!");
                Console.ResetColor();
            }

            return users;
        }

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("The User List has been kept as is!");
        Console.ResetColor();
        return userList;
    }
    #endregion

    #region Message Handling
    static async Task ScanAndHandleMessages(GraphHelper graphHelper, string slackArchiveBasePath, List<STUser> userList, string teamID) {
        foreach (var dir in Directory.GetDirectories(slackArchiveBasePath)) {
            // Create migration channel
            string dirName = dir.Split("\\").Last();
            string? channelID;
            if (dirName == "zzzGeneral") {
                dirName = "General";
                channelID = await GetChannelByName(graphHelper, teamID, dirName);
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

            await CompleteChannelMigrationAsync(graphHelper, teamID, channelID, dirName);
        }

        await CompleteTeamMigrationAsync(graphHelper, teamID);

        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine("===============================================");
        Console.WriteLine("|| !! MIGRATION OF MESSAGES WAS A SUCCESS !! ||");
        Console.WriteLine("===============================================");
        Console.ResetColor();
    }
    #endregion

    #region Migration Handling
    static async Task CompleteChannelMigrationAsync(GraphHelper graphHelper, string teamID, string channelID, string channelName) {
        try {
            await graphHelper.CompleteChannelMigrationAsync(teamID, channelID);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error finishing migration of channel: {ex.Message}");
            Console.ResetColor();
            Environment.Exit(1);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Channel {channelName} [{channelID}] has been migrated!");
        Console.ResetColor();
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

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"Team [{teamID}] has been migrated!");
        Console.WriteLine();
        Console.ResetColor();
    }
    #endregion

    #region File Handling
    static string GetSlackArchiveBasePath(string directory, string arg) {
        string slackArchiveBasePath = string.Empty;
        bool isValidPath = false;

        if (!string.IsNullOrEmpty(arg)) {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine("Retrieving Slack Export folder...");
            Console.ResetColor();

            slackArchiveBasePath = Path.GetFullPath(Path.Combine(directory, @arg));
            isValidPath = Directory.Exists(slackArchiveBasePath);
            if (!isValidPath) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{slackArchiveBasePath} is not a valid path!");
                Console.WriteLine();
                Console.ResetColor();
            }
        }

        while (!isValidPath) {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Relative path to local Slack Archive folder: ");
            Console.ResetColor();
            var userReadPath = Console.ReadLine() ?? string.Empty;
            slackArchiveBasePath = Path.GetFullPath(Path.Combine(directory, @userReadPath));
            isValidPath = Directory.Exists(slackArchiveBasePath);
            if (!isValidPath) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{slackArchiveBasePath} is not a valid path! Try again...");
                Console.WriteLine();
                Console.ResetColor();
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("Successfully retrieved Slack Export folder");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(slackArchiveBasePath);
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

        Console.ForegroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("Successfully retrieved Slack Users file");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(slackUsersPath);
        Console.ResetColor();

        return slackUsersPath;
    }
    #endregion

    #region Graph Callers
    public static readonly string TEAM_DATA_FILE = "Data/team.json";
    static async Task<string> CreateTeam(GraphHelper graphHelper) {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"Creating new Team from data of: {TEAM_DATA_FILE}");
        Console.ResetColor();

        string teamID = string.Empty;

        try {
            teamID = await graphHelper.CreateTeamAsync(TEAM_DATA_FILE);
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

    static async Task<Microsoft.Graph.ITeamChannelsCollectionPage> ListJoinedTeamsAsync(GraphHelper graphHelper, string teamID) {
        try {
            var channels = await graphHelper.GetTeamsChannelsAsync(teamID);
            return channels;
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error getting teams channels: {ex.Message}");
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

    static async Task AddAttachmentsToMessage(GraphHelper graphHelper, string teamID, string channelID, STMessage message) {
        try {
            await graphHelper.AddAttachmentsToMessageAsync(teamID, channelID, message);
        } catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error adding attachment to message: {ex.Message}");
            Console.ResetColor();
        }
    }

    #endregion
}