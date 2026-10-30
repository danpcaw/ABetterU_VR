using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ABetterU.Coach
{
    /// Sharing to the user's A Better U feed. Atlas drafts a post (caption written from real data, optional Muse Image picture) and
    /// shows a preview; nothing is posted until the user taps Post or asks her to post it. The post goes through the platform's own
    /// create_post function as the signed-in user, so the platform's rules apply, and "posted" is only ever said after it confirms.
    public partial class VoiceLoopTest
    {
        sealed class PostDraft
        {
            public string Caption;
            public string[] Tags;
            public byte[] Jpg;
            public Texture2D Preview;
            public string Visibility = "friends";     // friends is the default: the demo account has no friends, so nothing reaches real users
        }

        const string PostBucket = "atlas-post-images";
        PostDraft draft;
        Transform postPanel;
        AppFlow.Stage draftStage;
        bool posting;
        string postStatus = "";
        byte[] lastCardJpg;                            // artwork from the most recent session card

        /// One house style for every picture Atlas makes: a realistic 3D render, never flat vector art, and no text (we draw any numbers).
        public static string RealisticPrompt(string subject) =>
            $"Photorealistic cinematic 3D render, {subject}. Premium modern gym, dramatic teal and cool white rim lighting, shallow depth of field, " +
            "ultra detailed, realistic materials and skin, soft volumetric light. Subject centred, the lower third calm and uncluttered. " +
            "Absolutely no text, no letters, no numbers and no logos anywhere in the image.";

        static string PostFail(string why) => new JObject { ["ok"] = false, ["error"] = why }.ToString(Newtonsoft.Json.Formatting.None);

        // ── Drafting ─────────────────────────────────────────────────────────────

        IEnumerator DraftPostRoutine(JObject args, Action<string> done)
        {
            if (supabase == null || !supabase.SignedIn) { done(PostFail("Sign in before posting.")); yield break; }
            if (inSet) { done(PostFail("A set is in progress. Finish it before sharing.")); yield break; }
            if (posting) { done(PostFail("A post is being sent right now.")); yield break; }

            string caption = ((string)args["caption"] ?? "").Trim();
            if (caption.Length < 3) { done(PostFail("The caption was empty.")); yield break; }
            if (caption.Length > 400) caption = caption.Substring(0, 400).TrimEnd();

            var tags = new System.Collections.Generic.List<string>();
            if (args["hashtags"] is JArray raw)
                foreach (var t in raw)
                {
                    string tag = Regex.Replace(((string)t ?? "").ToLowerInvariant(), "[^a-z0-9_]", "");
                    if (tag.Length > 0 && !tags.Contains(tag) && tags.Count < 3) tags.Add(tag);
                }
            if (!tags.Contains("abetteru")) tags.Add("abetteru");

            byte[] jpg = null;
            string imagePrompt = ((string)args["image_prompt"] ?? "").Trim();
            if (imagePrompt.Length > 0)
            {
                if (imagePrompt.Length > 220) imagePrompt = imagePrompt.Substring(0, 220);
                Texture2D art = null; string err = null;
                yield return museImage.Generate(RealisticPrompt(imagePrompt), 768, t => art = t, e => err = e);
                if (art != null) { jpg = art.EncodeToJPG(88); Destroy(art); }
                else Debug.LogWarning("[Post] Image failed, posting text only: " + err);
            }
            else if (lastCardJpg != null) jpg = lastCardJpg;

            DiscardDraft();
            draft = new PostDraft { Caption = caption, Tags = tags.ToArray(), Jpg = jpg };
            if (jpg != null) { draft.Preview = new Texture2D(2, 2, TextureFormat.RGBA32, false); draft.Preview.LoadImage(jpg); }
            draftStage = flow.Current;
            postStatus = "";
            ShowPostPanel();

            done(new JObject
            {
                ["ok"] = true, ["status"] = "draft_shown_not_posted", ["has_image"] = jpg != null, ["visibility"] = draft.Visibility,
                ["message"] = "The preview is on screen. Nothing is posted yet: the user taps Post, or asks you to post it.",
            }.ToString(Newtonsoft.Json.Formatting.None));
        }

        // The caption for the Share button is built here from the real numbers, so tapping it needs no model call and is instant.
        IEnumerator DraftFromCard()
        {
            if (workout == null || workout.TotalReps <= 0) yield break;
            int sets = workout.Results.Count;
            string caption = $"Just finished {workout.TotalReps} bodyweight squat{(workout.TotalReps == 1 ? "" : "s")} across {sets} set{(sets == 1 ? "" : "s")} in the A Better U VR gym. Tracked live on my Quest.";
            var args = new JObject { ["caption"] = caption, ["hashtags"] = new JArray("squats", "vr") };
            yield return DraftPostRoutine(args, _ => { });
        }

        // After the picture appears Atlas offers to share it, then listens for "share my session" without needing the wake name.
        IEnumerator OfferShare(SquatHud forHud)
        {
            float waited = 0f;
            while ((busy || (tts != null && tts.IsSpeaking)) && waited < 20f) { waited += Time.unscaledDeltaTime; yield return null; }
            if (forHud == null || draft != null || posting || tts == null || !tts.Ready) yield break;
            bool spoken = false;
            tts.Speak($"Your session card is ready. Want me to post it to your A Better U feed? Just say, share my session, or tap Share.",
                () => spoken = true, (s, secs) => ShowCaption(AssistantState.Speaking, WakeName, s, secs));
            while (!spoken) yield return null;
            Report(AssistantState.Ready, ReadyHeadline, ReadyDetail);
            listenAfter = Time.unscaledTime + 0.7f;
            followUntil = Time.unscaledTime + FollowUpSeconds;
        }

        // ── Publishing ───────────────────────────────────────────────────────────

        IEnumerator PublishRoutine(string visibility, Action<string> done)
        {
            if (draft == null) { done(PostFail("There is no draft to post.")); yield break; }
            if (posting) { done(PostFail("The post is already being sent.")); yield break; }
            if (visibility == "friends" || visibility == "public") draft.Visibility = visibility;

            posting = true; postStatus = "Posting..."; ShowPostPanel();
            string imageUrl = null, error = null;
            if (draft.Jpg != null)
            {
                string path = $"{supabase.UserId}/{Guid.NewGuid():N}.jpg";
                yield return supabase.UploadPublic(PostBucket, path, draft.Jpg, "image/jpeg", u => imageUrl = u, e => error = e);
                if (error != null)
                {
                    Debug.LogWarning("[Post] Upload failed: " + error);
                    posting = false; postStatus = "The image did not upload. Tap Post to try again."; ShowPostPanel();
                    done(PostFail("The image upload failed, so nothing was posted."));
                    yield break;
                }
            }

            var body = new JObject
            {
                ["p_body"] = draft.Caption,
                ["p_media_urls"] = imageUrl != null ? new JArray(imageUrl) : new JArray(),
                ["p_visibility"] = draft.Visibility,
                ["p_hashtags"] = new JArray(draft.Tags),
                ["p_posted_as"] = "self",
            }.ToString(Newtonsoft.Json.Formatting.None);

            string reply = null;
            yield return supabase.Rpc("create_post", body, r => reply = r, e => error = e);
            string postId = null;
            if (error == null)
            {
                try { var j = JObject.Parse(reply); if ((bool?)j["ok"] == true) postId = (string)j["post_id"]; else error = (string)j["error"] ?? "The platform refused the post."; }
                catch (Exception) { error = "The platform's reply was not understood."; }
            }
            if (postId == null)
            {
                Debug.LogWarning("[Post] create_post failed: " + error);
                posting = false; postStatus = "Couldn't post. Tap Post to try again."; ShowPostPanel();
                done(PostFail("The post was not created, so nothing was posted."));
                yield break;
            }

            // Read it back as the author: "posted" is only claimed once the platform can show the post.
            string check = null, checkError = null;
            yield return supabase.Get($"post?id=eq.{postId}&select=id,author_id,visibility", r => check = r, e => checkError = e);
            bool confirmed = false;
            if (checkError == null)
                try { var rows = JArray.Parse(check); confirmed = rows.Count == 1 && (string)rows[0]["author_id"] == supabase.UserId; } catch (Exception) { }
            if (!confirmed)
            {
                posting = false; postStatus = "Couldn't confirm the post. Check your A Better U feed."; ShowPostPanel();
                done(PostFail("The post could not be confirmed, so do not say it was posted."));
                yield break;
            }

            string vis = draft.Visibility;
            posting = false;
            postStatus = vis == "public" ? "Posted to your A Better U feed. Everyone on A Better U can see it." : "Posted to your A Better U feed. Only your A Better U friends can see it.";
            var shown = draft; draft = null;
            ShowPostPanel(shown);
            StartCoroutine(ClosePostPanelAfter(2.8f, shown));
            done(new JObject { ["ok"] = true, ["post_id"] = postId, ["visibility"] = vis, ["with_image"] = imageUrl != null }.ToString(Newtonsoft.Json.Formatting.None));
        }

        IEnumerator PublishFromButton()
        {
            string result = null;
            yield return PublishRoutine(null, r => result = r);
            var j = JObject.Parse(result);
            if ((bool?)j["ok"] == true)
            {
                string text = $"Posted to your A Better U feed ({((string)j["visibility"] == "public" ? "public" : "friends only")})";
                OnSaved?.Invoke(text);
                Report(AssistantState.Done, "Saved", text);
                readyAt = Time.unscaledTime + 8f;
            }
        }

        // ── Preview panel ────────────────────────────────────────────────────────

        void PostTick()
        {
            if (draft != null && flow != null && flow.Current != draftStage) DiscardDraft();     // moved to another screen: the draft goes
        }

        void DiscardDraft()
        {
            if (draft != null && draft.Preview != null) Destroy(draft.Preview);
            draft = null;
            ClosePostPanel();
        }

        IEnumerator ClosePostPanelAfter(float seconds, PostDraft shown)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (draft == null && !posting) ClosePostPanel();
            if (shown.Preview != null) Destroy(shown.Preview);
        }

        void ClosePostPanel()
        {
            if (postPanel != null) { Destroy(postPanel.gameObject); postPanel = null; }
            if (flow != null && flow.Current == AppFlow.Stage.Gym) flow.SetWorkoutPointer(WorkoutControlsVisible);
        }

        void ShowPostPanel(PostDraft shown = null)
        {
            var d = shown ?? draft;
            if (d == null) return;
            Vector3 pos; Quaternion rot;
            if (postPanel != null) { pos = postPanel.position; rot = postPanel.rotation; Destroy(postPanel.gameObject); }
            else
            {
                var head = Camera.main != null ? Camera.main.transform : transform;
                Vector3 forward = head.forward; forward.y = 0f;
                forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
                pos = new Vector3(head.position.x, Mathf.Max(head.position.y - 0.05f, 1.2f), head.position.z) + forward * 1.25f;
                rot = Quaternion.LookRotation(forward, Vector3.up);
            }
            postPanel = new GameObject("Post preview").transform;
            postPanel.SetPositionAndRotation(pos, rot);
            postPanel.localScale = Vector3.one * 0.8f;
            if (flow != null && flow.Current == AppFlow.Stage.Gym) flow.SetWorkoutPointer(true);

            var p = postPanel;
            VRGlass.Panel(p, "Panel", new Vector3(0, 0, 0.03f), new Vector2(2.0f, 1.3f), 0.06f, GlassStyle.Panel, 30);
            VRGlass.Label(p, "Post to your A Better U feed", new Vector3(-0.92f, 0.55f, -0.02f), 0.015f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left, 36);

            if (d.Preview != null) VRGlass.Image(p, "Picture", d.Preview, new Vector3(-0.50f, -0.02f, -0.01f), new Vector2(0.78f, 0.78f), 34);
            else VRGlass.Label(p, "Text post\n(no picture)", new Vector3(-0.50f, -0.02f, -0.02f), 0.009f, VRUi.Muted, TextAnchor.MiddleCenter, TextAlignment.Center, 36);

            VRGlass.Label(p, VRUi.Wrap(d.Caption, 30, 7), new Vector3(-0.04f, 0.43f, -0.02f), 0.0092f, VRUi.Ink, TextAnchor.UpperLeft, TextAlignment.Left, 36);
            VRGlass.Label(p, string.Join(" ", d.Tags.Select(t => "#" + t)), new Vector3(-0.04f, -0.07f, -0.02f), 0.0078f, new Color(0.45f, 0.92f, 0.86f), TextAnchor.MiddleLeft, TextAlignment.Left, 36);

            bool locked = posting || shown != null;
            VRGlass.Button(p, "Friends", new Vector3(0.16f, -0.20f, -0.02f), new Vector2(0.40f, 0.13f),
                locked ? GlassStyle.Disabled : d.Visibility == "friends" ? GlassStyle.Primary : GlassStyle.Secondary, 0.0085f,
                () => { if (!posting && draft != null) { draft.Visibility = "friends"; ShowPostPanel(); } }, 0.035f, 34);
            VRGlass.Button(p, "Public", new Vector3(0.60f, -0.20f, -0.02f), new Vector2(0.40f, 0.13f),
                locked ? GlassStyle.Disabled : d.Visibility == "public" ? GlassStyle.Primary : GlassStyle.Secondary, 0.0085f,
                () => { if (!posting && draft != null) { draft.Visibility = "public"; ShowPostPanel(); } }, 0.035f, 34);
            VRGlass.Label(p, d.Visibility == "public" ? "A Better U social feed: everyone on A Better U can see it" : "A Better U social feed: only your A Better U friends can see it",
                new Vector3(0.38f, -0.31f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleCenter, TextAlignment.Center, 36);
            VRGlass.Label(p, postStatus, new Vector3(0.0f, -0.43f, -0.02f), 0.0085f, VRUi.Ink, TextAnchor.MiddleCenter, TextAlignment.Center, 36);

            VRGlass.Button(p, "Post", new Vector3(0.16f, -0.55f, -0.02f), new Vector2(0.40f, 0.15f),
                locked ? GlassStyle.Disabled : GlassStyle.Primary, 0.0095f,
                () => { if (!posting && draft != null) StartCoroutine(PublishFromButton()); }, 0.04f, 34);
            VRGlass.Button(p, "Cancel", new Vector3(0.60f, -0.55f, -0.02f), new Vector2(0.40f, 0.15f),
                locked ? GlassStyle.Disabled : GlassStyle.Secondary, 0.0095f,
                () => { if (!posting) DiscardDraft(); }, 0.04f, 34);
        }
    }
}
