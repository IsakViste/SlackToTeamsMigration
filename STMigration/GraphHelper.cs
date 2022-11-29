// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.


using System.Text.RegularExpressions;

using Azure.Core;
using Azure.Identity;

using Microsoft.Graph;

using STMigration.Models;


namespace STMigration;

class GraphHelper {
	#region User-auth
	// <UserAuthConfigSnippet>
	// Settings object
	private static Settings? _settings;
	// User auth token credential
	private static DeviceCodeCredential? _deviceCodeCredential;
	// Client configured with user authentication
	private static GraphServiceClient? _userClient;

	private static HttpClient? _httpClient;
	private static Regex GUIDRegex = new Regex(@"\{([^{}]+)\}*");

	private static DriveItemUploadableProperties _uploadSettings = new DriveItemUploadableProperties {
		AdditionalData = new Dictionary<string, object>
			{
				{ "@microsoft.graph.conflictBehavior", "rename" }
			}
		};


	public static void InitializeHttpClient () {
		_httpClient = new HttpClient ();
	}

	public static void InitializeGraphForUserAuth (Settings settings,
		Func<DeviceCodeInfo, CancellationToken, Task> deviceCodePrompt) {
		_settings = settings;

		_deviceCodeCredential = new DeviceCodeCredential(deviceCodePrompt,
			settings.AuthTenant, settings.ClientId);

		_userClient = new GraphServiceClient(_deviceCodeCredential, settings.GraphUserScopes);
	}
	// </UserAuthConfigSnippet>

	// <SendMessageToChannelThreadSnippet>
	public static async Task SendMessageToChannelThreadAsync (string teamID, string channelID, string threadID, SimpleMessage message) {
		// Ensure client isn't null
		_ = _userClient ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		var msg = MessageToSend(message);

		// Send the message
		_ = await _userClient.Teams[teamID].Channels[channelID].Messages[threadID].Replies
			.Request()
			.AddAsync(msg);
	}
	// </SendMessageToChannelThreadSnippet>

	// <SendMessageToChannelSnippet>
	public static async Task<ChatMessage> SendMessageToChannelAsync (string teamID, string channelID, SimpleMessage message) {
		// Ensure client isn't null
		_ = _userClient ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		var msg = MessageToSend(message);

		// Send the message
		return await _userClient.Teams[teamID].Channels[channelID].Messages
			.Request()
			.AddAsync(msg);
	}
	// </SendMessageToChannelSnippet>

	private static ChatMessage MessageToSend(SimpleMessage message) {
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
		return (new ChatMessage {
			Body = new ItemBody {
				Content = message.FormattedMessage(),
				ContentType = BodyType.Html,
			},
			Attachments = attachments
		});
	}

	// <GetTeamChannelsSnippet>
	public static Task<ITeamChannelsCollectionPage> GetTeamChannelsAsync (string teamID) {
		_ = _userClient ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		return _userClient.Teams[teamID].Channels
			.Request()
			.GetAsync();
	}
	// </GetTeamChannelsSnippet>

	// <GetJoinedTeamsSnippet>
	public static Task<IUserJoinedTeamsCollectionPage> GetJoinedTeamsAsync () {
		_ = _userClient ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		return _userClient.Me
			.JoinedTeams
			.Request()
			.GetAsync();
	}
	// </GetJoinedTeamsSnippet>

	// <GetUserSnippet>
	public static Task<User> GetUserAsync () {
		// Ensure client isn't null
		_ = _userClient ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		return _userClient.Me
			.Request()
			.Select(u => new {
				// Only request specific properties
				u.DisplayName,
				u.Mail,
				u.UserPrincipalName
			})
			.GetAsync();
	}
	// </GetUserSnippet>

	// <GetUserTokenSnippet>
	public static async Task<string> GetUserTokenAsync () {
		// Ensure credential isn't null
		_ = _deviceCodeCredential ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		// Ensure scopes isn't null
		_ = _settings?.GraphUserScopes ?? throw new ArgumentNullException("Argument 'scopes' cannot be null");

		// Request token with given scopes
		var context = new TokenRequestContext(_settings.GraphUserScopes);
		var response = await _deviceCodeCredential.GetTokenAsync(context);
		return response.Token;
	}
	// </GetUserTokenSnippet>

	public static async Task UploadFileToTeamChannel (string teamID, string channelName, SimpleAttachment attachment) {
		// Ensure client isn't null
		_ = _userClient ??
			throw new NullReferenceException("Graph has not been initialized for user auth");

		_ = _httpClient ??
			throw new NullReferenceException("Http Client has not been initialized");

		// Create the upload session
		// itemPath does not need to be a path to an existing item
		string pathToItem = $"/{channelName}/MigrationFiles/{attachment.Date}/{attachment.Name}";
		var uploadSession = await _userClient
			.Groups[teamID]
			.Drive
			.Root
			.ItemWithPath(pathToItem)
			.CreateUploadSession(_uploadSettings)
			.Request()
			.PostAsync();

		var response = await _httpClient.GetAsync($"{attachment.SlackURL}");
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
			attachment.TeamsGUID = GUIDRegex.Match(uploadResult.ItemResponse.ETag).Groups[1].ToString();
			attachment.Name = uploadResult.ItemResponse.Name;
		} catch (ServiceException ex) {
			Console.WriteLine($"Error uploading: {ex}");
		}
	}

	#endregion
}
