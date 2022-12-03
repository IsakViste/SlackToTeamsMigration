using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace STMigration;

/// <summary>
/// Helper class to call a protected API and process its result
/// </summary>
public class ProtectedApiCallHelper {
    protected HttpClient HTTPClient { get; private set; }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="httpClient">HttpClient used to call the protected API</param>
    public ProtectedApiCallHelper(HttpClient httpClient) {
        HTTPClient = httpClient;
    }

    /// <summary>
    /// Calls the protected web API with a get async and returns the result
    /// </summary>
    /// <param name="webApiUrl">URL of the web API to call (supposed to return Json)</param>
    /// <param name="accessToken">Access token used as a bearer security token to call the web API</param>
    public async Task<JsonNode?> GetWebApiCall(string webApiUrl, string accessToken) {
        if (!string.IsNullOrEmpty(accessToken)) {
            var defaultRequestHeaders = HTTPClient.DefaultRequestHeaders;
            if (defaultRequestHeaders.Accept == null || !defaultRequestHeaders.Accept.Any(m => m.MediaType == "application/json")) {
                HTTPClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
            defaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage response = await HTTPClient.GetAsync(webApiUrl);
            if (response.IsSuccessStatusCode) {
                string json = await response.Content.ReadAsStringAsync();
                JsonNode? result = JsonNode.Parse(json);
                Console.ForegroundColor = ConsoleColor.Gray;
                return result;
            } else {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to call the web API: {response.StatusCode}");
                string content = await response.Content.ReadAsStringAsync();

                // Note that if you got response.Code == 403 and response.content.code == "Authorization_RequestDenied"
                // this is because the tenant admin as not granted consent for the application to call the Web API
                Console.WriteLine($"Content: {content}");
            }
            Console.ResetColor();
        }

        return null;
    }

    /// <summary>
    /// Calls the protected web API with a post async and returns the result
    /// </summary>
    /// <param name="webApiUrl">URL of the web API to call (supposed to return Json)</param>
    /// <param name="accessToken">Access token used as a bearer security token to call the web API</param>
    /// <param name="content">Content of the post call</param>
    public async Task<HttpResponseMessage?> PostWebApiCall(string webApiUrl, string accessToken, HttpContent content) {
        if (string.IsNullOrEmpty(accessToken)) {
            return null;
        }

        var defaultRequestHeaders = HTTPClient.DefaultRequestHeaders;
        if (defaultRequestHeaders.Accept == null || !defaultRequestHeaders.Accept.Any(m => m.MediaType == "application/json")) {
            HTTPClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        defaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response = await HTTPClient.PostAsync(webApiUrl, content);
        if (response.IsSuccessStatusCode) {
            return response;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to call the web API: {response.StatusCode}");
        string responseContent = await response.Content.ReadAsStringAsync();

        // Note that if you got response.Code == 403 and response.content.code == "Authorization_RequestDenied"
        // this is because the tenant admin as not granted consent for the application to call the Web API
        Console.WriteLine($"Content: {responseContent}");

        Console.ResetColor();
        return null;
    }
}