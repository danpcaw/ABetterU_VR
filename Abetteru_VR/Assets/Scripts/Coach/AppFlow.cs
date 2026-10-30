using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ABetterU.Coach
{
    /// The app's front door and its screens: login, Home (four destinations), My World (live data), My Experiences
    /// (the way into the virtual gym), all inside the sunset retreat, with one persistent voice assistant bar.
    /// Also owns the in-gym menu (left Menu button). VoiceLoopTest builds the gym when this enters Stage.Gym.
    public class AppFlow : MonoBehaviour
    {
        public enum Stage { Login, Home, MyWorld, Diary, Experiences, Gym, Exercise, Profile, Scan }
        public Stage Current { get; private set; } = Stage.Login;

        /// True while the in-gym menu is open, so the right trigger clicks menu buttons instead of starting the mic.
        public bool MenuOpen { get; private set; }

        /// The signed-in session lives in a static so it survives a scene reload ("Back to hub" does not ask you to log in again).
        public static SupabaseClient Session;

        static readonly Color Navy = new Color(0.04f, 0.06f, 0.10f);
        static readonly Color Teal = new Color(0.45f, 0.92f, 0.86f);
        static readonly Color Amber = new Color(1.0f, 0.76f, 0.30f);
        static readonly Color NutritionRing = new Color(0.30f, 0.95f, 0.80f);
        static readonly Color ProteinTint = new Color(0.40f, 0.95f, 0.55f);
        static readonly Color CarbsTint = new Color(1.0f, 0.65f, 0.30f);
        static readonly Color FatTint = new Color(1.0f, 0.45f, 0.45f);

        SupabaseClient supabase;
        StatusBoard board;
        VoiceLoopTest voice;
        VRPointer pointer;
        RetreatEnvironment retreat;
        AssistantBar assistant;
        readonly DashboardData data = new DashboardData();
        readonly DiaryData diary = new DiaryData();
        readonly ExerciseData exercise = new ExerciseData();
        readonly ProfileData profile = new ProfileData();
        DateTime exerciseDate = DateTime.Now.Date;
        int exercisePage;
        DateTime diaryDate = DateTime.Now.Date;
        bool refreshing, refreshAgain;

        Transform screen;                // the current screen; replaced as the stage changes
        Vector3 screenBasePos;           // where the current screen was placed before any zoom
        float screenZoom;                // metres the right thumbstick has pulled the screen towards you (negative = pushed back); kept between screens
        Transform keyboardRoot;          // the on-screen keyboard (login only)
        Transform quitDialog;
        Transform menuRoot;              // the in-gym menu
        TextMesh statusLabel;
        VRButton emailField, passwordField, showButton, shiftButton;
        readonly List<KeyValuePair<VRButton, char>> letterKeys = new List<KeyValuePair<VRButton, char>>();

        string email = "", password = "";
        bool editingPassword, showPassword, shift, busy;

        /// Connects the voice loop so the assistant bar can mirror it and saved actions can refresh the dashboards.
        public void Bind(VoiceLoopTest v)
        {
            voice = v;
            v.OnAssistant += (state, headline, detail) =>
            {
                if (assistant == null) return;
                assistant.Set(state, headline, detail);
                if (state == AssistantState.Listening) { hintsDismissed = true; assistant.HideHints(); }   // they found the voice control
            };
            v.OnSaved += _ => StartCoroutine(RefreshData());
            v.OnChoices += ShowPicker;
        }

        public void Init(SupabaseClient supabase, StatusBoard board)
        {
            this.supabase = supabase;
            this.board = board;

            // Height must be real before anything is placed: the rig defaults to EyeLevel (found on-device Oct 27).
            if (OVRManager.instance != null && OVRManager.instance.trackingOriginType != OVRManager.TrackingOrigin.FloorLevel)
                OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;

            GymRoom.AcquireTemplate();         // must run while the scene's original cube is still active
            GymRoom.HideSceneTestObjects();

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Navy;
            }
            board.SetVisible(false);

            retreat = gameObject.AddComponent<RetreatEnvironment>();
            retreat.Build();

            pointer = gameObject.AddComponent<VRPointer>();
            pointer.Init(cam != null ? cam.transform : transform);

            if (supabase.SignedIn) { hintsDismissed = false; ShowHome(); } else ShowLogin();    // already signed in: we came back from the gym menu
        }

        // ── Food picker: the agent offers foods; the user picks an amount and taps one, which logs it (no model call). ──
        // Pages of 10 (two columns), so a whole restaurant menu can be browsed. Closed by any stage change.
        const int PickerPageSize = 10;
        Transform picker;
        List<CoachAgent.Choice> pickerAll;
        int pickerPage;
        double pickerServings = 1.0, pickerGrams = 0.0;     // grams > 0 when the user said a weight

        void ClosePicker()
        {
            if (picker != null) Destroy(picker.gameObject);
            picker = null;
            pickerAll = null;
        }

        void ShowPicker(List<CoachAgent.Choice> choices, double servings, double grams)
        {
            ClosePicker();
            if (choices == null || choices.Count == 0 || Current == Stage.Gym || Current == Stage.Login) return;
            pickerAll = choices;
            pickerPage = 0;
            pickerServings = servings > 0 ? servings : 1.0;
            pickerGrams = grams > 0 ? grams : 0.0;
            RenderPicker();
        }

        void RenderPicker()
        {
            if (picker != null) Destroy(picker.gameObject);
            if (pickerAll == null) return;

            int pages = (pickerAll.Count + PickerPageSize - 1) / PickerPageSize;
            pickerPage = Mathf.Clamp(pickerPage, 0, pages - 1);
            var page = pickerAll.GetRange(pickerPage * PickerPageSize, Mathf.Min(PickerPageSize, pickerAll.Count - pickerPage * PickerPageSize));

            // Up to 5 options sit in one column; 6 to 10 go in two columns so the panel stays a comfortable height.
            bool two = page.Count > 5;
            int rows = two ? (page.Count + 1) / 2 : page.Count;
            const float rowH = 0.23f;
            float w = two ? 3.5f : 2.3f, btnW = two ? 1.62f : 2.05f;
            float h = 0.50f + rows * rowH + 0.32f;
            int maxChars = two ? 30 : 44;

            picker = new GameObject("FoodPicker").transform;
            picker.position = new Vector3(0f, 1.4f, 1.95f);

            VRGlass.Panel(picker, "Panel", new Vector3(0, 0, 0.03f), new Vector2(w, h), 0.06f, GlassStyle.Panel, 20);
            VRGlass.Label(picker, "Which one did you mean?", new Vector3(0, h / 2 - 0.17f, -0.02f), 0.0125f, VRUi.Ink, order: 24);

            for (int i = 0; i < page.Count; i++)
            {
                var c = page[i];
                string name = c.Name ?? "Food";
                string line1 = !string.IsNullOrEmpty(c.Brand) ? $"{name} - {c.Brand}" : c.Source == "cofid" ? $"{name} - generic" : name;
                if (line1.Length > maxChars - 6) line1 = line1.Substring(0, maxChars - 9) + "...";      // room is taken by the picture on the left
                string line2 = (c.Kcal.HasValue ? $"{c.Kcal.Value:0} kcal" : "calories not listed") + (string.IsNullOrEmpty(c.Serving) ? "" : $" per {c.Serving}");
                string foodId = c.FoodId;

                int col = two ? i % 2 : 0, row = two ? i / 2 : i;
                float x = two ? (col == 0 ? -0.84f : 0.84f) : 0f;
                float y = h / 2 - 0.46f - row * rowH;
                VRGlass.Button(picker, "", new Vector3(x, y, -0.02f), new Vector2(btnW, 0.2f),
                    GlassStyle.Secondary, two ? 0.0075f : 0.0085f, () => { double amount = pickerServings, weight = pickerGrams; ClosePicker(); voice.ChooseFood(foodId, amount, weight); }, 0.04f, 22);
                // A picture in the same rounded square as the diary, then the name and calories beside it.
                float left = x - btnW / 2f;
                FoodThumbnail.Create(picker, new Vector3(left + 0.12f, y, -0.045f), c.ImageUrl, 0.17f, VRArt.IconKind.Apple, 20);
                VRGlass.Label(picker, line1, new Vector3(left + 0.25f, y + 0.04f, -0.04f), two ? 0.0078f : 0.0088f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left, 27);
                VRGlass.Label(picker, line2, new Vector3(left + 0.25f, y - 0.045f, -0.04f), two ? 0.0066f : 0.0074f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left, 27);
            }

            // Bottom row: how many (left), pages (middle), cancel (right).
            float by = -h / 2 + 0.16f;
            float lx = two ? -1.25f : -0.8f, rx = two ? 1.25f : 0.8f;
            VRGlass.Button(picker, "-", new Vector3(lx - 0.36f, by, -0.02f), new Vector2(0.18f, 0.16f), GlassStyle.Secondary, 0.012f, () => StepAmount(-1), 0.04f, 22);
            var amountLabel = VRGlass.Label(picker, ServingsText(), new Vector3(lx, by, -0.02f), 0.0085f, VRUi.Ink, order: 24);
            VRGlass.Button(picker, "+", new Vector3(lx + 0.36f, by, -0.02f), new Vector2(0.18f, 0.16f), GlassStyle.Secondary, 0.012f, () => StepAmount(1), 0.04f, 22);
            pickerAmountLabel = amountLabel;

            if (pages > 1)
            {
                VRGlass.Button(picker, "Prev", new Vector3(-0.42f, by, -0.02f), new Vector2(0.4f, 0.16f), pickerPage > 0 ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f,
                    pickerPage > 0 ? (System.Action)(() => { pickerPage--; RenderPicker(); }) : null, 0.04f, 22);
                VRGlass.Label(picker, $"Page {pickerPage + 1} of {pages}", new Vector3(0f, by, -0.02f), 0.0082f, VRUi.Muted, order: 24);
                VRGlass.Button(picker, "Next", new Vector3(0.42f, by, -0.02f), new Vector2(0.4f, 0.16f), pickerPage < pages - 1 ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f,
                    pickerPage < pages - 1 ? (System.Action)(() => { pickerPage++; RenderPicker(); }) : null, 0.04f, 22);
            }
            VRGlass.Button(picker, "None of these", new Vector3(rx + (two ? 0.1f : 0.0f), by, -0.02f), new Vector2(two ? 0.9f : 0.8f, 0.16f),
                GlassStyle.Secondary, 0.0078f, ClosePicker, 0.04f, 22);
        }

        TextMesh pickerAmountLabel;

        // In grams mode a step is 25 g; otherwise half a serving. Foods listed per 100 g (UK CoFID) count 100 g as one serving.
        string ServingsText() => pickerGrams > 0
            ? $"Amount: {pickerGrams:0} g"
            : $"Amount: {pickerServings:0.#} serving{(pickerServings == 1.0 ? "" : "s")}";

        void StepAmount(int direction)
        {
            if (pickerGrams > 0) pickerGrams = System.Math.Max(25.0, System.Math.Min(2000.0, pickerGrams + 25.0 * direction));
            else pickerServings = System.Math.Max(0.5, System.Math.Min(20.0, pickerServings + 0.5 * direction));
            if (pickerAmountLabel != null) pickerAmountLabel.text = ServingsText();
        }

        /// Opens a screen by name for the voice agent. Returns false if it cannot be opened from here, so the agent never claims it did.
        public bool NavigateTo(string destination)
        {
            if (Current == Stage.Login || Current == Stage.Gym) return false;
            switch (destination)
            {
                case "home": ShowHome(); return true;
                case "my_world": ShowMyWorld(); return true;
                case "food_diary": ShowDiary(); return true;
                case "exercise_log": ShowExercise(); return true;
                case "my_profile": ShowProfile(); return true;
                case "my_experiences": ShowExperiences(); return true;
                case "virtual_gym": EnterGym(); return true;
                default: return false;
            }
        }

        /// True while the pointer is resting on a button, so voice capture can ignore the trigger.
        public bool PointerOverButton => quitDialog != null || (pointer != null && pointer.HoveringButton);

        static readonly Vector3 DefaultQuitPos = new Vector3(1.5f, 1.15f, -0.02f);
        Vector3 nextQuitPos = DefaultQuitPos;

        Transform NewRoot(Vector3 position)
        {
            CloseQuitConfirmation();
            if (screen != null) Destroy(screen.gameObject);
            ClosePicker();
            CloseKeyboard();
            statusLabel = null; emailField = null; passwordField = null; showButton = null;

            var go = new GameObject("Screen");
            go.transform.position = position;
            screen = go.transform;
            screenBasePos = position;
            ApplyScreenZoom();
            VRGlass.Button(screen, "Quit App", nextQuitPos, new Vector2(0.55f, 0.15f),
                GlassStyle.Secondary, 0.009f, ShowQuitConfirmation, 0.04f);
            nextQuitPos = DefaultQuitPos;                 // a screen that wants it elsewhere sets this again before calling NewRoot
            return screen;
        }

#if UNITY_EDITOR
        /// Layout inspection only: renders a view without authenticating or fetching account data.
        /// Kept out of player builds; normal navigation still uses the signed-in flow above.
        public void PreviewScreenInEditor(Stage stage)
        {
            if (Current == Stage.Gym || MenuOpen)
            {
                Debug.LogWarning("[UI Preview] Return to the hub before previewing dashboard screens.");
                return;
            }
            if (stage != Stage.Login && stage != Stage.Home && stage != Stage.MyWorld && stage != Stage.Diary && stage != Stage.Experiences)
                return;

            Current = stage;
            switch (stage)
            {
                case Stage.Login: ShowLogin(); break;
                case Stage.Home: EnsureAssistant(); BuildHome(); break;
                case Stage.MyWorld: EnsureAssistant(); BuildMyWorld(); break;
                case Stage.Diary: EnsureAssistant(); BuildDiary(); break;
                case Stage.Experiences: EnsureAssistant(); BuildExperiences(); break;
            }
            Debug.Log("[UI Preview] " + stage + ": layout only; the selector does not sign in or fetch data.");
        }
#endif

        // ── Login: two columns, like the mockup ─────────────────────────────────────────────────────────

        void ShowLogin()
        {
            Current = Stage.Login;
            if (assistant != null) { Destroy(assistant.gameObject); assistant = null; }
            var p = NewRoot(new Vector3(0f, 1.55f, 2.6f));

            VRGlass.Panel(p, "Backdrop", new Vector3(0, 0, 0.04f), new Vector2(3.6f, 2.1f), 0.10f, GlassStyle.Panel, 0);

            // Left column: the real logo, the tagline, and two unavailable sign-in tiles.
            const float lx = -0.81f;                                   // centre of the free space left of the card (-1.8 to 0.175)
            var logo = Resources.Load<Texture2D>("Branding/logo");
            if (logo != null) VRGlass.Image(p, "Logo", logo, new Vector3(lx, 0.52f, -0.01f), new Vector2(1.8f, 0.6f));
            else VRGlass.Label(p, "A Better U", new Vector3(lx, 0.52f, -0.02f), 0.024f, VRUi.Ink);

            VRGlass.Label(p, "Mind   ·   Movement   ·   Progress", new Vector3(lx, 0.12f, -0.02f), 0.0105f, Teal);
            VRGlass.Label(p, "Your complete wellness platform\nin virtual and real life.", new Vector3(lx, -0.10f, -0.02f), 0.0085f, VRUi.Ink);

            VRGlass.Button(p, "Gym Login\nComing soon", new Vector3(lx, -0.62f, -0.02f), new Vector2(0.74f, 0.46f), GlassStyle.Disabled, 0.0085f, null, 0.05f);

            // Right column: the sign-in card.
            const float cx = 0.95f;
            VRGlass.Panel(p, "Card", new Vector3(cx, 0, 0.0f), new Vector2(1.55f, 1.85f), 0.07f, GlassStyle.Card, 1);
            VRGlass.Label(p, "Welcome to", new Vector3(cx, 0.76f, -0.02f), 0.0095f, VRUi.Muted);
            VRGlass.Label(p, "A Better U", new Vector3(cx, 0.64f, -0.02f), 0.021f, VRUi.Ink);
            VRGlass.Label(p, "Sign in to continue your journey.", new Vector3(cx, 0.52f, -0.02f), 0.0072f, VRUi.Muted);

            emailField = VRGlass.Button(p, "", new Vector3(cx, 0.34f, -0.02f), new Vector2(1.3f, 0.15f), GlassStyle.Secondary, 0.0085f,
                () => { editingPassword = false; RefreshFields(); ShowKeyboard(); });
            passwordField = VRGlass.Button(p, "", new Vector3(cx - 0.13f, 0.15f, -0.02f), new Vector2(1.04f, 0.15f), GlassStyle.Secondary, 0.0085f,
                () => { editingPassword = true; RefreshFields(); ShowKeyboard(); });
            showButton = VRGlass.Button(p, "Show", new Vector3(cx + 0.54f, 0.15f, -0.02f), new Vector2(0.24f, 0.15f), GlassStyle.Secondary, 0.0075f,
                () => { showPassword = !showPassword; RefreshFields(); });

            VRGlass.Button(p, "Sign In", new Vector3(cx, -0.08f, -0.02f), new Vector2(1.3f, 0.17f), GlassStyle.Primary, 0.011f,
                () => StartCoroutine(Login(judge: false)));

            VRGlass.Label(p, "or", new Vector3(cx, -0.24f, -0.02f), 0.0075f, VRUi.Muted);
            VRGlass.Button(p, "Continue with Meta Quest  (coming soon)", new Vector3(cx, -0.38f, -0.02f), new Vector2(1.3f, 0.14f), GlassStyle.Disabled, 0.0072f, null);
            VRGlass.Button(p, "Judge Instant Login", new Vector3(cx, -0.60f, -0.02f), new Vector2(1.3f, 0.18f), GlassStyle.Purple, 0.011f,
                () => StartCoroutine(Login(judge: true)));

            statusLabel = VRGlass.Label(p, "", new Vector3(cx, -0.78f, -0.02f), 0.0068f, VRUi.Muted);
            RefreshFields();
        }

        // ── On-screen keyboard: appears when you select a field ─────────────────────────────────────────

        void ShowKeyboard()
        {
            if (keyboardRoot != null) return;
            letterKeys.Clear();

            var go = new GameObject("Keyboard");
            go.transform.position = new Vector3(0f, 0.55f, 1.9f);
            go.transform.rotation = Quaternion.Euler(25f, 0f, 0f);   // leaned back so it is easy to reach and read
            keyboardRoot = go.transform;
            var k = keyboardRoot;

            VRGlass.Panel(k, "Keyboard panel", new Vector3(0, 0, 0.02f), new Vector2(1.8f, 0.92f), 0.05f, GlassStyle.Panel, 0);

            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl@", "zxcvbnm._-" };
            const float step = 0.16f, size = 0.14f, top = 0.30f;
            for (int r = 0; r < rows.Length; r++)
            {
                float y = top - r * step;
                float x0 = -(rows[r].Length - 1) * step / 2f;
                for (int i = 0; i < rows[r].Length; i++)
                {
                    char key = rows[r][i];                       // local copy so each button keeps its own character
                    var button = VRGlass.Button(k, key.ToString(), new Vector3(x0 + i * step, y, -0.02f), new Vector2(size, size),
                        GlassStyle.Secondary, 0.0095f, () => TypeChar(key), 0.03f, 1);
                    if (char.IsLetter(key)) letterKeys.Add(new KeyValuePair<VRButton, char>(button, key));
                }
            }

            float bottom = top - rows.Length * step;
            shiftButton = VRGlass.Button(k, "Shift", new Vector3(-0.60f, bottom, -0.02f), new Vector2(0.5f, size), GlassStyle.Secondary, 0.0085f,
                () => { shift = !shift; RefreshKeys(); }, 0.03f, 1);
            VRGlass.Button(k, "Delete", new Vector3(0.60f, bottom, -0.02f), new Vector2(0.5f, size), GlassStyle.Secondary, 0.0085f, Backspace, 0.03f, 1);
            VRGlass.Button(k, "Done", new Vector3(0f, bottom, -0.02f), new Vector2(0.6f, size), GlassStyle.Primary, 0.0095f, CloseKeyboard, 0.03f, 1);
            RefreshKeys();
        }

        void CloseKeyboard()
        {
            if (keyboardRoot != null) Destroy(keyboardRoot.gameObject);
            keyboardRoot = null;
            letterKeys.Clear();
            shiftButton = null;
        }

        void TypeChar(char c)
        {
            char ch = shift && char.IsLetter(c) ? char.ToUpper(c) : c;
            if (editingPassword) password += ch; else email += ch;
            if (shift) { shift = false; RefreshKeys(); }          // shift applies to one letter, like a phone keyboard
            RefreshFields();
        }

        void Backspace()
        {
            if (editingPassword) { if (password.Length > 0) password = password.Substring(0, password.Length - 1); }
            else if (email.Length > 0) email = email.Substring(0, email.Length - 1);
            RefreshFields();
        }

        void RefreshKeys()
        {
            foreach (var pair in letterKeys) pair.Key.SetLabel((shift ? char.ToUpper(pair.Value) : pair.Value).ToString());
            if (shiftButton != null) shiftButton.SetLabel(shift ? "SHIFT" : "Shift");
        }

        void RefreshFields()
        {
            if (emailField != null)
                emailField.SetLabel((editingPassword ? "  " : "> ") + (email.Length > 0 ? email : "Email"));
            if (passwordField != null)
            {
                string shown = password.Length == 0 ? "Password" : showPassword ? password : new string('*', password.Length);
                passwordField.SetLabel((editingPassword ? "> " : "  ") + shown);
            }
            if (showButton != null) showButton.SetLabel(showPassword ? "Hide" : "Show");
        }

        IEnumerator Login(bool judge)
        {
            if (busy) yield break;                                  // no duplicate submissions
            busy = true;
            CloseKeyboard();
            Say(judge ? "Signing in as the judge..." : "Signing in...");

            string error = null;
            if (judge) yield return supabase.SignInJudge(e => error = e);
            else yield return supabase.SignIn(email, password, e => error = e);
            busy = false;

            if (error != null)
            {
                Debug.LogError("[Login] " + error);
                Say("Sign-in failed. Check your details and try again.");
                yield break;
            }
            Debug.Log($"[Login] signed in as {supabase.DisplayName ?? "user"} ({(judge ? "judge" : "own account")})");
            password = "";                      // do not keep the typed password around
            isJudgeSession = judge;
            hintsDismissed = false;
            ShowHome();
        }

        void Say(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
        }

        // ── Data: loaded after sign-in and again after every confirmed save ─────────────────────────────

        IEnumerator RefreshData()
        {
            if (refreshing) { refreshAgain = true; yield break; }
            refreshing = true;
            do
            {
                refreshAgain = false;
                yield return data.Refresh(supabase, null);
                if (Current == Stage.Diary && !diary.Loading) yield return diary.Load(supabase, diaryDate);   // a save may have changed the day on screen
                if (Current == Stage.Exercise && !exercise.Loading) yield return exercise.Load(supabase, exerciseDate);
                if (Current == Stage.Profile && !profile.Loading) yield return profile.Load(supabase);
            }
            while (refreshAgain);
            refreshing = false;
            Rerender();
        }

        // Redraws the current screen with the freshest numbers (without starting another refresh).
        void Rerender()
        {
            switch (Current)
            {
                case Stage.Home: BuildHome(); break;
                case Stage.MyWorld: BuildMyWorld(); break;
                case Stage.Diary: BuildDiary(); break;
                case Stage.Exercise: BuildExercise(); break;
                case Stage.Profile: BuildProfile(); break;
                case Stage.Scan: BuildScan(); break;
                case Stage.Experiences: BuildExperiences(); break;
            }
        }

        string DisplayName => !string.IsNullOrEmpty(data.Name) ? data.Name : (supabase.DisplayName ?? "there");

        // ── Assistant bar: one persistent control under the dashboards ──────────────────────────────────

        void EnsureAssistant()
        {
            if (assistant == null)
                assistant = AssistantBar.Create(new Vector3(0f, 0.38f, 2.65f), Quaternion.identity);
            ApplyHints();
        }

        // Judges get no briefing, so the judge login shows a "Try saying" strip until they speak once.
        // These phrases are the ones used in headset tests; keep the list to things that really work.
        static bool isJudgeSession, hintsDismissed;

        void ApplyHints()
        {
            if (assistant == null) return;
            if (!isJudgeSession || hintsDismissed) { assistant.HideHints(); return; }

            switch (Current)
            {
                case Stage.Diary:
                    assistant.SetHints(new[] { "Log my usual breakfast", "Show me McDonald's foods", "Log a Big Mac for lunch", "What can you do?" });
                    break;
                case Stage.Experiences:
                    assistant.SetHints(new[] { "Set me up two sets of ten squats", "What can you do?" });
                    break;
                default:
                    assistant.SetHints(new[] { "What can you do?", "Log a Big Mac for dinner", "Set me up two sets of ten squats", "Log my usual breakfast" });
                    break;
            }
        }

        // ── Home: greeting, today's summary and four destinations ───────────────────────────────────────

        void ShowHome()
        {
            Current = Stage.Home;
            EnsureAssistant();
            BuildHome();
            if (voice != null) voice.Introduce();      // once per launch; Atlas says who she is and what she does not do
            StartCoroutine(RefreshData());
        }

        void BuildHome()
        {
            nextQuitPos = new Vector3(1.585f, 1.15f, -0.02f);       // right edge flush with Switch account below it
            var p = NewRoot(new Vector3(0f, 1.55f, 2.5f));

            var logo = Resources.Load<Texture2D>("Branding/logo");
            if (logo != null) VRGlass.Image(p, "Logo", logo, new Vector3(0f, 1.05f, -0.01f), new Vector2(1.35f, 0.45f));
            VRGlass.Label(p, Greeting() + ", " + DisplayName, new Vector3(0f, 0.72f, -0.02f), 0.0145f, VRUi.Ink);

            string summary = SummaryLine();
            VRGlass.Label(p, summary, new Vector3(0f, 0.58f, -0.02f), 0.0082f, VRUi.Muted);

            DestinationCard(p, -1.5f, "My World", "Your personal information and daily activity.", VRArt.IconKind.Person,
                GlassStyle.Secondary, Teal, ShowMyWorld, "Open");
            DestinationCard(p, -0.5f, "My Community", "Friends, social posts, groups and connections.", VRArt.IconKind.Group,
                GlassStyle.Disabled, VRUi.Muted, null, "Coming soon");
            DestinationCard(p, 0.5f, "My Experiences", "Interactive activities and virtual spaces.", VRArt.IconKind.Dumbbell,
                GlassStyle.Amber, Amber, ShowExperiences, "Open");
            DestinationCard(p, 1.5f, "Discover", "Explore people, services and events.", VRArt.IconKind.Compass,
                GlassStyle.Disabled, VRUi.Muted, null, "Coming soon");

            VRGlass.Button(p, "Switch account", new Vector3(1.55f, 0.95f, -0.02f), new Vector2(0.62f, 0.12f), GlassStyle.Secondary, 0.0072f, SwitchAccount, 0.04f);
        }

        // One destination card, placed on a gentle arc around the user and turned to face them.
        void DestinationCard(Transform p, float x, string title, string subtitle, VRArt.IconKind icon, GlassStyle style, Color accent, Action onClick, string tag)
        {
            const float radius = 2.5f;
            float a = Mathf.Asin(Mathf.Clamp(x / radius, -0.95f, 0.95f));
            var g = new GameObject("Card: " + title).transform;
            g.SetParent(p, false);
            g.localPosition = new Vector3(radius * Mathf.Sin(a), -0.05f, radius * (Mathf.Cos(a) - 1f));
            g.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);

            bool off = style == GlassStyle.Disabled;
            VRGlass.Button(g, "", Vector3.zero, new Vector2(0.92f, 1.0f), style, 0.01f, onClick, 0.06f);
            VRGlass.Icon(g, icon, off ? VRUi.Muted : accent, new Vector3(0f, 0.27f, -0.02f), 0.30f);
            VRGlass.Label(g, title, new Vector3(0f, 0.02f, -0.02f), 0.0125f, off ? VRUi.Muted : VRUi.Ink);
            VRGlass.Label(g, VRUi.Wrap(subtitle, 22, 3), new Vector3(0f, -0.20f, -0.02f), 0.0072f, VRUi.Muted);
            if (tag != null) VRGlass.Label(g, tag, new Vector3(0f, -0.42f, -0.02f), 0.0078f, off ? VRUi.Muted : accent);
        }

        string Greeting()
        {
            int h = DateTime.Now.Hour;
            return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
        }

        string SummaryLine()
        {
            if (!data.Loaded) return data.Loading || supabase.SignedIn ? "Loading your day..." : "";
            var parts = new List<string>
            {
                data.MealsToday > 0 ? $"{data.Kcal:N0} kcal today" : "No meals logged today",
                data.WorkoutsThisWeek > 0 ? $"{data.WorkoutsThisWeek} workout{(data.WorkoutsThisWeek == 1 ? "" : "s")} this week" : "No workouts this week",
            };
            string line = string.Join("   ·   ", parts);
            return data.HadError ? line + "\n(some details could not be loaded)" : line;
        }

        static string Describe(DashboardData.InjuryNote n) =>
            Capitalise((string.IsNullOrEmpty(n.Side) ? "" : n.Side + " ") + n.Area);

        static string Capitalise(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpper(s[0]) + s.Substring(1);

        // ── My World: profile, nutrition, activity and wellbeing notes, all from the real account ───────

        void ShowMyWorld()
        {
            Current = Stage.MyWorld;
            EnsureAssistant();
            BuildMyWorld();
            StartCoroutine(RefreshData());
        }

        void BuildMyWorld()
        {
            nextQuitPos = new Vector3(1.225f, 0.95f, -0.02f);      // on the date row, flush with the right edge of the cards
            var p = NewRoot(new Vector3(0f, 1.62f, 2.8f));

            // Navigation rail. It sits far to the user's left, so it is turned to face them; otherwise the buttons,
            // which float in front of the panel, look shifted sideways by perspective.
            var rail = new GameObject("Rail").transform;
            rail.SetParent(p, false);
            // Match the metric row's top (0.77) and main cards' bottom (-0.68).
            rail.localPosition = new Vector3(-1.85f, 0.045f, 0f);
            rail.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(1.85f, 2.8f) * Mathf.Rad2Deg, 0f);
            VRGlass.Panel(rail, "Rail panel", new Vector3(0f, 0f, 0.03f), new Vector2(0.78f, 1.45f), 0.06f, GlassStyle.Panel, 0);
            RailButton(rail, "Home", 0.55f, GlassStyle.Secondary, ShowHome);
            RailButton(rail, "My World", 0.38f, GlassStyle.Primary, null);
            RailButton(rail, "My Community", 0.21f, GlassStyle.Disabled, null);
            RailButton(rail, "My Experiences", 0.04f, GlassStyle.Secondary, ShowExperiences);
            RailButton(rail, "Discover", -0.13f, GlassStyle.Disabled, null);
            RailButton(rail, "Switch account", -0.59f, GlassStyle.Secondary, SwitchAccount);

            VRGlass.Label(p, "My World", new Vector3(-1.4f, 1.02f, -0.02f), 0.02f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(p, "Your personal information and daily activity.", new Vector3(-1.4f, 0.88f, -0.02f), 0.0085f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            // Date pill, top right (as in the reference design).
            var datePill = VRGlass.Panel(p, "Date pill", new Vector3(0.55f, 0.95f, 0f), new Vector2(0.72f, 0.15f), 0.07f, GlassStyle.Secondary, 1);
            VRGlass.Label(datePill.parent, DateTime.Now.ToString("ddd d MMM yyyy"), new Vector3(0.55f, 0.95f, -0.02f), 0.0085f, VRUi.Ink);

            // Row 1 spans the same -1.40 to +1.50 edges as the main cards.
            // A consistent 0.055 m gap separates the profile and four metric cards.
            BuildProfileCard(Card(p, new Vector3(-0.92f, 0.52f, 0f), new Vector2(0.96f, 0.50f)));
            BuildWeightCard(Card(p, new Vector3(-0.17f, 0.52f, 0f), new Vector2(0.43f, 0.50f)));
            BuildSleepCard(Card(p, new Vector3(0.315f, 0.52f, 0f), new Vector2(0.43f, 0.50f)));
            BuildMoodCard(Card(p, new Vector3(0.80f, 0.52f, 0f), new Vector2(0.43f, 0.50f)));
            BuildStreakCard(Card(p, new Vector3(1.285f, 0.52f, 0f), new Vector2(0.43f, 0.50f)));

            // Row 2: nutrition, activity, wellbeing notes
            BuildNutritionCard(Card(p, new Vector3(-0.875f, -0.22f, 0f), new Vector2(1.05f, 0.92f)));
            BuildActivityCard(Card(p, new Vector3(0.15f, -0.22f, 0f), new Vector2(0.9f, 0.92f)));
            BuildWellbeingCard(Card(p, new Vector3(1.075f, -0.22f, 0f), new Vector2(0.85f, 0.92f)));
        }

        static void StatTitle(Transform c, string title) =>
            VRGlass.Label(c, title, new Vector3(0f, 0.185f, -0.02f), 0.0078f, VRUi.Muted);

        void BuildProfileCard(Transform profile)
        {
            var avatarOrb = VRGlass.Image(profile, "Avatar", VRArt.Orb(new Color(0.20f, 0.75f, 0.80f), new Color(0.20f, 0.75f, 0.80f)), new Vector3(-0.31f, 0.05f, -0.01f), new Vector2(0.28f, 0.28f), 3);
            var avatarInitial = VRGlass.Label(profile, Initial(DisplayName), new Vector3(-0.31f, 0.05f, -0.02f), 0.013f, VRUi.Ink);
            // The orb and initial are only the stand-in: they go away once the real picture has loaded.
            AvatarImage.Create(profile, new Vector3(-0.31f, 0.05f, -0.01f), data.AvatarUrl, 0.28f, avatarOrb.gameObject, avatarInitial.gameObject);
            VRGlass.Label(profile, VRUi.Wrap(DisplayName, 18, 1), new Vector3(-0.13f, 0.13f, -0.02f), 0.0125f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            string sub = data.CalorieGoal.HasValue && data.CalorieGoal.Value > 0
                ? $"Goal {data.CalorieGoal.Value:N0} kcal a day"
                : "A Better U member";
            VRGlass.Label(profile, sub, new Vector3(-0.13f, 0.03f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Button(profile, "My Profile", new Vector3(0f, -0.155f, -0.02f), new Vector2(0.72f, 0.11f), GlassStyle.Secondary, 0.0072f, ShowProfile, 0.035f);
        }

        void BuildWeightCard(Transform c)
        {
            StatTitle(c, "WEIGHT");
            if (!data.Loaded) { VRGlass.Label(c, "...", new Vector3(0f, 0.02f, -0.02f), 0.014f, VRUi.Muted); return; }
            if (!data.WeightKg.HasValue) { VRGlass.Label(c, "Not logged", new Vector3(0f, 0.02f, -0.02f), 0.011f, VRUi.Ink); return; }

            VRGlass.Label(c, Wt(data.WeightKg.Value), new Vector3(0f, 0.10f, -0.02f), 0.0145f, VRUi.Ink);
            if (data.WeightDelta.HasValue && Math.Abs(data.WeightDelta.Value) >= 0.05)
            {
                bool down = data.WeightDelta.Value < 0;
                var tint = down ? new Color(0.35f, 0.95f, 0.70f) : new Color(1.0f, 0.75f, 0.40f);
                VRGlass.Label(c, $"{(down ? "v" : "^")} {Wt(Math.Abs(data.WeightDelta.Value))}", new Vector3(0f, -0.015f, -0.02f), 0.0085f, tint);
            }
            if (data.WeightHistory.Count >= 2)
                VRGlass.Sparkline(c, new Vector3(0f, -0.135f, -0.02f), new Vector2(0.34f, 0.105f), data.WeightHistory.ToArray(), new Color(0.30f, 0.85f, 0.95f));
        }

        void BuildSleepCard(Transform c)
        {
            StatTitle(c, "SLEEP");
            if (!data.Loaded) { VRGlass.Label(c, "...", new Vector3(0f, 0.02f, -0.02f), 0.014f, VRUi.Muted); return; }
            if (!data.SleepMinutes.HasValue || !data.SleepScore.HasValue)
            { VRGlass.Label(c, "No sleep\nlogged", new Vector3(0f, 0.02f, -0.02f), 0.0105f, VRUi.Ink); return; }

            int mins = (int)Math.Round(data.SleepMinutes.Value);
            VRGlass.Label(c, $"{mins / 60}h {mins % 60:00}m", new Vector3(0f, 0.10f, -0.02f), 0.0145f, VRUi.Ink);
            VRGlass.Ring(c, new Vector3(0f, -0.085f, -0.02f), 0.17f, data.SleepScore.Value / 100f, new Color(0.35f, 0.80f, 1.0f));
            VRGlass.Label(c, data.SleepScore.Value.ToString(), new Vector3(0f, -0.075f, -0.03f), 0.011f, VRUi.Ink);
            VRGlass.Label(c, data.SleepScore.Value >= 85 ? "Great" : data.SleepScore.Value >= 70 ? "Good" : data.SleepScore.Value >= 50 ? "Fair" : "Low",
                new Vector3(0f, -0.195f, -0.02f), 0.0072f, VRUi.Muted);
        }

        void BuildMoodCard(Transform c)
        {
            StatTitle(c, "MOOD");
            if (!data.Loaded) { VRGlass.Label(c, "...", new Vector3(0f, 0.02f, -0.02f), 0.014f, VRUi.Muted); return; }
            if (!data.MoodRating.HasValue)
            { VRGlass.Label(c, "No mood\nlogged", new Vector3(0f, 0.02f, -0.02f), 0.0105f, VRUi.Ink); return; }

            string word = !string.IsNullOrEmpty(data.MoodEmotion)
                ? char.ToUpper(data.MoodEmotion[0]) + data.MoodEmotion.Substring(1).Replace('_', ' ')
                : data.MoodRating.Value >= 4.5 ? "Great" : data.MoodRating.Value >= 3.5 ? "Good" : data.MoodRating.Value >= 2.5 ? "Okay" : data.MoodRating.Value >= 1.5 ? "Low" : "Rough";
            VRGlass.Label(c, VRUi.Wrap(word, 12, 1), new Vector3(0f, 0.10f, -0.02f), 0.0115f, VRUi.Ink);
            VRGlass.WeekBars(c, new Vector3(0f, -0.075f, -0.02f), new Vector2(0.34f, 0.105f), data.MoodWeek, new Color(0.35f, 0.90f, 0.75f));
            VRGlass.Label(c, "Last 7 days", new Vector3(0f, -0.195f, -0.02f), 0.0072f, VRUi.Muted);
        }

        void BuildStreakCard(Transform c)
        {
            StatTitle(c, "STREAK");
            if (!data.Loaded) { VRGlass.Label(c, "...", new Vector3(0f, 0.02f, -0.02f), 0.014f, VRUi.Muted); return; }

            VRGlass.Label(c, data.StreakDays.ToString(), new Vector3(-0.035f, 0.08f, -0.02f), 0.020f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
            VRGlass.Label(c, data.StreakDays == 1 ? "day" : "days", new Vector3(0.045f, 0.075f, -0.02f), 0.0095f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Dots(c, new Vector3(0f, -0.055f, -0.02f), 7, Mathf.Min(7, data.StreakDays));
            VRGlass.Label(c, data.StreakDays > 0 ? "Keep it going!" : "Log today to start", new Vector3(0f, -0.155f, -0.02f), 0.0078f, VRUi.Muted);
        }

        // y is relative to the rail's own centre; x is 0 so the buttons are centred on the rail panel.
        void RailButton(Transform rail, string text, float y, GlassStyle style, Action onClick) =>
            VRGlass.Button(rail, text, new Vector3(0f, y, -0.02f), new Vector2(0.66f, 0.13f), style, 0.0075f, onClick, 0.04f);

        static Transform Card(Transform p, Vector3 pos, Vector2 size)
        {
            var g = new GameObject("Card").transform;
            g.SetParent(p, false);
            g.localPosition = pos;
            VRGlass.Panel(g, "Card panel", new Vector3(0, 0, 0.02f), size, 0.05f, GlassStyle.Card, 1);
            return g;
        }

        static string Initial(string s) => string.IsNullOrEmpty(s) ? "?" : s.Substring(0, 1).ToUpper();

        void CardTitle(Transform c, VRArt.IconKind icon, Color color, string title, float y)
        {
            VRGlass.Icon(c, icon, color, new Vector3(-0.36f, y, -0.02f), 0.14f);
            VRGlass.Label(c, title, new Vector3(-0.25f, y, -0.02f), 0.0105f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
        }

        // Weight shows in kg or lb, whichever the user last chose (the switch is on the profile page).
        static bool Imperial
        {
            get => PlayerPrefs.GetInt("weight_imperial", 0) == 1;
            set => PlayerPrefs.SetInt("weight_imperial", value ? 1 : 0);
        }
        static string Wt(double kg)
        {
            if (!Imperial) return $"{kg:0.#} kg";
            int lb = Mathf.RoundToInt((float)(kg * 2.20462));          // whole pounds, then split into stones; a small change stays in pounds
            return lb >= 14 ? $"{lb / 14} st {lb % 14} lb" : $"{lb} lb";
        }

        // The buttons under the two big cards sit just below them, on one line, so they never collide with the card text.
        const float CardButtonY = -0.56f;

        void BuildNutritionCard(Transform c)
        {
            CardTitle(c, VRArt.IconKind.Apple, new Color(0.40f, 0.95f, 0.70f), "Today's Nutrition", 0.37f);
            VRGlass.Button(c, "View Food Diary", new Vector3(0, CardButtonY, -0.02f), new Vector2(0.8f, 0.12f), GlassStyle.Secondary, 0.0078f, ShowDiary, 0.035f);

            if (!data.Loaded) { VRGlass.Label(c, "Loading...", new Vector3(0, 0.1f, -0.02f), 0.009f, VRUi.Muted); return; }
            if (data.MealsToday == 0)
            {
                VRGlass.Label(c, "Nothing logged today", new Vector3(0, 0.12f, -0.02f), 0.0095f, VRUi.Ink);
                VRGlass.Label(c, "Tell Atlas what you ate\nand it will appear here.", new Vector3(0, -0.02f, -0.02f), 0.0072f, VRUi.Muted);
                return;
            }

            // Donut on the left: calories against the daily goal (a plain count when there is no goal).
            bool goal = data.CalorieGoal.HasValue && data.CalorieGoal.Value > 0;
            float frac = goal ? (float)(data.Kcal / data.CalorieGoal.Value) : 0f;
            // Calories burnt by today's exercise come off what was eaten, so the ring shows the net figure.
            double net = Math.Max(0.0, data.Kcal - data.BurntKcal);
            if (goal) frac = (float)(net / data.CalorieGoal.Value);
            VRGlass.Ring(c, new Vector3(-0.23f, 0.05f, -0.02f), 0.40f, frac, NutritionRing);
            VRGlass.Label(c, $"{net:N0}", new Vector3(-0.23f, 0.075f, -0.03f), 0.019f, VRUi.Ink);
            VRGlass.Label(c, goal ? $"of {data.CalorieGoal.Value:N0} kcal" : "kcal today", new Vector3(-0.23f, -0.02f, -0.03f), 0.0060f, VRUi.Muted);

            // Macros on the right: grams, with bars showing each macro's share of the total grams.
            double totalMacro = Math.Max(1.0, data.Protein + data.Carbs + data.Fat);
            MacroRow(c, "Protein", data.Protein, data.Protein / totalMacro, ProteinTint, 0.24f);
            MacroRow(c, "Carbs", data.Carbs, data.Carbs / totalMacro, CarbsTint, 0.07f);
            MacroRow(c, "Fat", data.Fat, data.Fat / totalMacro, FatTint, -0.10f);

            float mealY = -0.26f;
            if (data.BurntKcal >= 1)
            {
                VRGlass.Label(c, $"Eaten {data.Kcal:N0}  -  Burnt {data.BurntKcal:N0} kcal", new Vector3(0, -0.26f, -0.02f), 0.0072f, VRUi.Muted);
                mealY = -0.37f;
            }
            if (!string.IsNullOrEmpty(data.LatestMeal))
                VRGlass.Label(c, VRUi.Wrap(data.LatestMeal, 30, 1), new Vector3(0, mealY, -0.02f), 0.0082f, VRUi.Ink);
        }

        /// Plain name-and-grams row (the diary's totals block).
        static void MacroRow(Transform c, string name, double grams, float y)
        {
            VRGlass.Label(c, name, new Vector3(-0.40f, y, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(c, $"{grams:0} g", new Vector3(0.40f, y, -0.02f), 0.0085f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
        }

        static void MacroRow(Transform c, string name, double grams, double share, Color tint, float y)
        {
            VRGlass.Label(c, name, new Vector3(0.02f, y + 0.055f, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(c, $"{grams:0} g", new Vector3(0.46f, y + 0.055f, -0.02f), 0.0085f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
            VRGlass.BarColoured(c, new Vector3(0.24f, y, -0.02f), 0.44f, 0.035f, (float)share, tint, Color.Lerp(tint, Color.black, 0.25f));
        }

        void BuildActivityCard(Transform c)
        {
            CardTitle(c, VRArt.IconKind.Dumbbell, new Color(0.45f, 0.75f, 1.0f), "This Week's Activity", 0.37f);
            VRGlass.Button(c, "View Exercise Log", new Vector3(0, CardButtonY, -0.02f), new Vector2(0.8f, 0.12f), GlassStyle.Secondary, 0.0078f, ShowExercise, 0.035f);

            if (!data.Loaded) { VRGlass.Label(c, "Loading...", new Vector3(0, 0.1f, -0.02f), 0.009f, VRUi.Muted); return; }
            if (data.WorkoutsThisWeek == 0)
            {
                VRGlass.Label(c, "No workouts this week", new Vector3(0, 0.12f, -0.02f), 0.0095f, VRUi.Ink);
                VRGlass.Label(c, "Start one from My Experiences.", new Vector3(0, -0.02f, -0.02f), 0.0072f, VRUi.Muted);
                return;
            }

            // This is a weekly count, not progress towards a seven-workout goal.
            VRGlass.Label(c, data.WorkoutsThisWeek.ToString(), new Vector3(0, 0.185f, -0.03f), 0.020f, VRUi.Ink);
            VRGlass.Label(c, data.WorkoutsThisWeek == 1 ? "workout this week" : "workouts this week", new Vector3(0, -0.015f, -0.02f), 0.0082f, VRUi.Muted);

            VRGlass.Label(c, "Latest", new Vector3(-0.38f, -0.10f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(c, VRUi.Wrap(data.LatestWorkout ?? "", 24, 2), new Vector3(-0.38f, -0.22f, -0.02f), 0.0088f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            if (!string.IsNullOrEmpty(data.LatestWorkoutWhen))
                VRGlass.Label(c, data.LatestWorkoutWhen, new Vector3(-0.38f, -0.33f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
        }

        // The third My World card: a way into the plate scan.
        void BuildWellbeingCard(Transform c)
        {
            CardTitle(c, VRArt.IconKind.Apple, new Color(0.40f, 0.95f, 0.70f), "Scan a Plate", 0.37f);
            VRGlass.Button(c, "Scan a Plate", new Vector3(0, CardButtonY, -0.02f), new Vector2(0.8f, 0.12f), GlassStyle.Secondary, 0.0078f, ShowScan, 0.035f);
            VRGlass.Label(c, "Look at your plate.\nAtlas names each food,\noutlines it and measures it.", new Vector3(0, 0.10f, -0.02f), 0.0092f, VRUi.Ink);
            VRGlass.Label(c, "Muse Spark and SAM 3.1", new Vector3(0, -0.12f, -0.02f), 0.0075f, VRUi.Muted);
            VRGlass.Label(c, "You confirm before anything is logged.", new Vector3(0, -0.39f, -0.02f), 0.0062f, VRUi.Muted);
        }

        // ── Food Diary: one day at a time, with previous/next day buttons ───────────────────────────────

        static readonly string[] SlotOrder = { "breakfast", "lunch", "dinner", "snack" };

        void ShowDiary()
        {
            Current = Stage.Diary;
            EnsureAssistant();
            diaryDate = DateTime.Now.Date;
            diaryPage = 0; diaryMessage = ""; pendingDiaryDelete = null;
            BuildDiary();
            StartCoroutine(LoadDiary());
        }

        IEnumerator LoadDiary()
        {
            // Do not leave another day's entries actionable beneath a newly selected date.
            diary.Loading = true;
            diary.Loaded = false;
            if (Current == Stage.Diary) BuildDiary();
            if (supabase == null || !supabase.SignedIn)
            {
                diary.Loading = false;
                diary.Loaded = true;
                diary.HadError = true;
                if (Current == Stage.Diary) BuildDiary();
                yield break;
            }
            yield return diary.Load(supabase, diaryDate);
            if (Current == Stage.Diary) BuildDiary();
        }

        void GoToDay(int offsetDays)
        {
            if (diary.Loading || diaryDeleting) return;                                  // finish the current load first
            diaryPage = 0; diaryMessage = ""; pendingDiaryDelete = null;
            diaryDate = diaryDate.AddDays(offsetDays);
            if (diaryDate > DateTime.Now.Date) diaryDate = DateTime.Now.Date;
            BuildDiary();
            StartCoroutine(LoadDiary());
        }

        static string DayTitle(DateTime d)
        {
            int days = (DateTime.Now.Date - d.Date).Days;
            return days == 0 ? "Today" : days == 1 ? "Yesterday" : d.ToString("dddd d MMM");
        }

        const int DiaryPageSize = 3;
        int diaryPage;
        int diaryMealTab;
        bool diaryDeleting;
        string diaryMessage = "";
        DiaryData.Item? pendingDiaryDelete;

        void BuildDiary()
        {
            var p = NewRoot(new Vector3(0f, 1.65f, 2.55f));
            VRGlass.Panel(p, "Food diary", new Vector3(0, 0, 0.04f), new Vector2(3.6f, 1.95f), 0.08f, GlassStyle.Panel);
            VRGlass.Label(p, "Food Diary", new Vector3(-1.62f, 0.81f, -0.02f), 0.019f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Button(p, "< Previous", new Vector3(-0.55f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f),
                !diary.Loading && !diaryDeleting ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => GoToDay(-1), 0.04f);
            VRGlass.Label(p, DayTitle(diaryDate), new Vector3(0.2f, 0.81f, -0.02f), 0.013f, VRUi.Ink);
            VRGlass.Button(p, "Next >", new Vector3(0.89f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f),
                !diary.Loading && !diaryDeleting && diaryDate < DateTime.Now.Date ? GlassStyle.Secondary : GlassStyle.Disabled,
                0.0085f, () => GoToDay(1), 0.04f);
            VRGlass.Button(p, "My World", new Vector3(1.445f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f), GlassStyle.Secondary, 0.0085f, ShowMyWorld, 0.04f);

            string[] tabLabels = { "Breakfast", "Lunch", "Dinner", "Snacks" };
            for (int tab = 0; tab < SlotOrder.Length; tab++)
            {
                int selected = tab;
                var style = diaryDeleting ? GlassStyle.Disabled : tab == diaryMealTab ? GlassStyle.Primary : GlassStyle.Secondary;
                VRGlass.Button(p, tabLabels[tab], new Vector3(-1.365f + tab * 0.55f, 0.56f, -0.02f), new Vector2(0.51f, 0.18f),
                    style, 0.009f, () => { diaryMealTab = selected; diaryPage = 0; diaryMessage = ""; BuildDiary(); }, 0.04f);
            }
            var mealEntries = new List<DiaryData.Item>();
            foreach (var item in diary.Items)
                if (string.Equals(item.Slot, SlotOrder[diaryMealTab], StringComparison.OrdinalIgnoreCase)) mealEntries.Add(item);

            if (!diary.Loaded || diary.Loading)
                VRGlass.Label(p, "Loading your food diary...", new Vector3(-0.55f, 0.15f, -0.02f), 0.011f, VRUi.Muted);
            else if (diary.HadError)
            {
                VRGlass.Label(p, "Couldn't load this day.", new Vector3(-0.55f, 0.2f, -0.02f), 0.011f, VRUi.Ink);
                VRGlass.Button(p, "Retry", new Vector3(-0.55f, 0, -0.02f), new Vector2(0.6f, 0.16f), GlassStyle.Secondary,
                    0.009f, () => StartCoroutine(LoadDiary()));
            }
            else if (mealEntries.Count == 0)
            {
                VRGlass.Label(p, "No " + tabLabels[diaryMealTab].ToLowerInvariant() + " entries on this day", new Vector3(-0.55f, 0.25f, -0.02f), 0.012f, VRUi.Ink);
                VRGlass.Label(p, "Tell Atlas what you ate to add your first entry.", new Vector3(-0.55f, 0.08f, -0.02f), 0.009f, VRUi.Muted);
            }
            else
            {
                int pages = Mathf.Max(1, (mealEntries.Count + DiaryPageSize - 1) / DiaryPageSize);
                diaryPage = Mathf.Clamp(diaryPage, 0, pages - 1);
                double mealKcal = 0;
                foreach (var item in mealEntries) mealKcal += item.Kcal;
                VRGlass.Label(p, $"{tabLabels[diaryMealTab]} · {mealEntries.Count} entries", new Vector3(-1.61f, 0.36f, -0.02f),
                    0.0095f, Teal, TextAnchor.MiddleLeft, TextAlignment.Left);
                VRGlass.Label(p, $"{mealKcal:N0} kcal", new Vector3(0.52f, 0.36f, -0.02f), 0.0095f, Teal, TextAnchor.MiddleRight, TextAlignment.Right);
                float y = 0.20f;
                for (int i = diaryPage * DiaryPageSize; i < Mathf.Min(mealEntries.Count, (diaryPage + 1) * DiaryPageSize); i++)
                {
                    BuildFoodRow(p, mealEntries[i], y);
                    y -= 0.30f;
                }
                VRGlass.Button(p, "< Items", new Vector3(-1.25f, -0.79f, -0.02f), new Vector2(0.55f, 0.14f),
                    diaryPage > 0 ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => { diaryPage--; BuildDiary(); });
                VRGlass.Label(p, $"Page {diaryPage + 1} of {pages}", new Vector3(-0.55f, -0.79f, -0.02f), 0.009f, VRUi.Muted);
                VRGlass.Button(p, "Items >", new Vector3(0.18f, -0.79f, -0.02f), new Vector2(0.55f, 0.14f),
                    diaryPage + 1 < pages ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => { diaryPage++; BuildDiary(); });
            }

            var t = new GameObject("Day totals").transform;
            t.SetParent(p, false);
            t.localPosition = new Vector3(1.17f, -0.06f, 0f);
            VRGlass.Panel(t, "Totals panel", new Vector3(0, 0, 0.02f), new Vector2(1.05f, 1.4f), 0.05f, GlassStyle.Card, 1);
            VRGlass.Label(t, "Day total", new Vector3(0, 0.56f, -0.02f), 0.011f, VRUi.Muted);
            if (diary.Loaded && !diary.Loading && !diary.HadError)
            {
                bool goal = data.CalorieGoal.HasValue && data.CalorieGoal.Value > 0;
                float progress = goal ? (float)(diary.Kcal / data.CalorieGoal.Value) : 0f;
                VRGlass.Ring(t, new Vector3(0, 0.25f, -0.02f), 0.56f, progress, NutritionRing);
                VRGlass.Label(t, $"{diary.Kcal:N0}", new Vector3(0, 0.28f, -0.03f), 0.022f, VRUi.Ink);
                VRGlass.Label(t, "kcal", new Vector3(0, 0.18f, -0.03f), 0.0085f, VRUi.Muted);
                VRGlass.Label(t, goal ? $"of {data.CalorieGoal.Value:N0} kcal" : "No daily goal set",
                    new Vector3(0, -0.055f, -0.02f), 0.008f, VRUi.Muted);
                double totalMacro = Math.Max(1.0, diary.Protein + diary.Carbs + diary.Fat);
                DiaryMacroRow(t, "Protein", diary.Protein, diary.Protein / totalMacro, ProteinTint, -0.18f);
                DiaryMacroRow(t, "Carbs", diary.Carbs, diary.Carbs / totalMacro, CarbsTint, -0.32f);
                DiaryMacroRow(t, "Fat", diary.Fat, diary.Fat / totalMacro, FatTint, -0.46f);
                VRGlass.Label(t, $"{diary.Items.Count} entries · All meals", new Vector3(0, -0.59f, -0.02f), 0.008f, VRUi.Muted);
            }
            else VRGlass.Label(t, diary.HadError ? "Unavailable" : "Loading...", new Vector3(0, 0.2f, -0.02f), 0.011f, VRUi.Muted);

            VRGlass.Label(p, diaryDeleting ? "Deleting entry..." : diaryMessage, new Vector3(-0.55f, -0.91f, -0.02f), 0.008f, VRUi.Muted);
            if (pendingDiaryDelete.HasValue || diaryDeleting)
                foreach (var button in p.GetComponentsInChildren<VRButton>()) button.Interactable = false;
            if (pendingDiaryDelete.HasValue && !diaryDeleting) BuildDeleteConfirmation(p, pendingDiaryDelete.Value);
        }

        static void DiaryMacroRow(Transform parent, string name, double grams, double share, Color tint, float y)
        {
            VRGlass.Label(parent, name, new Vector3(-0.40f, y + 0.045f, -0.02f), 0.0085f, tint, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(parent, $"{grams:0} g", new Vector3(0.40f, y + 0.045f, -0.02f), 0.0085f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
            VRGlass.BarColoured(parent, new Vector3(0, y - 0.015f, -0.02f), 0.80f, 0.025f, (float)share, tint, Color.Lerp(tint, Color.black, 0.25f));
        }

        void BuildFoodRow(Transform p, DiaryData.Item item, float y)
        {
            VRGlass.Panel(p, "Food entry", new Vector3(-0.54f, y - 0.075f, 0.01f), new Vector2(2.2f, 0.28f), 0.035f, GlassStyle.Card, 1);
            FoodThumbnail.Create(p, new Vector3(-1.49f, y - 0.075f, 0.005f), item.ImageUrl);
            VRGlass.Label(p, VRUi.Wrap(item.Name, 34, 1), new Vector3(-1.30f, y + 0.005f, -0.02f), 0.0095f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(p, item.Amount, new Vector3(-1.30f, y - 0.07f, -0.02f), 0.008f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(p, $"Protein {item.Protein:0.#}g · Carbs {item.Carbs:0.#}g · Fat {item.Fat:0.#}g",
                new Vector3(-1.30f, y - 0.145f, -0.02f), 0.0075f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(p, $"{item.Kcal:N0} kcal", new Vector3(0.46f, y + 0.005f, -0.02f), 0.009f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
            bool enabled = !diaryDeleting && supabase != null && supabase.SignedIn && !string.IsNullOrEmpty(item.Id);
            VRGlass.Button(p, "Delete", new Vector3(0.33f, y - 0.125f, -0.025f), new Vector2(0.34f, 0.12f),
                enabled ? GlassStyle.Secondary : GlassStyle.Disabled, 0.008f,
                () => { pendingDiaryDelete = item; diaryMessage = ""; BuildDiary(); }, 0.03f);
        }

        void BuildDeleteConfirmation(Transform p, DiaryData.Item item)
        {
            // A foreground collider prevents pointer rays reaching controls behind the dialog.
            var shield = VRGlass.Panel(p, "Modal shield", new Vector3(0, 0, -0.10f), new Vector2(3.6f, 1.95f), 0.08f, GlassStyle.Disabled, 20);
            shield.gameObject.AddComponent<BoxCollider>().size = new Vector3(1, 1, 0.02f);
            VRGlass.Panel(p, "Confirm deletion", new Vector3(0, 0, -0.14f), new Vector2(2.4f, 0.72f), 0.06f, GlassStyle.Panel, 21);
            VRGlass.Label(p, "Delete this diary entry?", new Vector3(0, 0.21f, -0.18f), 0.014f, VRUi.Ink, order: 26);
            VRGlass.Label(p, VRUi.Wrap(item.Name, 48, 1), new Vector3(0, 0.07f, -0.18f), 0.009f, VRUi.Muted, order: 26);
            VRGlass.Button(p, "Cancel", new Vector3(-0.50f, -0.18f, -0.18f), new Vector2(0.8f, 0.18f), GlassStyle.Secondary,
                0.010f, () => { pendingDiaryDelete = null; BuildDiary(); }, order: 22);
            VRGlass.Button(p, "Delete entry", new Vector3(0.50f, -0.18f, -0.18f), new Vector2(0.8f, 0.18f), GlassStyle.Primary,
                0.010f, () => StartCoroutine(DeleteDiaryEntry(item.Id)), order: 22);
        }

        IEnumerator DeleteDiaryEntry(string id)
        {
            if (diaryDeleting || supabase == null || !supabase.SignedIn) yield break;
            diaryDeleting = true;
            pendingDiaryDelete = null;
            BuildDiary();
            string error = null;
            bool deleted = false;
            yield return supabase.DeleteFoodEntry(id, () => deleted = true, e => error = e);
            if (deleted)
            {
                diaryMessage = "Entry deleted.";
                yield return diary.Load(supabase, diaryDate);
            }
            else
            {
                diaryMessage = "Couldn't delete this entry. Refresh and try again.";
                Debug.LogWarning("[Diary] Delete failed: " + error);
            }
            diaryDeleting = false;
            if (Current == Stage.Diary) BuildDiary();
            if (deleted) StartCoroutine(RefreshData());
        }

        // ── My Experiences: the way into the virtual gym ────────────────────────────────────────────────

        // ── My Profile: the user's own profile page, from their real profile row and feed ───────────────────────────

        void ShowProfile()
        {
            Current = Stage.Profile;
            EnsureAssistant();
            BuildProfile();
            StartCoroutine(LoadProfile());
            StartCoroutine(RefreshData());
        }

        IEnumerator LoadProfile()
        {
            profile.Loading = true; profile.Loaded = false;
            if (Current == Stage.Profile) BuildProfile();
            yield return profile.Load(supabase);
            if (Current == Stage.Profile) BuildProfile();
        }

        static string Pretty(string s) => string.IsNullOrEmpty(s) ? null : char.ToUpper(s[0]) + s.Substring(1).Replace('_', ' ');

        void BuildProfile()
        {
            var p = NewRoot(new Vector3(0f, 1.65f, 2.55f));
            VRGlass.Panel(p, "Profile", new Vector3(0, 0, 0.04f), new Vector2(3.6f, 1.95f), 0.08f, GlassStyle.Panel);
            VRGlass.Label(p, "My Profile", new Vector3(-1.62f, 0.81f, -0.02f), 0.019f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Button(p, "My World", new Vector3(1.445f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f), GlassStyle.Secondary, 0.0085f, ShowMyWorld, 0.04f);

            if (!profile.Loaded || profile.Loading)
            { VRGlass.Label(p, "Loading your profile...", new Vector3(0, 0.1f, -0.02f), 0.011f, VRUi.Muted); return; }
            if (profile.HadError)
            {
                VRGlass.Label(p, "Couldn't load your profile.", new Vector3(0, 0.2f, -0.02f), 0.011f, VRUi.Ink);
                VRGlass.Button(p, "Retry", new Vector3(0, 0, -0.02f), new Vector2(0.6f, 0.16f), GlassStyle.Secondary, 0.009f, () => StartCoroutine(LoadProfile()));
                return;
            }

            // Layout, in panel units (the panel is 3.6 x 1.95): a 0.1 margin all round and 0.08 gaps between cards.
            // Row 1 (y 0.66 down to -0.12): who you are, four numbers, goals. Row 2 (y -0.20 down to -0.90): your recent posts.

            // Who you are.
            var who = Card(p, new Vector3(-1.05f, 0.27f, 0f), new Vector2(1.30f, 0.78f));
            var profileOrb = VRGlass.Image(who, "Avatar", VRArt.Orb(new Color(0.20f, 0.75f, 0.80f), new Color(0.20f, 0.75f, 0.80f)), new Vector3(-0.46f, 0.20f, -0.01f), new Vector2(0.36f, 0.36f), 3);
            var profileInitial = VRGlass.Label(who, Initial(profile.Name ?? DisplayName), new Vector3(-0.46f, 0.20f, -0.02f), 0.017f, VRUi.Ink);
            AvatarImage.Create(who, new Vector3(-0.46f, 0.20f, -0.01f), profile.AvatarUrl, 0.36f, profileOrb.gameObject, profileInitial.gameObject);   // stand-ins go away once the picture loads
            VRGlass.Label(who, VRUi.Wrap(profile.Name ?? DisplayName, 18, 1), new Vector3(-0.22f, 0.27f, -0.02f), 0.0135f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            if (!string.IsNullOrEmpty(profile.Username))
                VRGlass.Label(who, "@" + profile.Username, new Vector3(-0.22f, 0.16f, -0.02f), 0.0088f, Teal, TextAnchor.MiddleLeft, TextAlignment.Left);
            if (profile.MemberSince.HasValue)
                VRGlass.Label(who, "Member since " + profile.MemberSince.Value.ToString("MMM yyyy"), new Vector3(-0.22f, 0.06f, -0.02f), 0.0078f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(who, string.IsNullOrWhiteSpace(profile.Bio) ? "No bio yet" : VRUi.Wrap(profile.Bio, 36, 3),
                new Vector3(-0.58f, -0.08f, -0.02f), 0.0085f, string.IsNullOrWhiteSpace(profile.Bio) ? VRUi.Muted : VRUi.Ink, TextAnchor.UpperLeft, TextAlignment.Left);

            // How you are doing: four plain numbers.
            var stats = Card(p, new Vector3(0.155f, 0.27f, 0f), new Vector2(0.95f, 0.78f));
            ProfileStat(stats, data.StreakDays.ToString(), "day streak", -0.23f, 0.16f);
            ProfileStat(stats, data.WorkoutsThisWeek.ToString(), "workouts this week", 0.23f, 0.16f);
            ProfileStat(stats, profile.PostCount.ToString(), profile.PostCount == 1 ? "post" : "posts", -0.23f, -0.16f);
            ProfileStat(stats, profile.Followers.ToString(), profile.Followers == 1 ? "follower" : "followers", 0.23f, -0.16f);

            // What you are working towards.
            var goals = Card(p, new Vector3(1.21f, 0.27f, 0f), new Vector2(1.0f, 0.78f));
            VRGlass.Label(goals, "Goals", new Vector3(0, 0.32f, -0.02f), 0.0105f, VRUi.Muted);
            VRGlass.Button(goals, Imperial ? "Show kg" : "Show st lb", new Vector3(0.36f, 0.32f, -0.02f), new Vector2(0.3f, 0.1f), GlassStyle.Secondary, 0.0066f,
                () => { Imperial = !Imperial; BuildProfile(); }, 0.03f);
            float y = 0.20f;
            void Line(string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                VRGlass.Label(goals, label, new Vector3(-0.42f, y, -0.02f), 0.0074f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
                VRGlass.Label(goals, VRUi.Wrap(value, 20, 1), new Vector3(0.42f, y, -0.02f), 0.0082f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
                y -= 0.105f;
            }
            Line("Daily calories", profile.CalorieGoal.HasValue ? $"{profile.CalorieGoal.Value:N0} kcal" : null);
            Line("Weight", profile.WeightKg.HasValue ? Wt(profile.WeightKg.Value) : null);
            Line("Goal weight", profile.GoalWeightKg.HasValue ? Wt(profile.GoalWeightKg.Value) : null);
            Line("Focus", Pretty(profile.GoalType));
            Line("Training days", profile.DaysPerWeek.HasValue ? $"{profile.DaysPerWeek.Value} a week" : null);
            Line("Experience", Pretty(profile.Experience));
            if (y > 0.19f) VRGlass.Label(goals, "No goals set yet", new Vector3(0, 0.1f, -0.02f), 0.0085f, VRUi.Muted);

            // Your feed: a picture (or a plain icon for a text post), the words, and who can see it.
            var feed = Card(p, new Vector3(0f, -0.55f, 0f), new Vector2(3.4f, 0.70f));
            VRGlass.Label(feed, "Recent posts", new Vector3(-1.55f, 0.29f, -0.02f), 0.0105f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            if (profile.Posts.Count == 0)
                VRGlass.Label(feed, "Nothing posted yet. Ask Atlas to share your session.", new Vector3(0, -0.02f, -0.02f), 0.0095f, VRUi.Muted);
            float py = 0.115f;
            foreach (var post in profile.Posts)
            {
                FoodThumbnail.Create(feed, new Vector3(-1.52f, py, -0.02f), post.ImageUrl, 0.15f, VRArt.IconKind.Group);
                VRGlass.Label(feed, VRUi.Wrap(post.Body.Replace('\n', ' '), 88, 1), new Vector3(-1.36f, py + 0.035f, -0.02f), 0.0088f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
                VRGlass.Label(feed, post.When, new Vector3(-1.36f, py - 0.04f, -0.02f), 0.0074f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
                VRGlass.Label(feed, post.Visibility == "public" ? "Public" : post.Visibility == "fof" ? "Friends of friends" : "Friends", new Vector3(1.55f, py, -0.02f), 0.0078f, Teal, TextAnchor.MiddleRight, TextAlignment.Right);
                py -= 0.19f;
            }
        }

        static void ProfileStat(Transform card, string value, string caption, float x, float y)
        {
            VRGlass.Label(card, value, new Vector3(x, y, -0.03f), 0.024f, VRUi.Ink);
            VRGlass.Label(card, caption, new Vector3(x, y - 0.11f, -0.02f), 0.0075f, VRUi.Muted);
        }

        // ── Scan a plate: camera, then Muse Spark names the foods and SAM 3.1 outlines them, then the user confirms what to log ──

        PlateCamera plateCam;
        Texture2D scanShot;
        bool scanWasStreaming, scanWorking, scanLogging, scanDone;
        VoiceLoopTest.PlateResult scanResult;
        string scanNote = "";

        static readonly Color[] ScanColors =
        {
            new Color(0.30f, 0.95f, 0.85f), new Color(1.0f, 0.76f, 0.30f), new Color(0.55f, 0.75f, 1.0f),
            new Color(1.0f, 0.55f, 0.65f), new Color(0.65f, 0.95f, 0.45f),
        };

        // The photo's frame on the left of the panel, in panel units (4:3).
        static readonly Vector2 ScanFrameCentre = new Vector2(-0.70f, -0.07f);
        const float ScanImageW = 1.90f, ScanImageH = 1.425f;

        void ShowScan() => OpenScan(false);

        // Calibration mode uses the same camera screen: an empty plate with a bank card on it gives the plate's real width.
        bool scanCalibrating, calWorking, calSaved;
        VoiceLoopTest.PlateCalibration calResult;
        double calWidth;

        void OpenScan(bool calibrating)
        {
            Current = Stage.Scan;
            EnsureAssistant();
            if (plateCam == null) plateCam = gameObject.AddComponent<PlateCamera>();
            scanCalibrating = calibrating;
            ResetScan();
            plateCam.Begin();
            StartCoroutine(SeeThroughSoon());
            BuildScan();
        }

        // While aiming the camera you see the real room: the backdrop is hidden and passthrough shows behind the screens, so looking down at
        // the plate works. The screens stay where they are. Passthrough needs a moment to start after it is switched on, so this waits.
        OVRPassthroughLayer passLayer;
        ViewFinder viewfinder;
        bool seeThrough;

        IEnumerator SeeThroughSoon()
        {
            yield return new WaitForSecondsRealtime(0.7f);
            if (Current == Stage.Scan && scanShot == null) SetSeeThrough(true);
        }

        void SetSeeThrough(bool on)
        {
            if (seeThrough == on) return;
            seeThrough = on;
            var cam = Camera.main;
            if (on)
            {
                if (passLayer == null)
                {
                    var rig = FindAnyObjectByType<OVRCameraRig>();
                    if (rig != null) passLayer = rig.gameObject.AddComponent<OVRPassthroughLayer>();
                }
                if (passLayer != null) passLayer.hidden = false;
                if (cam != null) cam.backgroundColor = new Color(0f, 0f, 0f, 0f);       // transparent: passthrough shows through
                if (retreat != null) retreat.SetVisible(false);
                if (viewfinder == null && cam != null) viewfinder = ViewFinder.Create(cam.transform);
            }
            else
            {
                if (passLayer != null) passLayer.hidden = true;
                if (cam != null) cam.backgroundColor = Navy;
                if (retreat != null) retreat.SetVisible(true);
                if (viewfinder != null) { Destroy(viewfinder.gameObject); viewfinder = null; }
            }
        }

        // From the on-screen button, or the left trigger / X button, so it works while looking down at the plate.
        void CaptureScan()
        {
            if (Current != Stage.Scan || scanShot != null || plateCam == null || !plateCam.Streaming) return;
            var shot = plateCam.Capture();
            if (shot == null) return;
            scanShot = shot;
            SetSeeThrough(false);
            plateCam.Stop();
            StartCoroutine(ShutterBuzz());
            StartCoroutine(scanCalibrating ? AnalyseCalibration() : AnalyseScan());
        }

        IEnumerator ShutterBuzz()
        {
            OVRInput.SetControllerVibration(0.5f, 0.8f, OVRInput.Controller.LTouch);
            yield return new WaitForSecondsRealtime(0.12f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        }

        void ResetScan()
        {
            if (scanShot != null) { Destroy(scanShot); scanShot = null; }
            scanResult = null; scanNote = ""; scanWorking = scanLogging = scanDone = false;
            calResult = null; calWorking = calSaved = false;
        }

        void StopScan()
        {
            SetSeeThrough(false);
            plateCam.Stop();
            ResetScan();
            scanWasStreaming = false;
        }

        IEnumerator AnalyseCalibration()
        {
            var shot = scanShot;
            calResult = null; calWorking = true; calSaved = false; scanNote = "Finding the plate and the card...";
            BuildScan();
            if (voice == null) { calWorking = false; scanNote = "The assistant is not ready."; BuildScan(); yield break; }
            byte[] jpg = shot.EncodeToJPG(85);
            VoiceLoopTest.PlateCalibration result = null;
            yield return voice.CalibratePlate(jpg, shot.width / (float)shot.height, r => result = r, note => { scanNote = note; if (Current == Stage.Scan && scanShot == shot) BuildScan(); });
            if (Current != Stage.Scan || scanShot != shot) yield break;
            calResult = result; calWorking = false;
            calWidth = result != null && result.Error == null ? result.WidthCm : VoiceLoopTest.PlateCm;
            BuildScan();
        }

        IEnumerator AnalyseScan()
        {
            var shot = scanShot;
            scanResult = null; scanWorking = true; scanDone = false; scanNote = "Looking at your plate...";
            BuildScan();
            if (voice == null) { scanWorking = false; scanNote = "The assistant is not ready."; BuildScan(); yield break; }
            byte[] jpg = shot.EncodeToJPG(85);
            VoiceLoopTest.PlateResult result = null;
            yield return voice.ScanPlate(jpg, r => result = r, note => { scanNote = note; if (Current == Stage.Scan && scanShot == shot) BuildScan(); });
            if (Current != Stage.Scan || scanShot != shot) yield break;       // they left or retook meanwhile
            scanResult = result; scanWorking = false;
            BuildScan();
        }

        IEnumerator LogScanItems()
        {
            scanLogging = true; scanNote = "Logging...";
            BuildScan();
            string note = null;
            yield return voice.LogScan(scanResult.Items, n => note = n);
            scanLogging = false; scanDone = true; scanNote = note;
            if (Current == Stage.Scan) BuildScan();
        }

        void BuildScan()
        {
            scanWasStreaming = plateCam != null && plateCam.Streaming;
            var p = NewRoot(new Vector3(0f, 1.65f, 2.55f));
            VRGlass.Panel(p, "Scan a plate", new Vector3(0, 0, 0.04f), new Vector2(3.6f, 1.95f), 0.08f, GlassStyle.Panel);
            VRGlass.Label(p, "Scan a Plate", new Vector3(-1.62f, 0.81f, -0.02f), 0.019f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Button(p, "My World", new Vector3(1.445f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f), GlassStyle.Secondary, 0.0085f, ShowMyWorld, 0.04f);

            VRGlass.Label(p, "Powered by Meta Muse Spark and Meta SAM 3.1", new Vector3(1.06f, -0.87f, -0.02f), 0.0072f, VRUi.Muted, TextAnchor.MiddleCenter, TextAlignment.Center);

            // The camera picture, or the frozen frame once captured.
            VRGlass.Panel(p, "Frame", new Vector3(ScanFrameCentre.x, ScanFrameCentre.y, 0.02f), new Vector2(2.0f, 1.5f), 0.05f, GlassStyle.Card, 1);
            Texture picture = scanShot != null ? (Texture)scanShot : (plateCam != null && plateCam.Streaming ? plateCam.Live : null);
            if (picture != null)
                VRGlass.Image(p, "Picture", picture, new Vector3(ScanFrameCentre.x, ScanFrameCentre.y, -0.01f), new Vector2(ScanImageW, ScanImageH), 3);
            else
                VRGlass.Label(p, plateCam != null ? VRUi.Wrap(plateCam.Status, 30, 3) : "Starting...", new Vector3(ScanFrameCentre.x, ScanFrameCentre.y, -0.02f), 0.011f, VRUi.Muted);

            const float rx = 1.07f;     // centre of the right-hand column

            // 1. Live camera.
            if (scanShot == null)
            {
                bool ready = plateCam != null && plateCam.Streaming;
                string hint = scanCalibrating
                    ? "Lay a card flat on an empty plate, then press the left trigger"
                    : "Hold the headset above the plate at a slight angle, with the whole plate inside the frame, then press the left trigger";
                VRGlass.Label(p, VRUi.Wrap(ready ? hint : plateCam != null ? plateCam.Status : "", 22, 3), new Vector3(rx, 0.45f, -0.02f), 0.0105f, VRUi.Ink);
                VRGlass.Button(p, "Capture", new Vector3(rx, 0.12f, -0.02f), new Vector2(0.9f, 0.18f), ready ? GlassStyle.Primary : GlassStyle.Disabled, 0.0095f,
                    CaptureScan, 0.04f);
                if (plateCam != null && plateCam.Failed)
                    VRGlass.Button(p, "Try again", new Vector3(rx, -0.12f, -0.02f), new Vector2(0.9f, 0.16f), GlassStyle.Secondary, 0.0088f, () => { plateCam.Begin(); BuildScan(); }, 0.04f);

                // Optional: measure your plate once with a bank card, so food weights use its real width.
                VRGlass.Label(p, VRUi.Wrap(scanCalibrating ? "Use a dark or white card. A card close to your plate's colour can't be seen." : "Would you like to calibrate your plate? You will need a bank or credit card.", 30, 3),
                    new Vector3(rx, -0.40f, -0.02f), 0.0078f, VRUi.Muted);
                VRGlass.Button(p, scanCalibrating ? "Back to scanning food" : "Calibrate plate", new Vector3(rx, -0.62f, -0.02f), new Vector2(1.0f, 0.15f), GlassStyle.Secondary, 0.0085f,
                    () => OpenScan(!scanCalibrating), 0.04f);
                return;
            }

            // 1b. A calibration photo: the plate and the card outlined, and the plate's width worked out.
            if (scanCalibrating)
            {
                if (calWorking)
                {
                    VRGlass.Label(p, VRUi.Wrap(scanNote, 24, 3), new Vector3(rx, 0.30f, -0.02f), 0.0105f, VRUi.Ink);
                    VRGlass.Label(p, "This takes a few seconds", new Vector3(rx, 0.05f, -0.02f), 0.0078f, VRUi.Muted);
                    return;
                }
                if (calResult == null || calResult.Error != null)
                {
                    VRGlass.Label(p, VRUi.Wrap(calResult != null ? calResult.Error : scanNote, 26, 5), new Vector3(rx, 0.28f, -0.02f), 0.0095f, VRUi.Ink);
                    VRGlass.Button(p, "Retake", new Vector3(rx, -0.20f, -0.02f), new Vector2(0.9f, 0.17f), GlassStyle.Primary, 0.0092f, () => OpenScan(true), 0.04f);
                    VRGlass.Button(p, "Back to scanning food", new Vector3(rx, -0.42f, -0.02f), new Vector2(0.9f, 0.17f), GlassStyle.Secondary, 0.0085f, () => OpenScan(false), 0.04f);
                    return;
                }
                DrawScanBox(p, calResult.Plate.Value, ScanColors[0], "plate");
                DrawScanBox(p, calResult.Card.Value, ScanColors[1], "card");
                VRGlass.Label(p, calSaved ? "Saved. Food scans will use this width." : "Your plate is about", new Vector3(rx, 0.52f, -0.02f), 0.0095f, VRUi.Muted);
                VRGlass.Label(p, $"{calWidth:0.0} cm", new Vector3(rx, 0.30f, -0.03f), 0.026f, VRUi.Ink);
                VRGlass.Button(p, "-", new Vector3(rx - 0.30f, 0.06f, -0.025f), new Vector2(0.2f, 0.15f), GlassStyle.Secondary, 0.0105f,
                    () => { calWidth = Math.Max(16, calWidth - 0.5); calSaved = false; BuildScan(); }, 0.03f);
                VRGlass.Button(p, "+", new Vector3(rx + 0.30f, 0.06f, -0.025f), new Vector2(0.2f, 0.15f), GlassStyle.Secondary, 0.0105f,
                    () => { calWidth = Math.Min(36, calWidth + 0.5); calSaved = false; BuildScan(); }, 0.03f);
                VRGlass.Button(p, calSaved ? "Scan food now" : "Save plate width", new Vector3(rx, -0.18f, -0.02f), new Vector2(0.9f, 0.17f), GlassStyle.Primary, 0.0092f,
                    () => { if (calSaved) OpenScan(false); else { VoiceLoopTest.PlateCm = calWidth; calSaved = true; BuildScan(); } }, 0.04f);
                VRGlass.Button(p, "Retake", new Vector3(rx, -0.40f, -0.02f), new Vector2(0.9f, 0.17f), GlassStyle.Secondary, 0.0088f, () => OpenScan(true), 0.04f);
                return;
            }

            // 2. Working, failed, or done.
            bool failed = !scanWorking && (scanResult == null || scanResult.Error != null);
            if (scanWorking || scanLogging || failed || scanDone)
            {
                string line = failed ? (scanResult != null ? scanResult.Error : scanNote) : scanNote;
                VRGlass.Label(p, VRUi.Wrap(line, 24, 4), new Vector3(rx, 0.30f, -0.02f), 0.0105f, VRUi.Ink);
                if (scanWorking || scanLogging) VRGlass.Label(p, "This takes a few seconds", new Vector3(rx, 0.05f, -0.02f), 0.0078f, VRUi.Muted);
                bool idle = !scanWorking && !scanLogging;
                if (scanDone)
                {
                    VRGlass.Button(p, "Scan another", new Vector3(rx, 0.0f, -0.02f), new Vector2(0.9f, 0.17f), GlassStyle.Primary, 0.0092f, ShowScan, 0.04f);
                    VRGlass.Button(p, "Food diary", new Vector3(rx, -0.24f, -0.02f), new Vector2(0.9f, 0.17f), GlassStyle.Secondary, 0.0092f, ShowDiary, 0.04f);
                }
                else VRGlass.Button(p, "Retake", new Vector3(rx, -0.20f, -0.02f), new Vector2(0.9f, 0.17f), idle ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0092f, ShowScan, 0.04f);
                if (!failed) return;
                return;
            }

            // 3. The result: outlines on the photo, and the foods to confirm.
            var items = scanResult.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Dropped) continue;
                var color = ScanColors[i % ScanColors.Length];
                foreach (var r in items[i].Boxes) DrawScanBox(p, r, color, items[i].Boxes.IndexOf(r) == 0 ? items[i].Name : null);
            }

            VRGlass.Label(p, "Tap a name to change it. - and + correct the weight.", new Vector3(rx, 0.70f, -0.02f), 0.0082f, VRUi.Muted);
            double total = 0; int loggable = 0; float y = 0.52f;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Dropped) continue;
                var color = ScanColors[i % ScanColors.Length];
                var captured = item;
                // The row is a button: tap it to swap to the next-best database entry (when there is more than one).
                VRGlass.Button(p, "", new Vector3(rx, y, 0.01f), new Vector2(1.30f, 0.20f), GlassStyle.Card,
                    0.008f, item.Candidates.Count > 1 ? (System.Action)(() => { captured.Next(); BuildScan(); }) : null, 0.03f, 1);
                VRUi.Box(p, "Swatch", new Vector3(rx - 0.60f, y, -0.015f), new Vector3(0.035f, 0.14f, 0.01f), color, false);
                VRGlass.Label(p, VRUi.Wrap(item.FoodId != null ? item.MatchedName : item.Name, 28, 1), new Vector3(rx - 0.55f, y + 0.045f, -0.02f), 0.0088f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
                string detail;
                if (item.FoodId != null)
                {
                    string weight = item.Measured.HasValue && !item.Adjusted ? $"{item.Grams:0} g measured (guess {item.EstimatedGrams:0})" : $"{item.Grams:0} g";
                    detail = weight + (item.Kcal.HasValue ? $" · {item.Kcal.Value:0} kcal" : "");
                    total += item.Kcal ?? 0; loggable++;
                }
                else detail = "Not found in your food database";
                VRGlass.Label(p, VRUi.Wrap(detail, 46, 1), new Vector3(rx - 0.55f, y - 0.05f, -0.02f), 0.0074f, item.FoodId != null ? VRUi.Muted : new Color(1f, 0.76f, 0.30f), TextAnchor.MiddleLeft, TextAlignment.Left);
                if (item.FoodId != null)
                {
                    // Correct the weight: - and + change it by 10 g (25 g for bigger portions). The calories follow.
                    VRGlass.Button(p, "-", new Vector3(rx + 0.22f, y, -0.025f), new Vector2(0.15f, 0.15f), GlassStyle.Secondary, 0.0105f,
                        () => { captured.AdjustGrams(captured.Grams > 150 ? -25 : -10); BuildScan(); }, 0.03f, 6);
                    VRGlass.Button(p, "+", new Vector3(rx + 0.39f, y, -0.025f), new Vector2(0.15f, 0.15f), GlassStyle.Secondary, 0.0105f,
                        () => { captured.AdjustGrams(captured.Grams >= 150 ? 25 : 10); BuildScan(); }, 0.03f, 6);
                }
                VRGlass.Button(p, "x", new Vector3(rx + 0.56f, y, -0.025f), new Vector2(0.15f, 0.15f), GlassStyle.Secondary, 0.0095f, () => { captured.Dropped = true; BuildScan(); }, 0.03f, 6);
                y -= 0.23f;
            }
            // The plate's real width sets the scale for measuring the food. Measure the plate you use and set it once.
            VRGlass.Label(p, scanResult.Plate.HasValue ? $"Plate width {VoiceLoopTest.PlateCm:0} cm" : "No plate found: weights are guesses",
                new Vector3(-1.62f, -0.905f, -0.02f), 0.0085f, scanResult.Plate.HasValue ? VRUi.Ink : new Color(1f, 0.76f, 0.30f), TextAnchor.MiddleLeft, TextAlignment.Left);
            if (scanResult.Plate.HasValue)
            {
                VRGlass.Button(p, "-", new Vector3(-0.62f, -0.905f, -0.02f), new Vector2(0.14f, 0.12f), GlassStyle.Secondary, 0.0095f,
                    () => { VoiceLoopTest.PlateCm -= 1; scanResult.Remeasure(VoiceLoopTest.PlateCm); BuildScan(); }, 0.03f);
                VRGlass.Button(p, "+", new Vector3(-0.44f, -0.905f, -0.02f), new Vector2(0.14f, 0.12f), GlassStyle.Secondary, 0.0095f,
                    () => { VoiceLoopTest.PlateCm += 1; scanResult.Remeasure(VoiceLoopTest.PlateCm); BuildScan(); }, 0.03f);
            }
            VRGlass.Label(p, "Weights are measured from the photo, so the angle and framing change them.", new Vector3(rx, -0.665f, -0.02f), 0.0068f, VRUi.Muted);
            if (loggable > 0) VRGlass.Label(p, $"About {total:0} kcal in total", new Vector3(rx, -0.57f, -0.02f), 0.0092f, VRUi.Ink);
            else VRGlass.Label(p, "Nothing here can be logged", new Vector3(rx, -0.57f, -0.02f), 0.0092f, VRUi.Muted);
            string meal = CoachAgent.MealNow();
            VRGlass.Button(p, $"Log to {meal}", new Vector3(rx - 0.27f, -0.80f, -0.02f), new Vector2(0.76f, 0.15f), loggable > 0 ? GlassStyle.Primary : GlassStyle.Disabled, 0.0088f,
                () => { if (loggable > 0 && !scanLogging) StartCoroutine(LogScanItems()); }, 0.04f);
            VRGlass.Button(p, "Retake", new Vector3(rx + 0.40f, -0.80f, -0.02f), new Vector2(0.44f, 0.15f), GlassStyle.Secondary, 0.0088f, ShowScan, 0.04f);
        }

        // One SAM outline: four thin bars round the box, mapped from photo fractions (y from the top) onto the frame.
        void DrawScanBox(Transform p, Rect r, Color color, string label)
        {
            float cx = ScanFrameCentre.x + (r.x + r.width / 2f - 0.5f) * ScanImageW;
            float cy = ScanFrameCentre.y - (r.y + r.height / 2f - 0.5f) * ScanImageH;
            float w = r.width * ScanImageW, h = r.height * ScanImageH, t = 0.012f;
            VRUi.Box(p, "Box top", new Vector3(cx, cy + h / 2f, -0.025f), new Vector3(w, t, 0.01f), color, false);
            VRUi.Box(p, "Box bottom", new Vector3(cx, cy - h / 2f, -0.025f), new Vector3(w, t, 0.01f), color, false);
            VRUi.Box(p, "Box left", new Vector3(cx - w / 2f, cy, -0.025f), new Vector3(t, h, 0.01f), color, false);
            VRUi.Box(p, "Box right", new Vector3(cx + w / 2f, cy, -0.025f), new Vector3(t, h, 0.01f), color, false);
            if (!string.IsNullOrEmpty(label))
                VRGlass.Label(p, label, new Vector3(cx - w / 2f + 0.02f, cy + h / 2f - 0.035f, -0.03f), 0.0078f, color, TextAnchor.MiddleLeft, TextAlignment.Left, 9);
        }

        // ── Exercise log: every recorded exercise for a day, from the real strength_sets and exercise_logs ──────────────

        const int ExercisePageSize = 4;

        void ShowExercise()
        {
            Current = Stage.Exercise;
            EnsureAssistant();
            exerciseDate = DateTime.Now.Date;
            exercisePage = 0;
            BuildExercise();
            StartCoroutine(LoadExercise());
        }

        IEnumerator LoadExercise()
        {
            exercise.Loading = true; exercise.Loaded = false;
            if (Current == Stage.Exercise) BuildExercise();
            yield return exercise.Load(supabase, exerciseDate);
            if (Current == Stage.Exercise) BuildExercise();
        }

        void GoToExerciseDay(int offsetDays)
        {
            if (exercise.Loading) return;
            exercisePage = 0;
            exerciseDate = exerciseDate.AddDays(offsetDays);
            if (exerciseDate > DateTime.Now.Date) exerciseDate = DateTime.Now.Date;
            BuildExercise();
            StartCoroutine(LoadExercise());
        }

        void BuildExercise()
        {
            var accent = new Color(0.45f, 0.75f, 1.0f);
            var p = NewRoot(new Vector3(0f, 1.65f, 2.55f));
            VRGlass.Panel(p, "Exercise log", new Vector3(0, 0, 0.04f), new Vector2(3.6f, 1.95f), 0.08f, GlassStyle.Panel);
            VRGlass.Label(p, "Exercise Log", new Vector3(-1.62f, 0.81f, -0.02f), 0.019f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Button(p, "< Previous", new Vector3(-0.55f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f),
                !exercise.Loading ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => GoToExerciseDay(-1), 0.04f);
            VRGlass.Label(p, DayTitle(exerciseDate), new Vector3(0.2f, 0.81f, -0.02f), 0.013f, VRUi.Ink);
            VRGlass.Button(p, "Next >", new Vector3(0.89f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f),
                !exercise.Loading && exerciseDate < DateTime.Now.Date ? GlassStyle.Secondary : GlassStyle.Disabled,
                0.0085f, () => GoToExerciseDay(1), 0.04f);
            VRGlass.Button(p, "My World", new Vector3(1.445f, 0.81f, -0.02f), new Vector2(0.5f, 0.16f), GlassStyle.Secondary, 0.0085f, ShowMyWorld, 0.04f);

            if (!exercise.Loaded || exercise.Loading)
                VRGlass.Label(p, "Loading your exercise log...", new Vector3(-0.55f, 0.15f, -0.02f), 0.011f, VRUi.Muted);
            else if (exercise.HadError)
            {
                VRGlass.Label(p, "Couldn't load this day.", new Vector3(-0.55f, 0.2f, -0.02f), 0.011f, VRUi.Ink);
                VRGlass.Button(p, "Retry", new Vector3(-0.55f, 0, -0.02f), new Vector2(0.6f, 0.16f), GlassStyle.Secondary,
                    0.009f, () => StartCoroutine(LoadExercise()));
            }
            else if (exercise.Items.Count == 0)
            {
                VRGlass.Label(p, "No exercise recorded on this day", new Vector3(-0.55f, 0.25f, -0.02f), 0.012f, VRUi.Ink);
                VRGlass.Label(p, "Do a squat set in the gym, or tell Atlas what you did.", new Vector3(-0.55f, 0.08f, -0.02f), 0.009f, VRUi.Muted);
            }
            else
            {
                int pages = Mathf.Max(1, (exercise.Items.Count + ExercisePageSize - 1) / ExercisePageSize);
                exercisePage = Mathf.Clamp(exercisePage, 0, pages - 1);
                VRGlass.Label(p, $"{exercise.Items.Count} exercise{(exercise.Items.Count == 1 ? "" : "s")}", new Vector3(-1.61f, 0.55f, -0.02f),
                    0.0095f, accent, TextAnchor.MiddleLeft, TextAlignment.Left);
                float y = 0.42f;
                for (int i = exercisePage * ExercisePageSize; i < Mathf.Min(exercise.Items.Count, (exercisePage + 1) * ExercisePageSize); i++)
                {
                    var item = exercise.Items[i];
                    VRGlass.Panel(p, "Exercise entry", new Vector3(-0.54f, y - 0.075f, 0.01f), new Vector2(2.2f, 0.28f), 0.035f, GlassStyle.Card, 1);
                    FoodThumbnail.Create(p, new Vector3(-1.49f, y - 0.075f, 0.005f), item.ImageUrl, 0.20f, VRArt.IconKind.Dumbbell);      // same size and place as the diary's food thumbnails
                    VRGlass.Label(p, VRUi.Wrap(item.Name, 34, 1), new Vector3(-1.30f, y - 0.025f, -0.02f), 0.0105f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
                    VRGlass.Label(p, VRUi.Wrap(item.Detail, 46, 1), new Vector3(-1.30f, y - 0.11f, -0.02f), 0.0085f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
                    string right = item.Minutes > 0 && item.Kcal > 0 ? $"{item.Minutes:0.#} min  -  {item.Kcal:N0} kcal"
                                 : item.Kcal > 0 ? $"{item.Kcal:N0} kcal burnt"
                                 : item.Minutes > 0 ? $"{item.Minutes:0.#} min" : null;
                    if (right != null)
                        VRGlass.Label(p, right, new Vector3(0.46f, y - 0.025f, -0.02f), 0.009f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
                    if (!string.IsNullOrEmpty(item.Tag))
                        VRGlass.Label(p, item.Tag, new Vector3(0.46f, y - 0.11f, -0.02f), 0.0075f, Teal, TextAnchor.MiddleRight, TextAlignment.Right);
                    y -= 0.31f;
                }
                VRGlass.Button(p, "< Items", new Vector3(-1.25f, -0.79f, -0.02f), new Vector2(0.55f, 0.14f),
                    exercisePage > 0 ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => { exercisePage--; BuildExercise(); });
                VRGlass.Label(p, $"Page {exercisePage + 1} of {pages}", new Vector3(-0.55f, -0.79f, -0.02f), 0.009f, VRUi.Muted);
                VRGlass.Button(p, "Items >", new Vector3(0.18f, -0.79f, -0.02f), new Vector2(0.55f, 0.14f),
                    exercisePage + 1 < pages ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => { exercisePage++; BuildExercise(); });
            }

            var t = new GameObject("Day totals").transform;
            t.SetParent(p, false);
            t.localPosition = new Vector3(1.17f, -0.06f, 0f);
            VRGlass.Panel(t, "Totals panel", new Vector3(0, 0, 0.02f), new Vector2(1.05f, 1.4f), 0.05f, GlassStyle.Card, 1);
            VRGlass.Label(t, "Day total", new Vector3(0, 0.56f, -0.02f), 0.011f, VRUi.Muted);
            if (exercise.Loaded && !exercise.Loading && !exercise.HadError)
            {
                ExerciseStat(t, exercise.TotalSets.ToString(), exercise.TotalSets == 1 ? "set" : "sets", 0.38f);
                ExerciseStat(t, exercise.TotalReps.ToString(), exercise.TotalReps == 1 ? "rep" : "reps", 0.10f);
                ExerciseStat(t, exercise.TotalMinutes > 0 ? exercise.TotalMinutes.ToString("0.#") : "-", "minutes", -0.18f);
                ExerciseStat(t, exercise.TotalKcal > 0 ? exercise.TotalKcal.ToString("N0") : "-", "kcal burnt (est.)", -0.46f);
            }
            else VRGlass.Label(t, exercise.HadError ? "Unavailable" : "Loading...", new Vector3(0, 0.2f, -0.02f), 0.011f, VRUi.Muted);
        }

        static void ExerciseStat(Transform parent, string value, string unit, float y)
        {
            VRGlass.Label(parent, value, new Vector3(0, y, -0.03f), 0.026f, VRUi.Ink);
            VRGlass.Label(parent, unit, new Vector3(0, y - 0.12f, -0.02f), 0.0085f, VRUi.Muted);
        }

        void ShowExperiences()
        {
            Current = Stage.Experiences;
            EnsureAssistant();
            BuildExperiences();
            StartCoroutine(RefreshData());
        }

        void BuildExperiences()
        {
            var p = NewRoot(new Vector3(0f, 1.6f, 2.6f));

            VRGlass.Panel(p, "Panel", new Vector3(0, 0, 0.04f), new Vector2(3.4f, 1.7f), 0.08f, GlassStyle.Panel, 0);
            VRGlass.Label(p, "My Experiences", new Vector3(-1.5f, 0.68f, -0.02f), 0.02f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(p, "Interactive activities and virtual spaces.", new Vector3(-1.5f, 0.54f, -0.02f), 0.0085f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Button(p, "Home", new Vector3(1.38f, 0.66f, -0.02f), new Vector2(0.5f, 0.14f), GlassStyle.Secondary, 0.0085f, ShowHome, 0.04f);

            // The Virtual Gym card
            var g = new GameObject("Virtual Gym card").transform;
            g.SetParent(p, false);
            g.localPosition = new Vector3(0f, -0.1f, 0f);
            VRGlass.Panel(g, "Card panel", new Vector3(0, 0, 0.02f), new Vector2(3.1f, 1.05f), 0.06f, GlassStyle.Amber, 1);
            VRGlass.Icon(g, VRArt.IconKind.Dumbbell, Amber, new Vector3(-1.32f, 0.17f, -0.02f), 0.36f);
            VRGlass.Label(g, "Virtual Gym", new Vector3(-1.05f, 0.30f, -0.02f), 0.0155f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(g, "A guided squat session. Calibrate, then the headset counts your reps\nand the set is saved to your A Better U profile.",
                new Vector3(-1.05f, 0.10f, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            string tip = "Ask Atlas to set up a squat workout, or just start a set.";
            VRGlass.Label(g, tip, new Vector3(-1.05f, -0.12f, -0.02f), 0.0082f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(g, "Left trigger starts and ends a set  ·  Atlas saves it for you", new Vector3(-1.05f, -0.26f, -0.02f), 0.0072f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            VRGlass.Button(g, "Start Virtual Gym", new Vector3(1.05f, 0.05f, -0.02f), new Vector2(0.85f, 0.2f), GlassStyle.Primary, 0.0095f, EnterGym, 0.05f);
            VRGlass.Button(g, "How to: Squat", new Vector3(1.05f, -0.17f, -0.02f), new Vector2(0.85f, 0.15f), GlassStyle.Secondary, 0.0088f, () => { if (voice != null) voice.ShowHowTo("Bodyweight Squat"); }, 0.04f);
        }

        // ── Moving on ───────────────────────────────────────────────────────────────────────────────────

        void SwitchAccount()
        {
            supabase.SignOut();
            email = ""; password = "";
            ShowLogin();
        }

        void EnterGym()
        {
            Destroy(pointer);
            pointer = null;
            CloseKeyboard();
            if (screen != null) Destroy(screen.gameObject);
            if (assistant != null) { Destroy(assistant.gameObject); assistant = null; }
            if (retreat != null) { retreat.Teardown(); Destroy(retreat); }
            board.SetVisible(true);
            Current = Stage.Gym;
            voice.EnterGym();
        }

        public void SetWorkoutPointer(bool visible)
        {
            if (Current != Stage.Gym || MenuOpen) return;
            if (!visible)
            {
                if (pointer != null) Destroy(pointer);
                pointer = null;
            }
            else if (pointer == null)
            {
                pointer = gameObject.AddComponent<VRPointer>();
                pointer.Init(Camera.main != null ? Camera.main.transform : transform);
            }
        }

        // ── In-gym menu: the left Menu button opens it ──────────────────────────────────────────────────

        void ApplyScreenZoom()
        {
            if (screen == null) return;
            var cam = Camera.main;
            Vector3 head = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 toScreen = new Vector3(screenBasePos.x - head.x, 0f, screenBasePos.z - head.z);
            if (toScreen.sqrMagnitude < 0.01f) return;
            screen.position = screenBasePos - toScreen.normalized * screenZoom;
        }

        void Update()
        {
            // Right thumbstick up/down pulls the screen closer or pushes it back (not in the gym or on the plate camera, which use the stick).
            if (screen != null && Current != Stage.Gym && Current != Stage.Scan && !MenuOpen)
            {
                float stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;
                if (Mathf.Abs(stick) > 0.2f)
                {
                    screenZoom = Mathf.Clamp(screenZoom + stick * Time.deltaTime * 1.0f, -1.5f, 1.2f);
                    ApplyScreenZoom();
                }
            }
            if (Current == Stage.Scan && scanShot == null &&
                (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch) || OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.LTouch)))
                CaptureScan();
            // The camera only runs on the scan screen, and rebuilds that screen once when its picture starts.
            if (Current != Stage.Scan && plateCam != null && (plateCam.Live != null || scanShot != null || scanResult != null || calResult != null)) StopScan();
            else if (Current == Stage.Scan && plateCam != null && plateCam.Streaming != scanWasStreaming) BuildScan();
            if (Current == Stage.Gym && OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            {
                if (MenuOpen) CloseMenu(); else OpenMenu();
            }
        }

        void OpenMenu()
        {
            if (pointer != null) Destroy(pointer);
            pointer = null;
            var head = Camera.main != null ? Camera.main.transform : transform;
            Vector3 forward = head.forward; forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;

            menuRoot = new GameObject("GymMenu").transform;
            menuRoot.position = new Vector3(head.position.x, Mathf.Max(head.position.y - 0.05f, 1.1f), head.position.z) + forward * 1.3f;
            menuRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);    // its +Z points away from you, so the text faces you

            VRGlass.Panel(menuRoot, "Panel", new Vector3(0, 0, 0.03f), new Vector2(1.3f, voice != null && voice.WorkoutInProgress ? 1.75f : 1.4f), 0.06f, GlassStyle.Panel, 0);
            VRGlass.Label(menuRoot, "Menu", new Vector3(0, 0.46f, -0.02f), 0.016f, VRUi.Ink);

            VRGlass.Button(menuRoot, "Resume", new Vector3(0, 0.25f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Primary, 0.010f, CloseMenu);
            VRGlass.Button(menuRoot, "Squat demo video", new Vector3(0, 0.05f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, () => { CloseMenu(); voice.ShowHowTo("Bodyweight Squat"); });
            VRGlass.Button(menuRoot, "Back to hub", new Vector3(0, -0.15f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, BackToHub);
            VRGlass.Button(menuRoot, "Switch account", new Vector3(0, -0.35f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, LeaveToLogin);
            VRGlass.Button(menuRoot, "Quit App", new Vector3(0, -0.55f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, ShowQuitConfirmation);

            if (voice != null && voice.WorkoutInProgress)
                VRGlass.Button(menuRoot, "Finish workout", new Vector3(0, -0.75f, -0.02f), new Vector2(1.0f, 0.16f),
                    GlassStyle.Secondary, 0.009f, () => { voice.RequestFinishWorkout(); CloseMenu(); });

            pointer = gameObject.AddComponent<VRPointer>();
            pointer.Init(head);
            pointer.PreferLeftHand(true);        // the left Menu button opened this, so that hand is already up
            MenuOpen = true;
        }

        void CloseMenu()
        {
            CloseQuitConfirmation();
            if (pointer != null) Destroy(pointer);
            pointer = null;
            if (menuRoot != null) Destroy(menuRoot.gameObject);
            MenuOpen = false;
            SetWorkoutPointer(voice != null && voice.WorkoutControlsVisible);
        }

        void ShowQuitConfirmation()
        {
            if (quitDialog != null) return;
            var camera = Camera.main;
            if (camera == null) return;
            quitDialog = new GameObject("Quit confirmation").transform;
            quitDialog.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 1.4f,
                Quaternion.LookRotation(camera.transform.forward, camera.transform.up));

            // Intercept rays before they reach any buttons on the screen behind the prompt.
            var shield = VRGlass.Panel(quitDialog, "Input shield", new Vector3(0, 0, 0.03f), new Vector2(3.6f, 2.4f),
                0.08f, GlassStyle.Disabled, 40);
            shield.gameObject.AddComponent<BoxCollider>().size = new Vector3(1, 1, 0.02f);
            VRGlass.Panel(quitDialog, "Dialog", Vector3.zero, new Vector2(1.5f, 0.68f), 0.06f, GlassStyle.Panel, 41);
            VRGlass.Label(quitDialog, "Quit A Better U?", new Vector3(0, 0.19f, -0.02f), 0.015f, VRUi.Ink, order: 46);
            VRGlass.Label(quitDialog, "Any unsaved activity will be lost.", new Vector3(0, 0.04f, -0.02f), 0.0085f, VRUi.Muted, order: 46);
            VRGlass.Button(quitDialog, "Cancel", new Vector3(-0.34f, -0.18f, -0.02f), new Vector2(0.58f, 0.16f),
                GlassStyle.Secondary, 0.0095f, CloseQuitConfirmation, order: 42);
            VRGlass.Button(quitDialog, "Quit App", new Vector3(0.34f, -0.18f, -0.02f), new Vector2(0.58f, 0.16f),
                GlassStyle.Primary, 0.0095f, QuitApp, order: 42);
        }

        void CloseQuitConfirmation()
        {
            if (quitDialog != null) Destroy(quitDialog.gameObject);
            quitDialog = null;
        }

        static void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy() => CloseQuitConfirmation();

        // Reloading the scene is the simplest clean reset; the signed-in session survives in the static above.
        public void LeaveGym() => BackToHub();
        void BackToHub() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        void LeaveToLogin()
        {
            supabase.SignOut();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
