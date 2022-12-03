// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;

namespace STMigration;

#pragma warning disable CS8618

/// <summary>
/// Description of the configuration of an AzureAD public client application (desktop/mobile application). This should
/// match the application registration done in the Azure portal
/// </summary>
public class AuthenticationConfig {
    /// <summary>
    /// instance of Azure AD, for example public Azure or a Sovereign cloud (Azure China, Germany, US government, etc ...)
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/{0}";

    /// <summary>
    /// Graph API endpoint, could be public Azure (default) or a Sovereign cloud (US government, etc ...)
    /// </summary>
    public string ApiUrl { get; set; } = "https://graph.microsoft.com/";

    /// <summary>
    /// The Tenant is:
    /// - either the tenant ID of the Azure AD tenant in which this application is registered (a guid)
    /// or a domain name associated with the tenant
    /// - or 'organizations' (for a multi-tenant application)
    /// </summary>
    public string Tenant { get; set; }

    /// <summary>
    /// Guid used by the application to uniquely identify itself to Azure AD
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// URL of the authority
    /// </summary>
    public string Authority {
        get {
            return string.Format(CultureInfo.InvariantCulture, Instance, Tenant);
        }
    }

    /// <summary>
    /// Client secret (application password)
    /// </summary>
    /// <remarks>Daemon applications can authenticate with AAD through two mechanisms: ClientSecret
    /// (which is a kind of application password: this property)
    /// or a certificate previously shared with AzureAD during the application registration 
    /// (and identified by the Certificate property belows)
    /// <remarks> 
    public string ClientSecret { get; set; }

    public string OwnerUserId { get; set; }

    /// <summary>
    /// Reads the configuration from a json file
    /// </summary>
    /// <param name="path">Path to the configuration json file</param>
    /// <returns>AuthenticationConfig read from the json file</returns>
    public static AuthenticationConfig ReadFromJsonFile(string path) {
        IConfigurationRoot configuration;

        var builder = new ConfigurationBuilder()
        .SetBasePath(System.IO.Directory.GetCurrentDirectory())
        .AddJsonFile(path);

        configuration = builder.Build();
        return configuration.Get<AuthenticationConfig>();
    }
}

