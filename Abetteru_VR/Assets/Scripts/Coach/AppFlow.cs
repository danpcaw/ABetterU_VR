using System.Collections;
using UnityEngine;

namespace ABetterU.Coach
{
    /// The app's front door: a lobby with a login screen (Judge Instant Login, or your own account) and a hub,
    /// then the gym. VoiceLoopTest waits for Stage.Gym before it builds the gym and starts the coach.
    public class AppFlow : MonoBehaviour
    {
        public enum Stage { Login, Hub, Gym }
        public Stage Current { get; private set; } = Stage.Login;

        static readonly Color Navy = new Color(0.04f, 0.06f, 0.10f);

        ModelApiConfig config;
        SupabaseClient supabase;
        StatusBoard board;
        VRPointer pointer;

        Transform lobby;                 // floor and mat; removed when entering the gym
        Transform screen;                // the current panel; replaced as the stage changes
        TextMesh statusLabel;
        VRButton emailField, passwordField;

        string email = "", password = "";
        TouchScreenKeyboard keyboard;
        bool editingPassword;
        bool busy;

        public void Init(ModelApiConfig config, SupabaseClient supabase, StatusBoard board)
        {
            this.config = config;
            this.supabase = supabase;
            this.board = board;

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
            ShowLogin();
        }

        // ── Lobby: a dark floor with a teal mat where you stand ─────────────────

        void BuildLobby()
        {
            lobby = new GameObject("Lobby").transform;
            VRUi.Box(lobby, "Lobby floor", new Vector3(0, -0.05f, 0), new Vector3(10f, 0.1f, 10f), new Color(0.06f, 0.08f, 0.11f));
            VRUi.Box(lobby, "Lobby mat", new Vector3(0, 0.005f, 0), new Vector3(1.4f, 0.01f, 1.4f), VRUi.Teal);
        }

        Transform NewScreen()
        {
            if (screen != null) Destroy(screen.gameObject);
            statusLabel = null; emailField = null; passwordField = null;
            var go = new GameObject("Screen");
            go.transform.position = new Vector3(0f, 1.45f, 2.2f);   // in front of you, at about eye height
            screen = go.transform;

            VRUi.Box(screen, "Panel", new Vector3(0, 0, 0.03f), new Vector3(1.9f, 1.3f, 0.04f), VRUi.PanelBg);
            VRUi.Box(screen, "Accent", new Vector3(0, 0.62f, 0f), new Vector3(1.9f, 0.03f, 0.02f), VRUi.Teal);
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

        void ShowAccountForm()
        {
            var p = NewScreen();
            VRUi.Label(p, "Sign in", new Vector3(0, 0.42f, -0.02f), 0.016f, VRUi.Teal);
            VRUi.Label(p, "Point and pull the trigger on a field to type with the Quest keyboard", new Vector3(0, 0.30f, -0.02f), 0.0062f, VRUi.Muted);

            emailField = VRUi.Button(p, "", new Vector3(0, 0.14f, -0.02f), new Vector2(1.5f, 0.17f), VRUi.Slate, VRUi.SlateHover, 0.009f, () => OpenKeyboard(false));
            passwordField = VRUi.Button(p, "", new Vector3(0, -0.07f, -0.02f), new Vector2(1.5f, 0.17f), VRUi.Slate, VRUi.SlateHover, 0.009f, () => OpenKeyboard(true));
            VRUi.Button(p, "Sign in", new Vector3(0, -0.30f, -0.02f), new Vector2(1.5f, 0.17f), VRUi.Teal, VRUi.TealHover, 0.011f, () => StartCoroutine(Login(judge: false)));
            VRUi.Button(p, "Back", new Vector3(0, -0.50f, -0.02f), new Vector2(0.6f, 0.12f), VRUi.Slate, VRUi.SlateHover, 0.008f, ShowLogin);

            statusLabel = VRUi.Label(p, "", new Vector3(0, -0.60f, -0.02f), 0.0065f, VRUi.Muted);
            RefreshFields();
        }

        void OpenKeyboard(bool forPassword)
        {
            editingPassword = forPassword;
            if (!TouchScreenKeyboard.isSupported) { Say("No system keyboard on this device."); return; }
            keyboard = TouchScreenKeyboard.Open(forPassword ? password : email,
                forPassword ? TouchScreenKeyboardType.Default : TouchScreenKeyboardType.EmailAddress,
                false, false, forPassword);
        }

        void RefreshFields()
        {
            if (emailField != null) emailField.SetLabel(email.Length > 0 ? email : "Tap to enter your email");
            if (passwordField != null) passwordField.SetLabel(password.Length > 0 ? new string('*', password.Length) : "Tap to enter your password");
        }

        void Update()
        {
            if (keyboard == null) return;

            // Mirror what is being typed into the field, and stop listening when the keyboard closes.
            string live = keyboard.text ?? "";
            if (editingPassword) password = live; else email = live;
            RefreshFields();
            if (keyboard.status != TouchScreenKeyboard.Status.Visible) keyboard = null;
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
    }
}
