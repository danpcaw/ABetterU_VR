using UnityEngine;

namespace ABetterU.Coach
{
    /// The squat set display: one glass panel fixed in the room in front of the user, showing a large rep count, a depth bar
    /// with the real "counts as down" line on it, and a one-line coaching prompt. After the set it becomes the summary card.
    /// It only displays what RepCounter measured; "Saved" appears only when the caller says the backend confirmed it.
    public class SquatHud : MonoBehaviour
    {
        const float BarW = 1.2f, BarH = 0.07f;
        const int PromptWidth = 22;             // characters per line that fit the panel at the prompt's size
        const float RangeBeyondLine = 1.5f;     // the bar's full width is 1.5x the depth that counts, so the line sits at two thirds

        static readonly Color Cyan = new Color(0.25f, 0.85f, 0.98f);
        static readonly Color Green = new Color(0.38f, 0.95f, 0.48f);
        static readonly Color Amber = new Color(1.00f, 0.76f, 0.26f);
        static readonly Color Red = new Color(0.95f, 0.38f, 0.40f);

        TextMesh header, repNumber, repTarget, prompt, footer, lineTag;
        Transform fill, marker, track;
        Renderer fillRenderer;
        int shownReps = -1;
        float punchUntil;

        public static SquatHud Create(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("SquatHud");
            go.transform.SetPositionAndRotation(position, rotation);
            var hud = go.AddComponent<SquatHud>();
            hud.Build(go.transform);
            return hud;
        }

