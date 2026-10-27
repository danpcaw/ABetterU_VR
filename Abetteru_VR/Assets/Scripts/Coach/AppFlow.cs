using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ABetterU.Coach
{
    /// The app's front door: the retreat environment with a two-column login (Judge Instant Login, or your own
    /// account typed on an on-screen keyboard), a hub, then the gym. Also owns the in-gym menu (left Menu button).
    /// VoiceLoopTest waits for Stage.Gym before it builds the gym and starts the coach.
    public class AppFlow : MonoBehaviour
    {
        public enum Stage { Login, Hub, Gym }
        public Stage Current { get; private set; } = Stage.Login;

        /// True while the in-gym menu is open, so the right trigger clicks menu buttons instead of starting the mic.
        public bool MenuOpen { get; private set; }

        /// The signed-in session lives in a static so it survives a scene reload ("Back to hub" does not ask you to log in again).
        public static SupabaseClient Session;

        static readonly Color Navy = new Color(0.04f, 0.06f, 0.10f);
        static readonly Color Teal = new Color(0.45f, 0.92f, 0.86f);

        SupabaseClient supabase;
        StatusBoard board;
        VRPointer pointer;
        RetreatEnvironment retreat;

        Transform screen;                // the current screen; replaced as the stage changes
        Transform keyboardRoot;          // the on-screen keyboard (login only)
        Transform menuRoot;              // the in-gym menu
        TextMesh statusLabel;
        VRButton emailField, passwordField, showButton, shiftButton;
        readonly List<KeyValuePair<VRButton, char>> letterKeys = new List<KeyValuePair<VRButton, char>>();

        string email = "", password = "";
        bool editingPassword, showPassword, shift, busy;

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

            if (supabase.SignedIn) ShowHub(); else ShowLogin();    // already signed in: we came back from the gym menu
        }

        /// True while the pointer is resting on a button, so voice capture can ignore the trigger.
        public bool PointerOverButton => pointer != null && pointer.HoveringButton;

        Transform NewRoot(Vector3 position)
        {
            if (screen != null) Destroy(screen.gameObject);
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
            var p = NewRoot(new Vector3(0f, 1.55f, 2.6f));

            VRGlass.Panel(p, "Backdrop", new Vector3(0, 0, 0.04f), new Vector2(4.1f, 2.1f), 0.10f, GlassStyle.Panel, 0);

            // Left column: the real logo, the tagline, and two unavailable sign-in tiles.
            var logo = Resources.Load<Texture2D>("Branding/logo");
            if (logo != null) VRGlass.Image(p, "Logo", logo, new Vector3(-1.15f, 0.52f, -0.01f), new Vector2(1.95f, 0.65f));
            else VRGlass.Label(p, "A Better U", new Vector3(-1.15f, 0.52f, -0.02f), 0.024f, VRUi.Ink);

            VRGlass.Label(p, "Mind   ·   Movement   ·   Progress", new Vector3(-1.15f, 0.12f, -0.02f), 0.0105f, Teal);
            VRGlass.Label(p, "Your complete wellness platform\nin virtual and real life.", new Vector3(-1.15f, -0.10f, -0.02f), 0.0085f, VRUi.Ink);

            VRGlass.Button(p, "Coach Login\nComing soon", new Vector3(-1.55f, -0.62f, -0.02f), new Vector2(0.74f, 0.46f), GlassStyle.Disabled, 0.0085f, null, 0.05f);
            VRGlass.Button(p, "Gym Login\nComing soon", new Vector3(-0.75f, -0.62f, -0.02f), new Vector2(0.74f, 0.46f), GlassStyle.Disabled, 0.0085f, null, 0.05f);

            // Right column: the sign-in card.
            const float cx = 1.2f;
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
            ShowHub();
        }

        void Say(string message)
        {
            if (statusLabel != null) statusLabel.text = message;
        }

        // ── Hub (placeholder until the home dashboard milestone): a welcome and the door into the gym ──────

        void ShowHub()
        {
            Current = Stage.Hub;
            var p = NewRoot(new Vector3(0f, 1.5f, 2.4f));

            VRGlass.Panel(p, "Hub panel", new Vector3(0, 0, 0.03f), new Vector2(2.1f, 1.4f), 0.07f, GlassStyle.Panel, 0);
            VRGlass.Label(p, "Welcome,", new Vector3(0, 0.45f, -0.02f), 0.012f, VRUi.Muted);
            VRGlass.Label(p, supabase.DisplayName ?? "athlete", new Vector3(0, 0.30f, -0.02f), 0.024f, VRUi.Ink);
            VRGlass.Label(p, "You are signed in to your A Better U account.\nYour voice coach is ready.", new Vector3(0, 0.08f, -0.02f), 0.0082f, VRUi.Muted);

            VRGlass.Button(p, "Enter the gym", new Vector3(0, -0.20f, -0.02f), new Vector2(1.5f, 0.2f), GlassStyle.Primary, 0.012f, EnterGym);
            VRGlass.Button(p, "Switch account", new Vector3(0, -0.46f, -0.02f), new Vector2(0.9f, 0.14f), GlassStyle.Secondary, 0.0085f, SwitchAccount);
        }

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
            if (retreat != null) { retreat.Teardown(); Destroy(retreat); }
            board.SetVisible(true);
            Current = Stage.Gym;
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
