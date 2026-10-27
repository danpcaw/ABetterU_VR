using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ABetterU.Coach
{
    /// The app's front door: a lobby with a login screen (Judge Instant Login, or your own account typed on an
    /// on-screen keyboard) and a hub, then the gym. Also owns the in-gym menu (left Menu button).
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

        SupabaseClient supabase;
        StatusBoard board;
        VRPointer pointer;

        Transform lobby;                 // floor and mat; removed when entering the gym
        Transform screen;                // the current panel; replaced as the stage changes
        Transform menuRoot;
        TextMesh statusLabel;
        VRButton emailField, passwordField, shiftButton;
        readonly List<KeyValuePair<VRButton, char>> letterKeys = new List<KeyValuePair<VRButton, char>>();

        string email = "", password = "";
        bool editingPassword;
        bool shift;
        bool busy;

        public void Init(SupabaseClient supabase, StatusBoard board)
        {
            this.supabase = supabase;
            this.board = board;

            // Height must be real before anything is placed: the rig defaults to EyeLevel, which put the login panel
            // in the air and the floor under the user's feet (found on-device Oct 27).
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

            BuildLobby();
            pointer = gameObject.AddComponent<VRPointer>();
            pointer.Init(cam != null ? cam.transform : transform);

            if (supabase.SignedIn) ShowHub(); else ShowLogin();    // already signed in: we came back from the gym menu
        }

        // ── Lobby: a dark floor with a teal mat where you stand ─────────────────

        void BuildLobby()
        {
            lobby = new GameObject("Lobby").transform;
            VRUi.Box(lobby, "Lobby floor", new Vector3(0, -0.05f, 0), new Vector3(10f, 0.1f, 10f), new Color(0.06f, 0.08f, 0.11f));
            VRUi.Box(lobby, "Lobby mat", new Vector3(0, 0.005f, 0), new Vector3(1.4f, 0.01f, 1.4f), VRUi.Teal);
        }

        Transform NewScreen(float height = 1.3f)
        {
            if (screen != null) Destroy(screen.gameObject);
            statusLabel = null; emailField = null; passwordField = null; shiftButton = null;
            letterKeys.Clear();

            var go = new GameObject("Screen");
            go.transform.position = new Vector3(0f, 1.45f, 2.2f);   // in front of you, at about eye height
            screen = go.transform;

            VRUi.Box(screen, "Panel", new Vector3(0, 0, 0.03f), new Vector3(1.9f, height, 0.04f), VRUi.PanelBg);
            VRUi.Box(screen, "Accent", new Vector3(0, height / 2f - 0.03f, 0f), new Vector3(1.9f, 0.03f, 0.02f), VRUi.Teal);
            return screen;
        }

        // ── Login screen ─────────────────────────────────────────────────────────

        void ShowLogin()
        {
            Current = Stage.Login;
            var p = NewScreen();

            VRUi.Label(p, "A BETTER U", new Vector3(0, 0.40f, -0.02f), 0.022f, VRUi.Teal);
            VRUi.Label(p, "VR COACH", new Vector3(0, 0.26f, -0.02f), 0.011f, VRUi.Ink);
            VRUi.Label(p, "A voice agent that acts on your real\nA Better U account", new Vector3(0, 0.12f, -0.02f), 0.0075f, VRUi.Muted);

            VRUi.Button(p, "Judge Instant Login", new Vector3(0, -0.10f, -0.02f), new Vector2(1.5f, 0.22f),
                VRUi.Teal, VRUi.TealHover, 0.011f, () => StartCoroutine(Login(judge: true)));
            VRUi.Button(p, "Sign in with your account", new Vector3(0, -0.38f, -0.02f), new Vector2(1.5f, 0.18f),
                VRUi.Slate, VRUi.SlateHover, 0.009f, ShowAccountForm);

            statusLabel = VRUi.Label(p, "", new Vector3(0, -0.57f, -0.02f), 0.0065f, VRUi.Muted);
        }

        // Sign-in form with our own on-screen keyboard (the Quest system keyboard did not open reliably).
        void ShowAccountForm()
        {
            var p = NewScreen(height: 2.0f);
            VRUi.Label(p, "Sign in", new Vector3(0, 0.86f, -0.02f), 0.016f, VRUi.Teal);

            emailField = VRUi.Button(p, "", new Vector3(0, 0.66f, -0.02f), new Vector2(1.6f, 0.15f), VRUi.Slate, VRUi.SlateHover, 0.009f, () => { editingPassword = false; RefreshFields(); });
            passwordField = VRUi.Button(p, "", new Vector3(0, 0.47f, -0.02f), new Vector2(1.6f, 0.15f), VRUi.Slate, VRUi.SlateHover, 0.009f, () => { editingPassword = true; RefreshFields(); });

            BuildKeyboard(p, 0.27f);

            VRUi.Button(p, "Sign in", new Vector3(-0.33f, -0.50f, -0.02f), new Vector2(0.9f, 0.16f), VRUi.Teal, VRUi.TealHover, 0.011f, () => StartCoroutine(Login(judge: false)));
            VRUi.Button(p, "Back", new Vector3(0.55f, -0.50f, -0.02f), new Vector2(0.5f, 0.16f), VRUi.Slate, VRUi.SlateHover, 0.009f, ShowLogin);

            statusLabel = VRUi.Label(p, "", new Vector3(0, -0.70f, -0.02f), 0.0065f, VRUi.Muted);
            editingPassword = false;
            RefreshFields();
        }

        void BuildKeyboard(Transform p, float topY)
        {
            string[] rows = { "1234567890", "qwertyuiop", "asdfghjkl@", "zxcvbnm._-" };
            const float step = 0.15f, size = 0.13f;

            for (int r = 0; r < rows.Length; r++)
            {
                float y = topY - r * step;
                float x0 = -(rows[r].Length - 1) * step / 2f;
                for (int i = 0; i < rows[r].Length; i++)
                {
                    char key = rows[r][i];                       // local copy so each button keeps its own character
                    var button = VRUi.Button(p, key.ToString(), new Vector3(x0 + i * step, y, -0.02f), new Vector2(size, size),
                        VRUi.Slate, VRUi.SlateHover, 0.009f, () => TypeChar(key));
                    if (char.IsLetter(key)) letterKeys.Add(new KeyValuePair<VRButton, char>(button, key));
                }
            }

            float bottom = topY - rows.Length * step;
            shiftButton = VRUi.Button(p, "Shift", new Vector3(-0.55f, bottom, -0.02f), new Vector2(0.45f, size), VRUi.Slate, VRUi.SlateHover, 0.008f, () => { shift = !shift; RefreshKeys(); });
            VRUi.Button(p, "Delete", new Vector3(0.55f, bottom, -0.02f), new Vector2(0.45f, size), VRUi.Slate, VRUi.SlateHover, 0.008f, Backspace);
            RefreshKeys();
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
                passwordField.SetLabel((editingPassword ? "> " : "  ") + (password.Length > 0 ? new string('*', password.Length) : "Password"));
        }

        IEnumerator Login(bool judge)
        {
            if (busy) yield break;
            busy = true;
            Say(judge ? "Signing in as the judge..." : "Signing in...");

            string error = null;
            if (judge) yield return supabase.SignInJudge(e => error = e);
            else yield return supabase.SignIn(email, password, e => error = e);
            busy = false;

            if (error != null)
            {
                Debug.LogError("[Login] " + error);
                Say(error.Length > 90 ? error.Substring(0, 90) + "..." : error);
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

        // ── Hub (milestone 1: a welcome and the door into the gym; the live dashboard comes next) ──

        void ShowHub()
        {
            Current = Stage.Hub;
            var p = NewScreen();

            VRUi.Label(p, "Welcome,", new Vector3(0, 0.40f, -0.02f), 0.012f, VRUi.Muted);
            VRUi.Label(p, supabase.DisplayName ?? "athlete", new Vector3(0, 0.26f, -0.02f), 0.022f, VRUi.Teal);
            VRUi.Label(p, "You are signed in to your A Better U account.\nYour voice coach is ready.", new Vector3(0, 0.08f, -0.02f), 0.0075f, VRUi.Muted);

            VRUi.Button(p, "Enter the gym", new Vector3(0, -0.20f, -0.02f), new Vector2(1.5f, 0.22f), VRUi.Teal, VRUi.TealHover, 0.012f, EnterGym);
            VRUi.Button(p, "Switch account", new Vector3(0, -0.46f, -0.02f), new Vector2(0.9f, 0.14f), VRUi.Slate, VRUi.SlateHover, 0.008f, SwitchAccount);
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
            if (screen != null) Destroy(screen.gameObject);
            if (lobby != null) Destroy(lobby.gameObject);
            board.SetVisible(true);
            Current = Stage.Gym;
        }

        // ── In-gym menu: the left Menu button opens it ──────────────────────────

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

            VRUi.Box(menuRoot, "Panel", new Vector3(0, 0, 0.03f), new Vector3(1.3f, 1.2f, 0.04f), VRUi.PanelBg);
            VRUi.Box(menuRoot, "Accent", new Vector3(0, 0.57f, 0f), new Vector3(1.3f, 0.03f, 0.02f), VRUi.Teal);
            VRUi.Label(menuRoot, "Menu", new Vector3(0, 0.45f, -0.02f), 0.016f, VRUi.Teal);

            VRUi.Button(menuRoot, "Resume", new Vector3(0, 0.25f, -0.02f), new Vector2(1.0f, 0.15f), VRUi.Teal, VRUi.TealHover, 0.010f, CloseMenu);
            VRUi.Button(menuRoot, "Back to hub", new Vector3(0, 0.05f, -0.02f), new Vector2(1.0f, 0.15f), VRUi.Slate, VRUi.SlateHover, 0.009f, BackToHub);
            VRUi.Button(menuRoot, "Switch account", new Vector3(0, -0.15f, -0.02f), new Vector2(1.0f, 0.15f), VRUi.Slate, VRUi.SlateHover, 0.009f, LeaveToLogin);
            VRUi.Button(menuRoot, "Exit app", new Vector3(0, -0.38f, -0.02f), new Vector2(1.0f, 0.15f), VRUi.Slate, VRUi.SlateHover, 0.009f, Application.Quit);

            pointer = gameObject.AddComponent<VRPointer>();
            pointer.Init(head);
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
