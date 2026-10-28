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
        public enum Stage { Login, Home, MyWorld, Diary, Experiences, Gym }
        public Stage Current { get; private set; } = Stage.Login;

        /// True while the in-gym menu is open, so the right trigger clicks menu buttons instead of starting the mic.
        public bool MenuOpen { get; private set; }

        /// The signed-in session lives in a static so it survives a scene reload ("Back to hub" does not ask you to log in again).
        public static SupabaseClient Session;

        static readonly Color Navy = new Color(0.04f, 0.06f, 0.10f);
        static readonly Color Teal = new Color(0.45f, 0.92f, 0.86f);
        static readonly Color Amber = new Color(1.0f, 0.76f, 0.30f);

        SupabaseClient supabase;
        StatusBoard board;
        VoiceLoopTest voice;
        VRPointer pointer;
        RetreatEnvironment retreat;
        AssistantBar assistant;
        readonly DashboardData data = new DashboardData();
        readonly DiaryData diary = new DiaryData();
        DateTime diaryDate = DateTime.Now.Date;
        bool refreshing, refreshAgain;

        Transform screen;                // the current screen; replaced as the stage changes
        Transform keyboardRoot;          // the on-screen keyboard (login only)
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
        double pickerServings = 1.0;

        void ClosePicker()
        {
            if (picker != null) Destroy(picker.gameObject);
            picker = null;
            pickerAll = null;
        }

        void ShowPicker(List<CoachAgent.Choice> choices, double servings)
        {
            ClosePicker();
            if (choices == null || choices.Count == 0 || Current == Stage.Gym || Current == Stage.Login) return;
            pickerAll = choices;
            pickerPage = 0;
            pickerServings = servings > 0 ? servings : 1.0;
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
                if (line1.Length > maxChars) line1 = line1.Substring(0, maxChars - 3) + "...";
                string line2 = (c.Kcal.HasValue ? $"{c.Kcal.Value:0} kcal" : "calories not listed") + (string.IsNullOrEmpty(c.Serving) ? "" : $" per {c.Serving}");
                string foodId = c.FoodId;

                int col = two ? i % 2 : 0, row = two ? i / 2 : i;
                float x = two ? (col == 0 ? -0.84f : 0.84f) : 0f;
                VRGlass.Button(picker, line1 + "\n" + line2, new Vector3(x, h / 2 - 0.46f - row * rowH, -0.02f), new Vector2(btnW, 0.2f),
                    GlassStyle.Secondary, two ? 0.0075f : 0.0085f, () => { double amount = pickerServings; ClosePicker(); voice.ChooseFood(foodId, amount); }, 0.04f, 22);
            }

            // Bottom row: how many (left), pages (middle), cancel (right).
            float by = -h / 2 + 0.16f;
            float lx = two ? -1.25f : -0.8f, rx = two ? 1.25f : 0.8f;
            VRGlass.Button(picker, "-", new Vector3(lx - 0.36f, by, -0.02f), new Vector2(0.18f, 0.16f), GlassStyle.Secondary, 0.012f, () => StepServings(-0.5), 0.04f, 22);
            var amountLabel = VRGlass.Label(picker, ServingsText(), new Vector3(lx, by, -0.02f), 0.0085f, VRUi.Ink, order: 24);
            VRGlass.Button(picker, "+", new Vector3(lx + 0.36f, by, -0.02f), new Vector2(0.18f, 0.16f), GlassStyle.Secondary, 0.012f, () => StepServings(0.5), 0.04f, 22);
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

        // Foods listed per 100 g (UK CoFID) are logged as 100 g per serving, so a step of 0.5 is 50 g for those.
        string ServingsText() => $"Amount: {pickerServings:0.#} serving{(pickerServings == 1.0 ? "" : "s")}";

        void StepServings(double delta)
        {
            pickerServings = System.Math.Max(0.5, System.Math.Min(20.0, pickerServings + delta));
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
                case "my_experiences": ShowExperiences(); return true;
                case "virtual_gym": EnterGym(); return true;
                default: return false;
            }
        }

        /// True while the pointer is resting on a button, so voice capture can ignore the trigger.
        public bool PointerOverButton => pointer != null && pointer.HoveringButton;

        Transform NewRoot(Vector3 position)
        {
            if (screen != null) Destroy(screen.gameObject);
            ClosePicker();
            CloseKeyboard();
            statusLabel = null; emailField = null; passwordField = null; showButton = null;

            var go = new GameObject("Screen");
            go.transform.position = position;
            screen = go.transform;
            return screen;
        }

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

            VRGlass.Button(p, "Coach Login\nComing soon", new Vector3(lx - 0.40f, -0.62f, -0.02f), new Vector2(0.74f, 0.46f), GlassStyle.Disabled, 0.0085f, null, 0.05f);
            VRGlass.Button(p, "Gym Login\nComing soon", new Vector3(lx + 0.40f, -0.62f, -0.02f), new Vector2(0.74f, 0.46f), GlassStyle.Disabled, 0.0085f, null, 0.05f);

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
                case Stage.Experiences: BuildExperiences(); break;
            }
        }

        string DisplayName => !string.IsNullOrEmpty(data.Name) ? data.Name : (supabase.DisplayName ?? "there");

        // ── Assistant bar: one persistent control under the dashboards ──────────────────────────────────

        void EnsureAssistant()
        {
            if (assistant == null)
                assistant = AssistantBar.Create(new Vector3(0f, 0.66f, 2.15f), Quaternion.Euler(14f, 0f, 0f));
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
                    assistant.SetHints(new[] { "Log my usual breakfast", "Show me McDonald's foods", "Open my food diary", "What can you do?" });
                    break;
                case Stage.Experiences:
                    assistant.SetHints(new[] { "My knee is sore, build me a workout", "What can you do?" });
                    break;
                default:
                    assistant.SetHints(new[] { "What can you do?", "Log a Big Mac for dinner", "My knee is sore, build me a workout", "Log my usual breakfast" });
                    break;
            }
        }

        // ── Home: greeting, today's summary and four destinations ───────────────────────────────────────

        void ShowHome()
        {
            Current = Stage.Home;
            EnsureAssistant();
            BuildHome();
            StartCoroutine(RefreshData());
        }

        void BuildHome()
        {
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
            if (data.Injuries.Count > 0)
            {
                var n = data.Injuries[0];
                parts.Add($"{Describe(n)} noted");
            }
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
            var p = NewRoot(new Vector3(0f, 1.62f, 2.8f));

            // Navigation rail. It sits far to the user's left, so it is turned to face them; otherwise the buttons,
            // which float in front of the panel, look shifted sideways by perspective.
            var rail = new GameObject("Rail").transform;
            rail.SetParent(p, false);
            rail.localPosition = new Vector3(-1.85f, 0.15f, 0f);
            rail.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(1.85f, 2.8f) * Mathf.Rad2Deg, 0f);
            VRGlass.Panel(rail, "Rail panel", new Vector3(0f, 0f, 0.03f), new Vector2(0.78f, 1.9f), 0.06f, GlassStyle.Panel, 0);
            RailButton(rail, "Home", 0.55f, GlassStyle.Secondary, ShowHome);
            RailButton(rail, "My World", 0.38f, GlassStyle.Primary, null);
            RailButton(rail, "My Community", 0.21f, GlassStyle.Disabled, null);
            RailButton(rail, "My Experiences", 0.04f, GlassStyle.Secondary, ShowExperiences);
            RailButton(rail, "Discover", -0.13f, GlassStyle.Disabled, null);
            RailButton(rail, "Switch account", -0.77f, GlassStyle.Secondary, SwitchAccount);

            VRGlass.Label(p, "My World", new Vector3(-1.4f, 1.02f, -0.02f), 0.02f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(p, "Your wellbeing, connected.", new Vector3(-1.4f, 0.88f, -0.02f), 0.0085f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            // Row 1: profile and weight
            var profile = Card(p, new Vector3(-0.40f, 0.52f, 0f), new Vector2(2.0f, 0.5f));
            VRGlass.Image(profile, "Avatar", VRArt.Orb(new Color(0.20f, 0.75f, 0.80f), new Color(0.20f, 0.75f, 0.80f)), new Vector3(-0.82f, 0f, -0.01f), new Vector2(0.36f, 0.36f), 3);
            VRGlass.Label(profile, Initial(DisplayName), new Vector3(-0.82f, 0f, -0.02f), 0.014f, VRUi.Ink);
            VRGlass.Label(profile, DisplayName, new Vector3(-0.55f, 0.07f, -0.02f), 0.0145f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(profile, "A Better U account", new Vector3(-0.55f, -0.09f, -0.02f), 0.0078f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            var weight = Card(p, new Vector3(1.05f, 0.52f, 0f), new Vector2(0.8f, 0.5f));
            VRGlass.Label(weight, "Weight", new Vector3(0f, 0.13f, -0.02f), 0.0085f, VRUi.Muted);
            VRGlass.Label(weight, data.WeightKg.HasValue ? $"{data.WeightKg.Value:0.#} kg" : (data.Loaded ? "Not logged" : "..."), new Vector3(0f, -0.04f, -0.02f), 0.0155f, VRUi.Ink);

            // Row 2: nutrition, activity, wellbeing notes
            BuildNutritionCard(Card(p, new Vector3(-0.875f, -0.25f, 0f), new Vector2(1.05f, 0.95f)));
            BuildActivityCard(Card(p, new Vector3(0.15f, -0.25f, 0f), new Vector2(0.9f, 0.95f)));
            BuildWellbeingCard(Card(p, new Vector3(1.075f, -0.25f, 0f), new Vector2(0.85f, 0.95f)));
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

        void BuildNutritionCard(Transform c)
        {
            CardTitle(c, VRArt.IconKind.Apple, new Color(0.40f, 0.95f, 0.70f), "Today's Nutrition", 0.37f);
            VRGlass.Button(c, "View Food Diary", new Vector3(0, -0.41f, -0.02f), new Vector2(0.8f, 0.11f), GlassStyle.Secondary, 0.0075f, ShowDiary, 0.035f);

            if (!data.Loaded) { VRGlass.Label(c, "Loading...", new Vector3(0, 0.1f, -0.02f), 0.009f, VRUi.Muted); return; }
            if (data.MealsToday == 0)
            {
                VRGlass.Label(c, "Nothing logged today", new Vector3(0, 0.12f, -0.02f), 0.0095f, VRUi.Ink);
                VRGlass.Label(c, "Tell your coach what you ate\nand it will appear here.", new Vector3(0, -0.02f, -0.02f), 0.0072f, VRUi.Muted);
                return;
            }

            VRGlass.Label(c, $"{data.Kcal:N0}", new Vector3(0, 0.21f, -0.02f), 0.024f, VRUi.Ink);
            bool goal = data.CalorieGoal.HasValue && data.CalorieGoal.Value > 0;
            VRGlass.Label(c, goal ? $"of {data.CalorieGoal.Value:N0} kcal" : "kcal today", new Vector3(0, 0.10f, -0.02f), 0.0082f, VRUi.Muted);
            if (goal) VRGlass.Bar(c, new Vector3(0, 0.02f, -0.02f), 0.88f, 0.045f, (float)(data.Kcal / data.CalorieGoal.Value));

            MacroRow(c, "Protein", data.Protein, -0.09f);
            MacroRow(c, "Carbs", data.Carbs, -0.18f);
            MacroRow(c, "Fat", data.Fat, -0.27f);

            if (!string.IsNullOrEmpty(data.LatestMeal))
                VRGlass.Label(c, VRUi.Wrap(data.LatestMeal, 30, 1), new Vector3(0, -0.34f, -0.02f), 0.0082f, VRUi.Ink);
        }

        static void MacroRow(Transform c, string name, double grams, float y)
        {
            VRGlass.Label(c, name, new Vector3(-0.40f, y, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(c, $"{grams:0} g", new Vector3(0.40f, y, -0.02f), 0.0085f, VRUi.Ink, TextAnchor.MiddleRight, TextAlignment.Right);
        }

        void BuildActivityCard(Transform c)
        {
            CardTitle(c, VRArt.IconKind.Dumbbell, new Color(0.45f, 0.75f, 1.0f), "Today's Activity", 0.37f);

            if (!data.Loaded) { VRGlass.Label(c, "Loading...", new Vector3(0, 0.1f, -0.02f), 0.009f, VRUi.Muted); return; }
            if (data.WorkoutsThisWeek == 0)
            {
                VRGlass.Label(c, "No workouts this week", new Vector3(0, 0.12f, -0.02f), 0.0095f, VRUi.Ink);
                VRGlass.Label(c, "Start one from My Experiences.", new Vector3(0, -0.02f, -0.02f), 0.0072f, VRUi.Muted);
                return;
            }

            VRGlass.Label(c, data.WorkoutsThisWeek.ToString(), new Vector3(0, 0.21f, -0.02f), 0.024f, VRUi.Ink);
            VRGlass.Label(c, data.WorkoutsThisWeek == 1 ? "workout this week" : "workouts this week", new Vector3(0, 0.10f, -0.02f), 0.0082f, VRUi.Muted);

            VRGlass.Label(c, "Latest", new Vector3(-0.38f, -0.06f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(c, VRUi.Wrap(data.LatestWorkout ?? "", 24, 2), new Vector3(-0.38f, -0.2f, -0.02f), 0.0088f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            if (!string.IsNullOrEmpty(data.LatestWorkoutWhen))
                VRGlass.Label(c, data.LatestWorkoutWhen, new Vector3(-0.38f, -0.33f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(c, "Saved to your profile", new Vector3(0, -0.44f, -0.02f), 0.0062f, VRUi.Muted);
        }

        void BuildWellbeingCard(Transform c)
        {
            CardTitle(c, VRArt.IconKind.Heart, new Color(1.0f, 0.50f, 0.60f), "Wellbeing Notes", 0.37f);

            if (!data.Loaded) { VRGlass.Label(c, "Loading...", new Vector3(0, 0.1f, -0.02f), 0.009f, VRUi.Muted); return; }
            if (data.Injuries.Count == 0)
            {
                VRGlass.Label(c, "No notes right now", new Vector3(0, 0.12f, -0.02f), 0.0095f, VRUi.Ink);
                VRGlass.Label(c, "Tell your coach if something\nhurts and it will be kept here.", new Vector3(0, -0.02f, -0.02f), 0.0072f, VRUi.Muted);
                return;
            }

            float y = 0.20f;
            for (int i = 0; i < Mathf.Min(3, data.Injuries.Count); i++)
            {
                var n = data.Injuries[i];
                VRGlass.Label(c, Describe(n), new Vector3(-0.36f, y, -0.02f), 0.0105f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
                string sub = string.IsNullOrEmpty(n.Reported) ? "Reported" : "Reported " + n.Reported.ToLower();
                VRGlass.Label(c, sub, new Vector3(-0.36f, y - 0.075f, -0.02f), 0.0075f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
                y -= 0.20f;
            }
            VRGlass.Label(c, "Used to shape your workouts", new Vector3(0, -0.44f, -0.02f), 0.0062f, VRUi.Muted);
        }

        // ── Food Diary: one day at a time, with previous/next day buttons ───────────────────────────────

        static readonly string[] SlotOrder = { "breakfast", "lunch", "dinner", "snack" };

        void ShowDiary()
        {
            Current = Stage.Diary;
            EnsureAssistant();
            diaryDate = DateTime.Now.Date;
            BuildDiary();
            StartCoroutine(LoadDiary());
        }

        IEnumerator LoadDiary()
        {
            yield return diary.Load(supabase, diaryDate);
            if (Current == Stage.Diary) BuildDiary();
        }

        void GoToDay(int offsetDays)
        {
            if (diary.Loading) return;                                  // finish the current load first
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

        void BuildDiary()
        {
            var p = NewRoot(new Vector3(0f, 1.6f, 2.7f));
            VRGlass.Panel(p, "Panel", new Vector3(0, 0, 0.04f), new Vector2(3.6f, 1.9f), 0.08f, GlassStyle.Panel, 0);

            VRGlass.Label(p, "Food Diary", new Vector3(-1.62f, 0.8f, -0.02f), 0.019f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            bool canGoBack = !diary.Loading;
            bool canGoForward = !diary.Loading && diaryDate < DateTime.Now.Date;
            VRGlass.Button(p, "< Previous", new Vector3(-0.55f, 0.8f, -0.02f), new Vector2(0.5f, 0.14f), canGoBack ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => GoToDay(-1), 0.04f);
            VRGlass.Label(p, DayTitle(diaryDate), new Vector3(0.2f, 0.8f, -0.02f), 0.0145f, VRUi.Ink);
            VRGlass.Button(p, "Next >", new Vector3(0.95f, 0.8f, -0.02f), new Vector2(0.5f, 0.14f), canGoForward ? GlassStyle.Secondary : GlassStyle.Disabled, 0.0085f, () => GoToDay(1), 0.04f);
            VRGlass.Button(p, "My World", new Vector3(1.5f, 0.8f, -0.02f), new Vector2(0.5f, 0.14f), GlassStyle.Secondary, 0.0085f, ShowMyWorld, 0.04f);

            // Left: the entries, grouped by meal
            const float left = -1.62f, right = 0.45f, rowH = 0.095f;
            if (!diary.Loaded)
            {
                VRGlass.Label(p, "Loading...", new Vector3(-0.6f, 0.2f, -0.02f), 0.0105f, VRUi.Muted);
            }
            else if (diary.HadError)
            {
                VRGlass.Label(p, "Couldn't load this day.\nTry again in a moment.", new Vector3(-0.6f, 0.2f, -0.02f), 0.0095f, VRUi.Muted);
            }
            else if (diary.Items.Count == 0)
            {
                VRGlass.Label(p, "Nothing logged on this day", new Vector3(-0.6f, 0.25f, -0.02f), 0.0115f, VRUi.Ink);
                VRGlass.Label(p, "Tell your coach what you ate and it will appear here.", new Vector3(-0.6f, 0.12f, -0.02f), 0.0078f, VRUi.Muted);
            }
            else
            {
                float y = 0.58f;
                int rows = 0;
                const int maxRows = 14;
                foreach (var slot in SlotOrder)
                {
                    double slotKcal = 0;
                    var inSlot = new List<DiaryData.Item>();
                    foreach (var item in diary.Items) if (item.Slot == slot) { inSlot.Add(item); slotKcal += item.Kcal; }
                    if (inSlot.Count == 0) continue;

                    if (rows >= maxRows) break;
                    VRGlass.Label(p, Capitalise(slot), new Vector3(left, y, -0.02f), 0.0092f, Teal, TextAnchor.MiddleLeft, TextAlignment.Left);
                    VRGlass.Label(p, $"{slotKcal:N0} kcal", new Vector3(right, y, -0.02f), 0.0085f, Teal, TextAnchor.MiddleRight, TextAlignment.Right);
                    y -= rowH; rows++;

                    foreach (var item in inSlot)
                    {
                        if (rows >= maxRows) break;
                        VRGlass.Label(p, VRUi.Wrap($"{item.Name}  {item.Amount}", 52, 1), new Vector3(left + 0.05f, y, -0.02f), 0.0082f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
                        VRGlass.Label(p, $"{item.Kcal:N0}", new Vector3(right, y, -0.02f), 0.0082f, VRUi.Muted, TextAnchor.MiddleRight, TextAlignment.Right);
                        y -= rowH; rows++;
                    }
                    y -= 0.03f;
                }
                if (rows >= maxRows) VRGlass.Label(p, "More entries are in the web and phone app.", new Vector3(left, -0.82f, -0.02f), 0.0068f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);
            }

            // Right: the day's totals
            var t = new GameObject("Totals").transform;
            t.SetParent(p, false);
            t.localPosition = new Vector3(1.2f, -0.05f, 0f);
            VRGlass.Panel(t, "Totals panel", new Vector3(0, 0, 0.02f), new Vector2(1.15f, 1.4f), 0.05f, GlassStyle.Card, 1);
            VRGlass.Label(t, "Day total", new Vector3(0, 0.58f, -0.02f), 0.0105f, VRUi.Muted);

            if (!diary.Loaded || diary.HadError || diary.Items.Count == 0)
            {
                VRGlass.Label(t, diary.Loaded && !diary.HadError ? "No entries" : "...", new Vector3(0, 0.2f, -0.02f), 0.012f, VRUi.Muted);
                return;
            }
            VRGlass.Label(t, $"{diary.Kcal:N0}", new Vector3(0, 0.36f, -0.02f), 0.026f, VRUi.Ink);
            bool goal = data.CalorieGoal.HasValue && data.CalorieGoal.Value > 0;
            VRGlass.Label(t, goal ? $"of {data.CalorieGoal.Value:N0} kcal" : "kcal", new Vector3(0, 0.22f, -0.02f), 0.0085f, VRUi.Muted);
            if (goal) VRGlass.Bar(t, new Vector3(0, 0.12f, -0.02f), 0.95f, 0.05f, (float)(diary.Kcal / data.CalorieGoal.Value));
            MacroRow(t, "Protein", diary.Protein, -0.05f);
            MacroRow(t, "Carbs", diary.Carbs, -0.17f);
            MacroRow(t, "Fat", diary.Fat, -0.29f);
            VRGlass.Label(t, $"{diary.Items.Count} item{(diary.Items.Count == 1 ? "" : "s")}", new Vector3(0, -0.52f, -0.02f), 0.0078f, VRUi.Muted);
        }

        // ── My Experiences: the way into the virtual gym ────────────────────────────────────────────────

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

            string tip = data.Injuries.Count > 0
                ? $"You noted: {Describe(data.Injuries[0])}. Ask your coach to build a workout around it."
                : "Ask your coach to build a workout, or just start a set.";
            VRGlass.Label(g, tip, new Vector3(-1.05f, -0.12f, -0.02f), 0.0082f, VRUi.Ink, TextAnchor.MiddleLeft, TextAlignment.Left);
            VRGlass.Label(g, "Left trigger starts and ends a set  ·  the coach saves it for you", new Vector3(-1.05f, -0.26f, -0.02f), 0.0072f, VRUi.Muted, TextAnchor.MiddleLeft, TextAlignment.Left);

            VRGlass.Button(g, "Start Virtual Gym", new Vector3(1.05f, -0.0f, -0.02f), new Vector2(0.85f, 0.2f), GlassStyle.Primary, 0.0095f, EnterGym, 0.05f);
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
            CloseKeyboard();
            if (screen != null) Destroy(screen.gameObject);
            if (assistant != null) { Destroy(assistant.gameObject); assistant = null; }
            if (retreat != null) { retreat.Teardown(); Destroy(retreat); }
            board.SetVisible(true);
            Current = Stage.Gym;
            voice.EnterGym();
        }

        // ── In-gym menu: the left Menu button opens it ──────────────────────────────────────────────────

        void Update()
        {
            if (Current == Stage.Gym && OVRInput.GetDown(OVRInput.Button.Start, OVRInput.Controller.LTouch))
            {
                if (MenuOpen) CloseMenu(); else OpenMenu();
            }
        }

        void OpenMenu()
        {
            var head = Camera.main != null ? Camera.main.transform : transform;
            Vector3 forward = head.forward; forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;

            menuRoot = new GameObject("GymMenu").transform;
            menuRoot.position = new Vector3(head.position.x, Mathf.Max(head.position.y - 0.05f, 1.1f), head.position.z) + forward * 1.3f;
            menuRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);    // its +Z points away from you, so the text faces you

            VRGlass.Panel(menuRoot, "Panel", new Vector3(0, 0, 0.03f), new Vector2(1.3f, 1.2f), 0.06f, GlassStyle.Panel, 0);
            VRGlass.Label(menuRoot, "Menu", new Vector3(0, 0.46f, -0.02f), 0.016f, VRUi.Ink);

            VRGlass.Button(menuRoot, "Resume", new Vector3(0, 0.25f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Primary, 0.010f, CloseMenu);
            VRGlass.Button(menuRoot, "Back to hub", new Vector3(0, 0.05f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, BackToHub);
            VRGlass.Button(menuRoot, "Switch account", new Vector3(0, -0.15f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, LeaveToLogin);
            VRGlass.Button(menuRoot, "Exit app", new Vector3(0, -0.38f, -0.02f), new Vector2(1.0f, 0.16f), GlassStyle.Secondary, 0.009f, Application.Quit);

            pointer = gameObject.AddComponent<VRPointer>();
            pointer.Init(head);
            pointer.PreferLeftHand(true);        // the left Menu button opened this, so that hand is already up
            MenuOpen = true;
        }

        void CloseMenu()
        {
            if (pointer != null) Destroy(pointer);
            if (menuRoot != null) Destroy(menuRoot.gameObject);
            MenuOpen = false;
        }

        // Reloading the scene is the simplest clean reset; the signed-in session survives in the static above.
        void BackToHub() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        void LeaveToLogin()
        {
            supabase.SignOut();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
