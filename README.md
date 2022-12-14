# SlackToTeamsMigration <!-- omit from toc -->

- [Prerequisites](#prerequisites)
- [Register Application](#register-application)
- [Configure AAD Application](#configure-aad-application)
- [Configure App Settings](#configure-app-settings)
- [Build and Run](#build-and-run)

## Prerequisites

To run the completed project in this folder, you need the following:

- The [.NET SDK](https://dotnet.microsoft.com/download) installed on your development machine.

- A Microsoft work or school account.

If you don't have a Microsoft account, you can [sign up for the Microsoft 365 Developer Program](https://developer.microsoft.com/microsoft-365/dev-program) to get a free Microsoft 365 subscription.

## Register Application

1. Open a browser and navigate to the [Azure Active Directory admin center](https://aad.portal.azure.com) and login using a **personal account** (aka: Microsoft Account) or **Work or School Account**.

1. <details>
    <summary><strong>[CLICK ME]</strong> Select <strong>Azure Active Directory</strong> in the left-hand navigation, then select <strong>App registrations</strong> under <strong>Manage</strong>.</summary>
    <img src="./imgs/01-AzureActiveDirectory.png" />
    </details>


2. Register a new application to the **Azure Active Directory**

    1. <details>
        <summary><strong>[CLICK ME]</strong> Select <strong>New registration</strong>.</summary>
        <img src="./imgs/02-NewAppRegistration.png" />
        </details>
    
    2. Enter a name for your application, for example, `STMigration`.

3. Set **Supported account types** as desired. The options are:

    | Option                                                                           | Who can sign in?                                                                                  |
    | -------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- |
    | **Accounts in this organizational directory only**                               | Only users in your Microsoft 365 organization                                                     |
    | **Accounts in any organizational directory**                                     | Users in any Microsoft 365 organization (work or school accounts)                                 |
    | **Accounts in any organizational directory ... and personal Microsoft accounts** | Users in any Microsoft 365 organization (work or school accounts) and personal Microsoft accounts |

4. Leave **Redirect URI** empty.

5. Select **Register**.

6. <details>
    <summary><strong>[CLICK ME]</strong> Get the <strong>Client</strong> and <strong>Tenant ID</strong> on the application's <strong>Overview</strong> page</summary>
    <img src="./imgs/03-NewAppIDs.png" />
    </details>

    1. Copy the value of the **Application (client) ID** and save it, you will need it later.
    
    2. Also copy the **Directory (tenant) ID** and save it.

7. <details>
    <summary><strong>[CLICK ME]</strong> Enable <strong>public client flows</strong></summary>
    <img src="./imgs/06-AllowPublicClientFlows.png" />
    </details>

    1. Select **Authentication** under **Manage**.
    
    2. Locate the **Advanced settings** section and change the **Allow public client flows** toggle to **Yes**, then choose **Save**.

## Configure AAD Application

> **Note:** This section requires a work/school account with the Global administrator role.

1. Select **API permissions** under **Manage**.

1. Remove the default **User.Read** permission under **Configured permissions** by selecting the ellipses (**...**) in its row and selecting **Remove permission**.

1. <details>
    <summary><strong>[CLICK ME]</strong> Select <strong>Add a permission</strong>, then <strong>Microsoft Graph</strong></summary>
    <img src="./imgs/07-AddPermissionSelectAPI.png" />
    </details>

    1. Select **Application permissions**.

    1. <details>
        <summary><strong>[CLICK ME]</strong> Select <strong>Teamwork.Migrate.All</strong></summary>
        <img src="./imgs/08-AddRequiredPermissions.png" />
        </details>

    1. Select **TeamMember.ReadWrite.All**

    1. Select **ChannelSettings.ReadWrite.All**
    
    1. then select **Add permissions**.

1. <details>
    <summary><strong>[CLICK ME]</strong> Select <strong>Grant admin consent for...</strong>, then select <strong>Yes</strong> to provide admin consent for the selected permission.</summary>
    <img src="./imgs/09-GrantAdminConsent.png" />
    </details>

1. Select **Certificates and secrets** under **Manage**, then select **New client secret**.

    1. <details>
        <summary><strong>[CLICK ME]</strong> Enter a description, choose a duration, and select <strong>Add</strong>.</summary>
        <img src="./imgs/04-NewClientSecret.png" />
        </details>

    1. <details>
        <summary><strong>[CLICK ME]</strong> Copy the secret from the <strong>Value</strong> column, you will need it soon.</summary>
        <img src="./imgs/05-CopyClientSecret.png" />
        </details>

1. Go to the online [Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer)

    1. <details>
        <summary><strong>[CLICK ME]</strong> Run the default command: <strong>GET my profile</strong></summary>
        <pre><code>https://graph.microsoft.com/v1.0/me</code></pre>
        </details>

    2. <details>
        <summary><strong>[CLICK ME]</strong> Copy the <strong>team user id</strong> and save it, you will need it in the next step!</summary>
        <img src="./imgs/10-GetTeamUserID.png" />
        </details>
    

## Configure App Settings

Open [appsettings.json](./STMigration/Data/appsettings.json) and update the values according to the following table.

|        Setting | Value                                  |
| -------------: | -------------------------------------- |
|       `Tenant` | The tenant ID of your organization     |
|     `ClientId` | The client ID of your app registration |
| `ClientSecret` | The value of the client secret         |
|  `OwnerUserId` | The user ID of your team account       |

## Build and Run

In your command-line interface (CLI), navigate to the project directory and run the following commands.

```Shell
dotnet restore
dotnet build
dotnet run
```

