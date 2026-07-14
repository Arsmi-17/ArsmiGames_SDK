using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArsmiGames.Demo
{
    /// <summary>
    /// The only component the demo scene contains. Camera, canvas, bridge and both panels
    /// are built here at startup, so the scene file stays a single reviewable object
    /// instead of several hundred hand-wired RectTransforms.
    ///
    /// Layout is 1920x1080 with a 0.5 match, and every element is anchored or stretched
    /// rather than placed at fixed pixels — so it holds up in a phone-shaped frame, at
    /// 21:9, and in fullscreen.
    /// </summary>
    [RequireComponent(typeof(ArsmiSave))]
    public class DemoBootstrap : MonoBehaviour
    {
        public static DemoBootstrap Instance { get; private set; }

        [Header("Own-backend save (publish option 3)")]
        [Tooltip("Your own backend. The example talks to Supabase over REST. Leave blank and " +
                 "'Own backend' mode reports that it is not configured.")]
        public string SupabaseUrl = "";

        [Tooltip("The publishable anon key. Safe in a client build, but it means anyone can read " +
                 "and write this table — only ever point it at a demo table. " +
                 "See supabase/demo_quiz_backend_schema.sql.")]
        public string SupabaseAnonKey = "";

        public string SupabaseTable = "game_platform_demo_quiz_saves";

        public ArsmiBackendClient Backend { get; private set; }

        private void Awake()
        {
            Instance = this;

            Backend = new ArsmiBackendClient
            {
                Url = SupabaseUrl,
                AnonKey = SupabaseAnonKey,
                Table = SupabaseTable,
            };

            EnsureFont();
            EnsureCamera();
            EnsureEventSystem();

            // Must exist before anything asks it a question: its Awake calls into the
            // .jslib, which is what introduces the game to the platform.
            if (GameHubBridge.Instance == null)
            {
                new GameObject("GameHubBridge").AddComponent<GameHubBridge>();
            }

            BuildUI();
        }

        /// <summary>
        /// TextMeshPro renders nothing at all without a default font asset, and the failure
        /// looks exactly like a broken layout rather than a missing resource. Fail loudly.
        /// </summary>
        private static void EnsureFont()
        {
            if (TMP_Settings.defaultFontAsset != null) return;
            Debug.LogError(
                "[Arsmi] TextMeshPro has no default font asset — every label will be invisible. " +
                "Run Arsmi Games -> Import TextMeshPro Essentials.");
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = DemoUI.Bg;
            camera.orthographic = true;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            // The project runs with "Both" input handling, so the legacy module is valid and
            // avoids a hard dependency on an Input Actions asset.
            go.AddComponent<StandaloneInputModule>();
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // 0.5 — weight width and height equally, so a wider *or* taller frame both
            // scale the UI rather than cropping it.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var background = DemoUI.Node("Background", canvasGo.transform);
            DemoUI.Box(background, DemoUI.Bg);
            DemoUI.Fill(background);

            // A soft accent wash at the top, so the screen is not a flat black rectangle.
            var glow = DemoUI.Node("Glow", canvasGo.transform);
            DemoUI.Box(glow, new Color(DemoUI.Accent.r, DemoUI.Accent.g, DemoUI.Accent.b, 0.05f));
            DemoUI.Anchor(glow, new Vector2(0f, 0.86f), new Vector2(1f, 1f));

            BuildHeader(canvasGo.transform);

            // Two columns, anchored as fractions of the frame: the quiz (a real game using
            // the SDK) on the left, the raw function surface on the right.
            var left = DemoUI.Node("QuizPanel", canvasGo.transform);
            DemoUI.Anchor(left, new Vector2(0f, 0f), new Vector2(0.5f, 0.86f), new Vector4(28, 24, 14, 12));

            var right = DemoUI.Node("SdkPanel", canvasGo.transform);
            DemoUI.Anchor(right, new Vector2(0.5f, 0f), new Vector2(1f, 0.86f), new Vector4(14, 24, 28, 12));

            var save = GetComponent<ArsmiSave>();
            var console = gameObject.AddComponent<SdkFunctionPanel>();
            var quiz = gameObject.AddComponent<KidsQuiz>();

            quiz.Build(left.transform, save, console);
            console.Build(right.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var header = DemoUI.Node("Header", parent);
            DemoUI.Anchor(header, new Vector2(0f, 0.86f), new Vector2(1f, 1f), new Vector4(28, 10, 28, 14));

            var title = DemoUI.Label(header.transform, "ARSMI GAMES", 40, DemoUI.Text,
                                     TextAlignmentOptions.TopLeft, FontStyles.Bold);
            title.characterSpacing = 8f;
            DemoUI.Anchor(title.gameObject, new Vector2(0f, 0.42f), new Vector2(0.7f, 1f));

            var subtitle = DemoUI.Label(header.transform,
                "SDK demo — every bridge call, and a quiz that really saves your progress.",
                18, DemoUI.Muted, TextAlignmentOptions.TopLeft);
            DemoUI.Anchor(subtitle.gameObject, new Vector2(0f, 0f), new Vector2(0.7f, 0.42f));

            // Live connection state, so "is the SDK even there?" is answered at a glance.
            var status = DemoUI.Node("Status", header.transform);
            DemoUI.Anchor(status, new Vector2(0.7f, 0.15f), new Vector2(1f, 0.85f));
            var row = status.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childAlignment = TextAnchor.MiddleRight;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            gameObject.AddComponent<DemoStatusChips>().Build(status.transform);
        }
    }

    /// <summary>Header chips: bridge connected, save mode, signed-in player.</summary>
    public class DemoStatusChips : MonoBehaviour
    {
        private TextMeshProUGUI _bridge;
        private TextMeshProUGUI _player;

        public void Build(Transform parent)
        {
            // Chip() returns the label; the chip itself is its parent, and that is what the
            // layout group measures.
            _bridge = DemoUI.Chip(parent, "BRIDGE …", DemoUI.Faint);
            _bridge.transform.parent.gameObject.AddComponent<LayoutElement>().minWidth = 150;

            _player = DemoUI.Chip(parent, "GUEST", DemoUI.Faint);
            _player.transform.parent.gameObject.AddComponent<LayoutElement>().minWidth = 220;

            var hub = GameHubBridge.Instance;
            if (hub == null) return;
            hub.OnUserChanged += Refresh;
            hub.OnDataChanged += Refresh;
            Refresh();
        }

        private void Refresh()
        {
            var hub = GameHubBridge.Instance;
            if (hub == null) return;

            var connected = hub.DataReady || hub.LoggedIn;
            SetChip(_bridge, connected ? "BRIDGE OK" : "BRIDGE …", connected ? DemoUI.Good : DemoUI.Faint);
            SetChip(_player,
                    hub.LoggedIn ? (hub.DisplayName ?? "PLAYER").ToUpperInvariant() : "GUEST",
                    hub.LoggedIn ? DemoUI.Accent : DemoUI.Faint);
        }

        private static void SetChip(TextMeshProUGUI label, string text, Color color)
        {
            if (label == null) return;
            label.text = text;
            label.color = color;
            var bg = label.transform.parent.GetComponent<Image>();
            if (bg != null) bg.color = new Color(color.r, color.g, color.b, 0.14f);
        }
    }
}
