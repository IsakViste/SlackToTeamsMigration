using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using STMigration.Models;

namespace STMMigration.Utils;

public class Users {
    public static List<SimpleUser> ScanUsers(string combinedPath) {
        var simpleUserList = new List<SimpleUser>();
        using (FileStream fs = new(combinedPath, FileMode.Open, FileAccess.Read))
        using (StreamReader sr = new(fs))
        using (JsonTextReader reader = new(sr)) {
            while (reader.Read()) {
                if (reader.TokenType == JsonToken.StartObject) {
                    JObject obj = JObject.Load(reader);

                    // SelectToken returns null not an empty string if nothing is found
                    string? userId = obj.SelectToken("id")?.ToString();
                    string? name = obj.SelectToken("profile.real_name_normalized")?.ToString();
                    string? email = obj.SelectToken("profile.email")?.ToString();

                    if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email)) {
                        continue;
                    }

                    var is_bot = obj.SelectToken("is_bot");
                    bool isBot = false;
                    if (is_bot != null) {
                        isBot = (bool)is_bot;
                    }

                    SimpleUser user = new(userId, name, email, isBot);

                    simpleUserList.Add(user);
                }
            }
        }
        return simpleUserList;
    }
}
