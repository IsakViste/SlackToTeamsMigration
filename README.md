# SlackToTeamsMigration

## How to run the completed project

### Prerequisites

To run the completed project in this folder, you need the following:

- The [.NET SDK](https://dotnet.microsoft.com/download) installed on your development machine. (**Note:** This tutorial was written with .NET SDK version 6.0.102. The steps in this guide may work with other versions, but that has not been tested.)
- A Microsoft work or school account.

If you don't have a Microsoft account, you can [sign up for the Microsoft 365 Developer Program](https://developer.microsoft.com/microsoft-365/dev-program) to get a free Microsoft 365 subscription.

### Register an application

You can register an application using the Azure Active Directory admin center, or by using the [Microsoft Graph PowerShell SDK](https://docs.microsoft.com/graph/powershell/get-started).

**NOTE:** If you downloaded this code from [https://developer.microsoft.com/graph/quick-start](https://developer.microsoft.com/graph/quick-start), an app registration has already been created for you. However, if you want to use the app-only portion of this sample, you will need to modify the app registration as specified in [Configure app-only auth (AAD admin center)](#configure-app-only-auth-aad-admin-center) or [Configure app-only auth (PowerShell)](#configure-app-only-auth-powershell).

#### Azure Active Directory admin center

1. Open a browser and navigate to the [Azure Active Directory admin center](https://aad.portal.azure.com) and login using a **personal account** (aka: Microsoft Account) or **Work or School Account**.

1. Select **Azure Active Directory** in the left-hand navigation, then select **App registrations** under **Manage**.

1. Select **New registration**. Enter a name for your application, for example, `.NET Graph Tutorial`.

1. Set **Supported account types** as desired. The options are:

    | Option | Who can sign in? |
    |--------|------------------|
    | **Accounts in this organizational directory only** | Only users in your Microsoft 365 organization |
    | **Accounts in any organizational directory** | Users in any Microsoft 365 organization (work or school accounts) |
    | **Accounts in any organizational directory ... and personal Microsoft accounts** | Users in any Microsoft 365 organization (work or school accounts) and personal Microsoft accounts |

1. Leave **Redirect URI** empty.

1. Select **Register**. On the application's **Overview** page, copy the value of the **Application (client) ID** and save it, you will need it in the next step. If you chose **Accounts in this organizational directory only** for **Supported account types**, also copy the **Directory (tenant) ID** and save it.

1. Select **Authentication** under **Manage**. Locate the **Advanced settings** section and change the **Allow public client flows** toggle to **Yes**, then choose **Save**.

##### Configure app-only auth (AAD admin center)

> **Note:** This section requires a work/school account with the Global administrator role. You only need to complete these steps if you plan on using the app-only portions of this sample.

1. Select **API permissions** under **Manage**.

1. Remove the default **User.Read** permission under **Configured permissions** by selecting the ellipses (**...**) in its row and selecting **Remove permission**.

1. Select **Add a permission**, then **Microsoft Graph**.

1. Select **Application permissions**.

1. Select **User.Read.All**, then select **Add permissions**.

1. Select **Grant admin consent for...**, then select **Yes** to provide admin consent for the selected permission.

1. Select **Certificates and secrets** under **Manage**, then select **New client secret**.

1. Enter a description, choose a duration, and select **Add**.

1. Copy the secret from the **Value** column, you will need it in the next steps.


### Configure the sample

1. Open [appsettings.json](./GraphTutorial/appsettings.json) and update the values according to the following table.

    | Setting | Value |
    |---------|-------|
    | `clientId` | The client ID of your app registration |
    | `tenantId` | The tenant ID of your organization (only needed if doing app-only) |
    | `authTenant` | If you chose the option to only allow users in your organization to sign in, change this value to your tenant ID. Otherwise leave as `common`. |

### Build and run the sample

In your command-line interface (CLI), navigate to the project directory and run the following commands.

```Shell
dotnet restore
dotnet build
dotnet run
```

