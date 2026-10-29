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
            yield return SignIn(config.judgeEmail, config.judgePassword, onError);
        }

        /// Signs out locally (drops the token and cached profile). The next sign-in starts clean.
        public void SignOut()
        {
            accessToken = null;
            UserId = null;
            DisplayName = null;
        }

        /// Password sign-in for any A Better U account. RLS then scopes every call to that user.
        public IEnumerator SignIn(string email, string password, Action<string> onError)
        {
            if (!Configured) { onError("Supabase url and anon key must be set in ModelApiSecrets."); yield break; }
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) { onError("Enter your email and password."); yield break; }

            var body = JsonUtility.ToJson(new SignInBody { email = email.Trim(), password = password });
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

        /// Calls a Postgres function through PostgREST (e.g. search_foods). RLS applies as the signed-in user.
        public IEnumerator Rpc(string fn, string json, Action<string> ok, Action<string> fail) =>
            Send($"{config.supabaseUrl}/rest/v1/rpc/{fn}", "POST", json, false, ok, fail);

        /// Uploads a file into a storage bucket as the signed-in user and returns its public URL. The bucket's policy decides where
        /// they may write (atlas-post-images: only into a folder named after their own user id).
        public IEnumerator UploadPublic(string bucket, string path, byte[] data, string contentType, Action<string> ok, Action<string> fail)
        {
            if (!SignedIn) { fail("Sign in first."); yield break; }
            using var req = new UnityWebRequest($"{config.supabaseUrl}/storage/v1/object/{bucket}/{path}", "POST")
            {
                uploadHandler = new UploadHandlerRaw(data),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("apikey", config.supabaseAnonKey);
            req.SetRequestHeader("Authorization", "Bearer " + accessToken);
            req.SetRequestHeader("Content-Type", contentType);
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) ok($"{config.supabaseUrl}/storage/v1/object/public/{bucket}/{path}");
            else fail($"{req.responseCode}: {req.downloadHandler.text}");
        }

        /// Reads rows through PostgREST, e.g. Get("user_injuries?select=body_area&status=eq.active", ...).
        public IEnumerator Get(string pathAndQuery, Action<string> ok, Action<string> fail) =>
            Send($"{config.supabaseUrl}/rest/v1/{pathAndQuery}", "GET", null, false, ok, fail);

        /// Inserts one row and returns the stored row, so callers confirm the write really happened.
        public IEnumerator Insert(string table, string json, Action<string> ok, Action<string> fail) =>
            Send($"{config.supabaseUrl}/rest/v1/{table}", "POST", json, true, ok, fail);

        /// Deletes exactly one diary entry belonging to this signed-in user; RLS still applies.
        public IEnumerator DeleteFoodEntry(string entryId, Action ok, Action<string> fail)
        {
            if (!SignedIn || !Guid.TryParse(UserId, out var owner) || !Guid.TryParse(entryId, out var id))
            {
                fail("Sign in and select a valid food diary entry.");
                yield break;
            }
            string raw = null, error = null;
            yield return Send($"{config.supabaseUrl}/rest/v1/food_log_entries?id=eq.{id:D}&user_id=eq.{owner:D}&select=id",
                "DELETE", null, true, r => raw = r, e => error = e);
            if (error != null) { fail(error); yield break; }
            if (!ConfirmsDeletedEntry(raw, entryId))
            {
                fail("The entry was not deleted. Refresh and try again.");
                yield break;
            }
            ok();
        }

        internal static bool ConfirmsDeletedEntry(string json, string entryId)
        {
            if (!Guid.TryParse(entryId, out var expected)) return false;
            try
            {
                var rows = Newtonsoft.Json.Linq.JArray.Parse(json);
                return rows.Count == 1 && Guid.TryParse((string)rows[0]["id"], out var actual) && actual == expected;
            }
            catch (Exception) { return false; }
        }

        /// Inserts a measured-workout row once; a timed-out request may be retried with the same UUID.
        public IEnumerator InsertWorkoutRowOnce(string table, Newtonsoft.Json.Linq.JObject row, Action<string> done)
        {
            if ((table != "strength_sets" && table != "exercise_logs") || !SignedIn || (string)row["user_id"] != UserId ||
                !Guid.TryParse((string)row["id"], out var id))
            { done("Sign in to the workout's account before saving."); yield break; }
            using var request = Build($"{config.supabaseUrl}/rest/v1/{table}?on_conflict=id", "POST",
                row.ToString(Newtonsoft.Json.Formatting.None), true);
            request.SetRequestHeader("Prefer", "resolution=ignore-duplicates,return=representation");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { done("The workout save was not confirmed. Please retry."); yield break; }
            string raw = null, error = null;
            // Check the stored values as well as HTTP success, including an earlier successful retry.
            yield return Get($"{table}?id=eq.{id:D}&user_id=eq.{UserId}&select=*", r => raw = r, e => error = e);
            if (error != null) { done("Could not verify the saved set. Please retry."); yield break; }
            bool matches = false;
            try
            {
                var rows = Newtonsoft.Json.Linq.JArray.Parse(raw);
                string repsField = table == "strength_sets" ? "reps" : "reps_completed";
                matches = rows.Count == 1 && (string)rows[0]["id"] == (string)row["id"] &&
                    (string)rows[0]["user_id"] == UserId && rows[0][repsField].ToString() == row[repsField].ToString();
            }
            catch (Exception) { /* no verified row means no saved claim */ }
            done(matches ? null : "The stored set did not match. Please retry.");
        }

        IEnumerator Send(string url, string method, string json, bool returnRow, Action<string> ok, Action<string> fail)
        {
            using var req = Build(url, method, json, true);
            if (returnRow) req.SetRequestHeader("Prefer", "return=representation");
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success) ok(req.downloadHandler.text);
            else fail($"{req.responseCode}: {req.downloadHandler.text}");
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
