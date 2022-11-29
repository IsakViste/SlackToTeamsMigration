// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;

using STMigration.Models;
using STMigration.Utils;

using STMMigration.Utils;

namespace STMigration;

class Program {

    static async Task Main(string[] args) {
        Console.WriteLine("[Migration] Slack -> Teams");

        List<ComplexMessage> messages = GetMessagesAsync(args);
        await MainProgram(messages);
    }

    static async Task MainProgram(List<ComplexMessage> messages) {
        static int ChoiceInput() {
            var choice = -1;
            try {
                choice = int.Parse(Console.ReadLine() ?? string.Empty);
            } catch (FormatException) {
                Environment.Exit(0);
            }
            return choice;
        }

        var settings = Settings.LoadSettings();

        // Initialize Graph
        InitializeHttpClient();
        InitializeGraph(settings);

        // Greet the user by name
        await GreetUserAsync();

        Console.WriteLine("Which Team would you like to migrate too?");
        var joinedTeams = await ListJoinedTeamsAsync();

        var choice = ChoiceInput();

        var teamID = joinedTeams[choice].Id;
        Console.WriteLine($"You have chosen: {joinedTeams[choice].DisplayName}");

        Console.WriteLine($"");
        Console.WriteLine($"Which Channel would you like to send a message too?");
        var channels = await ListTeamChannelsAsync(teamID);

        choice = ChoiceInput();

        var channelID = channels[choice].Id;
        var channelName = channels[choice].DisplayName;
        Console.WriteLine($"You have chosen: {channelName}");

        foreach (ComplexMessage complexMessage in messages) {
            SimpleMessage simpleMessage = complexMessage.Message;
            ChatMessage? teamsMessage = await SendMessageToChannel(simpleMessage, teamID, channelID, channelName);

            if (string.IsNullOrEmpty(teamsMessage?.Id)) {
                continue;
            }

            if (complexMessage.IsThread && complexMessage.ThreadMessages?.Count > 0) {
                foreach (SimpleMessage threadMessage in complexMessage.ThreadMessages) {
                    _ = await SendMessageToChannel(threadMessage, teamID, channelID, channelName, teamsMessage.Id);
                }
            }
        }
    }

    static async Task<ChatMessage?> SendMessageToChannel(SimpleMessage message, string teamID, string channelID, string channelName, string threadID = "") {
        if (!message.AttachedFiles.IsNullOrEmpty()) {
            foreach (var attachment in message.AttachedFiles) {
                await UploadFileToPath(teamID, channelName, attachment);
            }
        }

        if (!string.IsNullOrEmpty(threadID)) {
            await SendMessageToChannelThread(teamID, channelID, threadID, message);
            return null;
        }

        ChatMessage teamsMessage = await SendMessageToTeamChannel(teamID, channelID, message);
        return teamsMessage;
    }

    static List<ComplexMessage> GetMessagesAsync(string[] args) {
        List<ComplexMessage> messages = new();

        if (args.Length > 1) {
            string slackArchiveBasePath = args[0];
            string usersPath = Path.Combine(slackArchiveBasePath, "users.json");
            string channelToMigrate = args[1];

            List<SimpleUser> slackUsers = Users.ScanUsers(usersPath);

            messages = Messages.ScanMessagesByChannel(slackArchiveBasePath, channelToMigrate, slackUsers);
        } else {
            Console.WriteLine($"Please specify path to slack export folder and name of channel to migrate");
            Console.WriteLine($"e.g. dotnet run SlackExport General");
        }

        return messages;
    }

    static void InitializeHttpClient() {
        GraphHelper.InitializeHttpClient();
    }

    static void InitializeGraph(Settings settings) {
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

    static async Task GreetUserAsync() {
        try {
            var user = await GraphHelper.GetUserAsync();
            Console.WriteLine($"");
            Console.WriteLine($"Hello, {user?.DisplayName}!");
            // For Work/school accounts, email is in Mail property
            // Personal accounts, email is in UserPrincipalName
            Console.WriteLine($"Email: {user?.Mail ?? user?.UserPrincipalName ?? ""}");
        } catch (Exception ex) {
            Console.WriteLine($"Error getting user: {ex.Message}");
        }
        Console.WriteLine($"");
    }

    static async Task SendMessageToChannelThread(string teamID, string channelID, string threadID, SimpleMessage message) {
        try {
            await GraphHelper.SendMessageToChannelThreadAsync(teamID, channelID, threadID, message);
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }

    }

    static async Task<ChatMessage> SendMessageToTeamChannel(string teamID, string channelID, SimpleMessage message) {
        try {
            return await GraphHelper.SendMessageToChannelAsync(teamID, channelID, message);
        } catch (Exception ex) {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }

        return new ChatMessage();
    }

    static async Task<ITeamChannelsCollectionPage> ListTeamChannelsAsync(string teamID) {
        try {
            var channels = await GraphHelper.GetTeamChannelsAsync(teamID);

            int index = 0;
            foreach (var channel in channels) {
                Console.WriteLine($"[{index}] {channel.DisplayName} (({channel.Id}))");
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
                Console.WriteLine($"[{index}] {team.DisplayName} (({team.Id}))");
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
        try {
            await GraphHelper.UploadFileToTeamChannel(teamID, channelName, attachment);
        } catch (Exception ex) {
            Console.WriteLine($"Error uploading file: {ex.Message}");
        }
    }
}