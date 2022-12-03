// Copyright (c) Isak Viste. All rights reserved.
// Licensed under the MIT license.

using System.Net.Http.Headers;
using Microsoft.Graph;
using Microsoft.Identity.Client;

namespace STMigration;

public class DeviceCodeAuthProvider : IAuthenticationProvider {

    #region Initialization
    private readonly IPublicClientApplication _msalClient;
    private readonly string[] _scopes;
    private IAccount? _userAccount;

    public DeviceCodeAuthProvider(string appId, string[] scopes) {
        _scopes = scopes;

        _msalClient = PublicClientApplicationBuilder
            .Create(appId)
            .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount, true)
            .Build();
    }

    // This is the required method to implement IAuthenticationProvider
    // The Graph SDK will call this method each time it makes a Graph
    // call.
    public async Task AuthenticateRequestAsync(HttpRequestMessage requestMessage) {
        requestMessage.Headers.Authorization =
            new AuthenticationHeaderValue("bearer", await GetAccessToken());
    }

    public async Task<string?> GetAccessToken() {
        // If there is no saved user account, the user must sign-in
        if (_userAccount == null) {
            try {
                // Invoke device code flow so user can sign-in with a browser
                var result = await _msalClient.AcquireTokenWithDeviceCode(_scopes, callback => {
                    Console.WriteLine(callback.Message);
                    return Task.FromResult(0);
                }).ExecuteAsync();

                _userAccount = result.Account;
                return result.AccessToken;
            } catch (Exception exception) {
                Console.WriteLine($"Error getting access token: {exception.Message}");
                return null;
            }
        } else {
            // If there is an account, call AcquireTokenSilent
            // By doing this, MSAL will refresh the token automatically if
            // it is expired. Otherwise it returns the cached token.

            var result = await _msalClient
                .AcquireTokenSilent(_scopes, _userAccount)
                .ExecuteAsync();

            return result.AccessToken;
        }
    }
    #endregion
}
