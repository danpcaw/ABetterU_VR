using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ABetterU.Coach
{
    /// Minimal Supabase REST client: password sign-in plus authenticated calls.
    /// Every call carries the anon key in `apikey` and the user's JWT in Authorization,
    /// so RLS scopes all reads and writes to the signed-in user.
    public class SupabaseClient
    {
        readonly ModelApiConfig config;

        public string UserId { get; private set; }
        public string DisplayName { get; private set; }
        public bool SignedIn => !string.IsNullOrEmpty(accessToken);
        string accessToken;

        public SupabaseClient(ModelApiConfig config) { this.config = config; }

        public bool Configured => !string.IsNullOrEmpty(config.supabaseUrl) && !string.IsNullOrEmpty(config.supabaseAnonKey);

        [Serializable] class SignInResponse { public string access_token; public User user; }
        [Serializable] class User { public string id; }
        [Serializable] class SignInBody { public string email; public string password; }
        [Serializable] class ProfileRow { public string name; }
        [Serializable] class ProfileRows { public ProfileRow[] rows; }

        /// "Judge Instant Login": signs in with the judge account from the secrets asset.
        public IEnumerator SignInJudge(Action<string> onError)
        {
            if (!Configured || string.IsNullOrEmpty(config.judgePassword))
            {
                onError("Supabase url, anon key and judge password must be set in ModelApiSecrets.");
                yield break;
            }

            var body = JsonUtility.ToJson(new SignInBody { email = config.judgeEmail, password = config.judgePassword });
            using var req = Build($"{config.supabaseUrl}/auth/v1/token?grant_type=password", "POST", body, false);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError($"Sign-in failed ({req.responseCode}): {req.downloadHandler.text}");
                yield break;
            }

            var res = JsonUtility.FromJson<SignInResponse>(req.downloadHandler.text);
            accessToken = res.access_token;
            UserId = res.user != null ? res.user.id : null;

            // Greeting needs the name; RLS lets the user read only their own profile row.
            using var prof = Build($"{config.supabaseUrl}/rest/v1/profiles?select=name&user_id=eq.{UserId}", "GET", null, true);
            yield return prof.SendWebRequest();
            if (prof.result == UnityWebRequest.Result.Success)
            {
                var rows = JsonUtility.FromJson<ProfileRows>("{\"rows\":" + prof.downloadHandler.text + "}");
                if (rows.rows != null && rows.rows.Length > 0) DisplayName = rows.rows[0].name;
            }
        }

        UnityWebRequest Build(string url, string method, string json, bool auth)
        {
            var req = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer() };
            if (json != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SetRequestHeader("apikey", config.supabaseAnonKey);
            if (auth) req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            return req;
        }
    }
}