        void Build(Transform root)
        {
            VRGlass.Panel(root, "Panel", new Vector3(0, 0, 0.03f), new Vector2(1.6f, 1.1f), 0.07f, GlassStyle.Panel, 0);

            header = VRGlass.Label(root, "SQUAT SET", new Vector3(0, 0.47f, -0.02f), 0.0095f, VRUi.Muted);

            repNumber = VRGlass.Label(root, "0", new Vector3(0.06f, 0.20f, -0.02f), 0.075f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
            repTarget = VRGlass.Label(root, "", new Vector3(0.11f, 0.17f, -0.02f), 0.026f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            prompt = VRGlass.Label(root, "", new Vector3(0, -0.10f, -0.02f), 0.0135f, VRUi.Ink);

            track = VRGlass.Solid(root, "Depth track", new Vector3(0, -0.27f, -0.01f), new Vector2(BarW, BarH), BarH / 2f,
                new Color(0.13f, 0.17f, 0.24f, 1f), new Color(0.10f, 0.14f, 0.20f, 1f), 2);

            // The fill is a white block tinted at runtime; its width is set from the live depth.
            fill = VRGlass.Solid(root, "Depth fill", new Vector3(-BarW / 2f, -0.27f, -0.02f), new Vector2(1f, BarH), BarH / 2f, Color.white, Color.white, 3);
            fillRenderer = fill.GetComponent<Renderer>();
            fill.localScale = new Vector3(0.001f, BarH, 1f);

            marker = VRGlass.Solid(root, "Depth line", new Vector3(0, -0.27f, -0.03f), new Vector2(0.012f, BarH * 2.1f), 0.004f,
                new Color(1f, 0.92f, 0.55f, 1f), new Color(1f, 0.92f, 0.55f, 1f), 5);
            lineTag = VRGlass.Label(root, "COUNTS", new Vector3(0, -0.35f, -0.02f), 0.0072f, VRUi.Muted);

            footer = VRGlass.Label(root, "Left trigger when you are done", new Vector3(0, -0.46f, -0.02f), 0.0085f, VRUi.Muted);
        }

        /// Called several times a second during a set.
        public void SetLive(int reps, int target, float drop, float downFraction, bool inBottom, string formCue = null)
        {
            header.text = "SQUAT SET";
            repNumber.text = reps.ToString();
            repTarget.text = target > 0 ? "/ " + target : "reps";
            if (reps != shownReps)
            {
                if (shownReps >= 0) punchUntil = Time.unscaledTime + 0.25f;     // a quick pop when a rep lands
                shownReps = reps;
            }

            // A form cue (leaning, looking down) takes over the line while it applies; otherwise the phase cue.
            prompt.text = VRUi.Wrap(formCue ?? PromptFor(drop, downFraction, inBottom, reps, target), PromptWidth, 3);
            prompt.color = formCue != null ? Amber : inBottom ? Green : VRUi.Ink;

            SetBarVisible(true);
            float range = downFraction * RangeBeyondLine;
            float f = Mathf.Clamp01(Mathf.Max(0f, drop) / range);
            float w = Mathf.Max(0.001f, BarW * f);
            fill.localScale = new Vector3(w, BarH, 1f);
            fill.localPosition = new Vector3(-BarW / 2f + w / 2f, -0.27f, -0.02f);
            fillRenderer.sharedMaterial.color = drop >= downFraction ? Green : Cyan;
            marker.localPosition = new Vector3(-BarW / 2f + BarW / RangeBeyondLine, -0.27f, -0.03f);
            lineTag.transform.localPosition = new Vector3(marker.localPosition.x, -0.35f, -0.02f);

            footer.transform.localPosition = new Vector3(0, -0.46f, -0.02f);
            footer.text = "Left trigger when you are done";
        }

        /// The card shown after the set. savedState: null while saving, true once the backend confirmed, false if it did not.
        public void SetSummary(int reps, float secondsPerRep, float deepestFraction, int flaggedReps, string status, bool? savedState)
        {
            header.text = "SET COMPLETE";
            repNumber.text = reps.ToString();
            repTarget.text = reps == 1 ? "rep" : "reps";
            SetBarVisible(false);

            prompt.text = VRUi.Wrap(status, PromptWidth, 2);
            prompt.color = savedState == true ? Green : savedState == false ? Red : Amber;

            string detail = "";
            if (reps > 0)
            {
                detail = $"{secondsPerRep:0.0} s per rep  -  deepest {deepestFraction * 100f:0}%\n" +
                         (flaggedReps > 0 ? $"{flaggedReps} leaning movement{(flaggedReps == 1 ? "" : "s")} not counted\n" : "");
            }
            footer.transform.localPosition = new Vector3(0, -0.40f, -0.02f);
            footer.text = detail + "Left trigger for another set";
        }

        // ── Session card: art made by Muse Image beside the summary. The numbers on it are drawn by us from the headset's
        // real count; the model is asked for artwork only, because it cannot be trusted to render exact text.
        Transform cardRoot;
        TextMesh cardStatus;
        Texture2D cardTexture;

        public void ShowCardPending()
        {
            HideCard();
            cardRoot = new GameObject("SessionCard").transform;
            cardRoot.SetParent(transform, false);
            cardRoot.localPosition = new Vector3(1.28f, 0f, 0f);
            VRGlass.Panel(cardRoot, "CardPanel", new Vector3(0, 0, 0.03f), new Vector2(0.95f, 1.1f), 0.07f, GlassStyle.Panel, 0);
            cardStatus = VRGlass.Label(cardRoot, "Creating your\nsession card\nwith Muse Image...", new Vector3(0, 0.02f, -0.02f), 0.0125f, VRUi.Muted);
        }

        public void ShowCard(Texture2D art, int reps, string title)
        {
            if (cardRoot == null) return;
            cardTexture = art;
            if (cardStatus != null) cardStatus.gameObject.SetActive(false);
            VRGlass.Image(cardRoot, "Art", art, new Vector3(0, 0.06f, -0.01f), new Vector2(0.84f, 0.84f), 1);
            VRGlass.Label(cardRoot, reps.ToString(), new Vector3(0, -0.14f, -0.03f), 0.05f, new Color(0.30f, 0.96f, 0.86f), order: 8);
            VRGlass.Label(cardRoot, title, new Vector3(0, -0.285f, -0.03f), 0.0115f, VRUi.Ink, order: 8);
            VRGlass.Label(cardRoot, "Made with Muse Image", new Vector3(0, -0.49f, -0.02f), 0.0075f, VRUi.Muted);
        }

        public void HideCard()
        {
            if (cardRoot != null) Destroy(cardRoot.gameObject);
            if (cardTexture != null) Destroy(cardTexture);
            cardRoot = null; cardStatus = null; cardTexture = null;
        }

        void SetBarVisible(bool on)
        {
            track.gameObject.SetActive(on);
            fill.gameObject.SetActive(on);
            marker.gameObject.SetActive(on);
            lineTag.gameObject.SetActive(on);
        }

        void Update()
        {
            float pop = Time.unscaledTime < punchUntil ? 1f + 0.18f * Mathf.Sin((punchUntil - Time.unscaledTime) / 0.25f * Mathf.PI) : 1f;
            repNumber.transform.localScale = Vector3.one * pop;
        }

        /// The one-line coaching cue under the rep count. Pure display: it never changes how reps are counted.
        public static string PromptFor(float drop, float downFraction, bool inBottom, int reps, int target)
        {
            if (target > 0 && reps >= target) return "Target reached! Left trigger to finish";
            if (inBottom) return "Now stand tall";
            if (drop < 0.03f) return reps == 0 ? "Squat down to start" : "Lower slowly";
            if (drop < downFraction) return "A little deeper";
            return "Now stand tall";
        }
    }
}
