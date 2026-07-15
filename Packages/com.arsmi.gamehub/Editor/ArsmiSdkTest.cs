using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ArsmiGames.EditorTools
{
    /// <summary>
    /// The SDK Assessment, run inside Unity, before the developer ever uploads.
    ///
    /// The platform decides whether a game is publishable by RUNNING it and driving the bridge
    /// protocol at it — it sends a real set_mute and waits to hear the game's own code answer.
    /// Silence is never a pass, because a game that ignores the volume button and a game that
    /// honours it send nothing different from the outside; only the game's own handler answering
    /// tells them apart.
    ///
    /// That verdict cannot be reproduced by reading code. A subscription to OnMuteChanged only
    /// exists once the game's Start() has run — at edit time there is nothing to see. So this
    /// tool does what the platform does: it enters Play Mode with the developer's own scene, lets
    /// the game wire itself up, then acts as the platform — reads what the game actually
    /// subscribed to, and fires set_mute / set_fullscreen at the live bridge to confirm the
    /// handlers run without throwing.
    ///
    /// The checks below are the same ones lib/sdkCompliance.ts enforces on upload, so a game that
    /// passes here passes there. The point is to find that out in the Editor, in seconds, rather
    /// than from a rejection after a build-and-upload round trip.
    /// </summary>
    public class ArsmiSdkTestWindow : EditorWindow
    {
        // What the developer intends to publish as. The save requirement is different for each,
        // exactly as it is in the upload wizard, so we have to ask rather than guess.
        private enum SaveMode { Platform, OwnBackend, NoSave }

        private enum Status { Pass, Fail, Optional, Info }

        private struct Result
        {
            public Status Status;
            public string Label;
            public string Detail;
            public string Fix;      // shown only on Fail
            public bool Required;   // counts toward the verdict
        }

        private const string TemplateName = "PROJECT:ArsmiGames";

        private SaveMode _saveMode = SaveMode.Platform;
        private readonly List<Result> _static = new List<Result>();
        private readonly List<Result> _live = new List<Result>();
        private bool _ran;
        private bool _liveRan;
        private Vector2 _scroll;

        // The "Enter Play Mode & Test" flow crosses a domain reload — entering Play Mode reloads
        // the scripting domain, which wipes every non-serialized field on this window. So the
        // intent to test (and the chosen save mode) is parked in SessionState, which survives the
        // reload, and picked back up by Tick once Play Mode is live. Without this the auto-test
        // would silently do nothing: the window would come back from the reload having forgotten
        // it was mid-test.
        private const string PendingKey = "Arsmi.SdkTest.Pending";
        private const string FramesKey = "Arsmi.SdkTest.Frames";
        private const string SaveModeKey = "Arsmi.SdkTest.SaveMode";
        private const int MaxWaitFrames = 300; // ~5s at 60fps — a Unity build can take that to boot

        private static bool Pending
        {
            get => SessionState.GetBool(PendingKey, false);
            set => SessionState.SetBool(PendingKey, value);
        }

        [MenuItem("Arsmi Games/Test SDK Integration", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<ArsmiSdkTestWindow>("SDK Test");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        private void OnEnable()
        {
            _saveMode = (SaveMode)SessionState.GetInt(SaveModeKey, (int)SaveMode.Platform);
            EditorApplication.update += Tick;
        }

        private void OnDisable() => EditorApplication.update -= Tick;

        // Runs every editor frame, but does nothing unless an auto-test is pending. It resumes the
        // test after the play-mode domain reload, waits (on a frame budget) for the game to create
        // its bridge, then assesses.
        private void Tick()
        {
            if (!Pending) return;

            if (!EditorApplication.isPlaying)
            {
                // Mid-transition into Play Mode, or the user bailed out. Give it a budget.
                if (BumpFrames() > MaxWaitFrames) Finish(null, entered: false);
                return;
            }

            var bridge = FindBridge();
            if (bridge != null) { Finish(bridge, entered: true); return; }
            if (BumpFrames() > MaxWaitFrames) Finish(null, entered: true);
        }

        private static int BumpFrames()
        {
            var next = SessionState.GetInt(FramesKey, 0) + 1;
            SessionState.SetInt(FramesKey, next);
            return next;
        }

        // The single place the auto-test lands, after the reload. Rebuilds the whole report from
        // scratch (static too — its lists were wiped by the reload) so what the window shows is
        // always internally consistent.
        private void Finish(GameHubBridge bridge, bool entered)
        {
            Pending = false;
            RunStatic();

            if (bridge != null)
            {
                RunLive(bridge);
            }
            else
            {
                _live.Clear();
                _live.Add(new Result
                {
                    Status = Status.Fail,
                    Required = true,
                    Label = "SDK connected",
                    Detail = entered
                        ? "Play Mode started but no GameHubBridge appeared within " + (MaxWaitFrames / 60) + "s."
                        : "Could not enter Play Mode to run the live checks.",
                    Fix = "Add a GameHubBridge to your first scene (or create it in code before anything uses it, " +
                          "as the sample's DemoBootstrap does). Without it every SDK call is a silent no-op.",
                });
                _liveRan = true;
            }

            _ran = true;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("SDK Integration Test", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The same checks the platform runs on upload. Passing here means passing there.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Publishing as", GUILayout.Width(90));
                var picked = (SaveMode)EditorGUILayout.EnumPopup(_saveMode);
                if (picked != _saveMode)
                {
                    _saveMode = picked;
                    SessionState.SetInt(SaveModeKey, (int)_saveMode); // survive the play-mode reload
                }
            }
            EditorGUILayout.LabelField(SaveModeHint(_saveMode), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EditorApplication.isPlaying ? "Run Test" : "Run Static Checks", GUILayout.Height(26)))
                    Run();

                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || Pending))
                {
                    if (GUILayout.Button("Enter Play Mode & Test", GUILayout.Height(26)))
                        EnterPlayAndTest();
                }
            }

            if (Pending)
                EditorGUILayout.HelpBox("Entering Play Mode and waiting for the bridge…", MessageType.Info);

            if (!_ran)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "Static checks (build settings, SDK version) run without Play Mode.\n\n" +
                    "The live checks — mute, fullscreen, save, identity — can only be proven with the game " +
                    "running, because a game subscribes to those events at runtime. Use \"Enter Play Mode & " +
                    "Test\", or enter Play Mode yourself and press Run Test.",
                    MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawVerdict();
            DrawSection("Build settings", _static);
            DrawSection("Live protocol", _live,
                emptyNote: EditorApplication.isPlaying
                    ? null
                    : "Not run — enter Play Mode to assess mute, fullscreen, save and identity.");

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(!_ran))
            {
                if (GUILayout.Button("Save report…"))
                    SaveReport();
            }
        }

        // ---- Running -------------------------------------------------------------

        private void Run()
        {
            RunStatic();
            if (EditorApplication.isPlaying)
            {
                var bridge = FindBridge();
                if (bridge != null) RunLive(bridge);
                else
                {
                    _live.Clear();
                    _live.Add(new Result
                    {
                        Status = Status.Fail,
                        Required = true,
                        Label = "SDK connected",
                        Detail = "In Play Mode, but no GameHubBridge is in the scene.",
                        Fix = "Add a GameHubBridge to your first scene, or create it in code before anything uses it.",
                    });
                    _liveRan = true;
                }
            }
            _ran = true;
            Repaint();
        }

        private void EnterPlayAndTest()
        {
            // Park the intent in SessionState and enter Play Mode. The domain reload that follows
            // wipes this window's fields, so the actual assessment happens in Tick → Finish once
            // Play Mode is live and the bridge exists.
            SessionState.SetInt(FramesKey, 0);
            SessionState.SetInt(SaveModeKey, (int)_saveMode);
            Pending = true;
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        private void RunStatic()
        {
            _static.Clear();

            _static.Add(PassFail(
                PlayerSettings.WebGL.template == TemplateName,
                required: true,
                label: "WebGL template",
                pass: "Set to ArsmiGames.",
                failDetail: "The active WebGL template is \"" + PlayerSettings.WebGL.template + "\", not ArsmiGames.",
                fix: "Player Settings → WebGL → Resolution and Presentation → WebGL Template → ArsmiGames. " +
                     "The Arsmi build also forces this, but setting it now makes File → Build work too."));

            _static.Add(PassFail(
                PlayerSettings.WebGL.decompressionFallback,
                required: true,
                label: "Decompression fallback",
                pass: "On.",
                failDetail: "Off. The platform serves the build as static files with no Content-Encoding, so a " +
                            "compressed build simply fails to load.",
                fix: "Player Settings → WebGL → Publishing Settings → Decompression Fallback. The Arsmi build forces this too."));

            _static.Add(PassFail(
                PlayerSettings.runInBackground,
                required: true,
                label: "Run In Background",
                pass: "On.",
                failDetail: "Off. The game lives in an iframe with platform chrome; clicking that chrome takes focus " +
                            "off the canvas and Unity stops rendering until the player clicks back.",
                fix: "Player Settings → Resolution and Presentation → Run In Background."));

            _static.Add(SdkVersionCheck());
        }

        private void RunLive(GameHubBridge bridge)
        {
            _live.Clear();

            _live.Add(new Result
            {
                Status = Status.Pass, Required = true, Label = "SDK connected",
                Detail = "A GameHubBridge is live in the scene.",
            });

            _live.Add(InboundCheck(
                bridge, "Mute", "OnMuteChanged",
                get: () => bridge.IsMuted,
                set: muted => bridge.OnGameHubMuted("{\"muted\":" + (muted ? "true" : "false") + "}"),
                cost: "The platform's volume button will do nothing — the player mutes and keeps hearing the game.",
                fix: "Subscribe in Start(): GameHubBridge.Instance.OnMuteChanged += (muted, fromPlatform) => " +
                     "AudioListener.volume = muted ? 0 : 1;  — and silence every channel, not just some."));

            _live.Add(InboundCheck(
                bridge, "Fullscreen", "OnFullscreenChanged",
                get: () => bridge.IsFullscreen,
                set: full => bridge.OnGameHubFullscreen("{\"fullscreen\":" + (full ? "true" : "false") + "}"),
                cost: "The platform's fullscreen button resizes the frame and the game will not know, so it can " +
                      "render at the wrong size or crop its own UI.",
                fix: "Subscribe in Start(): GameHubBridge.Instance.OnFullscreenChanged += fullscreen => { … };  " +
                     "an empty handler is enough to acknowledge it."));

            _live.Add(SaveCheck(bridge));

            // Optional — reported so the developer can see what the platform will see, but never
            // blocking. A game with no wallet, ads or leaderboard is a perfectly publishable game.
            _live.Add(Optional("Wallet (Flux)", Wired(bridge, "OnWalletChanged"),
                "Reads the Flux balance (OnWalletChanged). Only needed if you sell things for Flux."));
            _live.Add(Optional("Rewarded ads", Wired(bridge, "OnAdFinished"),
                "Handles ad results (OnAdFinished). Only needed if you show rewarded ads."));
            _live.Add(Optional("Leaderboard", PrivateBool(bridge, "_definedLeaderboard"),
                "Has submitted a score this session. Only needed if your game ranks players."));

            _liveRan = true;
        }

        // ---- Individual checks ---------------------------------------------------

        private Result SdkVersionCheck()
        {
            var installed = ArsmiTemplateInstaller.InstalledFile("gamehub-sdk.js");
            var canonical = ArsmiTemplateInstaller.PackageFile("gamehub-sdk.js");

            if (installed == null || canonical == null)
            {
                return new Result
                {
                    Status = Status.Fail, Required = true, Label = "SDK version",
                    Detail = "Could not find the SDK to compare — the template may not be installed.",
                    Fix = "Run Arsmi Games → Reinstall WebGL template.",
                };
            }

            var have = ReadSdkVersion(installed);
            var want = ReadSdkVersion(canonical);

            if (have == want)
                return new Result { Status = Status.Pass, Required = true, Label = "SDK version", Detail = "Current (" + have + ")." };

            return new Result
            {
                Status = Status.Fail, Required = true, Label = "SDK version",
                Detail = "The template ships SDK " + have + ", but the package is on " + want + ".",
                Fix = "Run Arsmi Games → Reinstall WebGL template. An out-of-date SDK answers the handshake but " +
                      "cannot report newer checks, and the platform will not publish a game it cannot verify.",
            };
        }

        private Result InboundCheck(GameHubBridge bridge, string label, string eventField,
            Func<bool> get, Action<bool> set, string cost, string fix)
        {
            var wired = Wired(bridge, eventField);
            if (!wired)
            {
                return new Result
                {
                    Status = Status.Fail, Required = true, Label = label + " handled",
                    Detail = "The game is not listening — nothing is subscribed to " + eventField + ". " + cost,
                    Fix = fix,
                };
            }

            // Live proof: drive the real handler through a full toggle and back, and make sure it
            // does not throw. A handler that throws did not handle anything, and the platform
            // reports it exactly that way. We restore the original state so the test leaves the
            // game as it found it.
            try
            {
                var original = get();
                set(!original);   // force a change so the handler actually runs
                set(original);    // put it back
            }
            catch (Exception e)
            {
                return new Result
                {
                    Status = Status.Fail, Required = true, Label = label + " handled",
                    Detail = "The game is subscribed to " + eventField + ", but its handler threw when the platform " +
                             "sent the message: " + e.GetType().Name + " — " + e.Message,
                    Fix = "Fix the exception in your " + eventField + " handler. A throwing handler is a handler that " +
                          "does nothing, from the platform's point of view.",
                };
            }

            return new Result
            {
                Status = Status.Pass, Required = true, Label = label + " handled",
                Detail = "Subscribed to " + eventField + ", and the handler ran without error.",
            };
        }

        private Result SaveCheck(GameHubBridge bridge)
        {
            switch (_saveMode)
            {
                case SaveMode.Platform:
                    var dataWired = Wired(bridge, "OnDataChanged") || PrivateBool(bridge, "_usedSaveApi");
                    return dataWired
                        ? new Result { Status = Status.Pass, Required = true, Label = "Save (Platform)", Detail = "Uses the save API." }
                        : new Result
                        {
                            Status = Status.Fail, Required = true, Label = "Save (Platform)",
                            Detail = "Publishing as Platform save, but the game has not touched the save API this session. " +
                                     "The player's progress would be stored nowhere.",
                            Fix = "Use GameHubBridge.Instance.SetInt/SetString (or ArsmiSave), and/or subscribe to " +
                                  "OnDataChanged to re-read after the platform hands over a cloud save. If your game " +
                                  "only saves on certain actions, perform one before testing.",
                        };

                case SaveMode.OwnBackend:
                    return Wired(bridge, "OnUserChanged")
                        ? new Result { Status = Status.Pass, Required = true, Label = "Save (own backend)", Detail = "Reads the player identity." }
                        : new Result
                        {
                            Status = Status.Fail, Required = true, Label = "Save (own backend)",
                            Detail = "Publishing as Own backend, but the game never reads the player identity. Without a " +
                                     "playerId it has nothing to key a save on.",
                            Fix = "Subscribe to OnUserChanged and read GameHubBridge.Instance.PlayerId.",
                        };

                default: // NoSave
                    return new Result
                    {
                        Status = Status.Info, Required = false, Label = "Save",
                        Detail = "Publishing with no save — nothing to check.",
                    };
            }
        }

        // ---- Reflection into the live bridge -------------------------------------
        //
        // The wiring flags the platform gates on are computed in GameHubBridge from whether an
        // event has a subscriber (OnMuteChanged != null) — a fact only the C# knows. Those
        // backing fields are private, so we read them by reflection rather than adding a
        // test-only accessor to the runtime that games would then ship. Field names are
        // centralised in the callers above so a rename is a one-line fix here.

        private static GameHubBridge FindBridge()
        {
            if (GameHubBridge.Instance != null) return GameHubBridge.Instance;
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<GameHubBridge>();
#else
            return UnityEngine.Object.FindObjectOfType<GameHubBridge>();
#endif
        }

        /// <summary>True if the field-like event has at least one subscriber.</summary>
        private static bool Wired(GameHubBridge bridge, string eventName)
        {
            var field = typeof(GameHubBridge).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.GetValue(bridge) is Delegate d && d.GetInvocationList().Length > 0;
        }

        private static bool PrivateBool(GameHubBridge bridge, string fieldName)
        {
            var field = typeof(GameHubBridge).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.GetValue(bridge) is bool b && b;
        }

        // ---- Small helpers -------------------------------------------------------

        private static Result PassFail(bool ok, bool required, string label, string pass, string failDetail, string fix)
        {
            return ok
                ? new Result { Status = Status.Pass, Required = required, Label = label, Detail = pass }
                : new Result { Status = Status.Fail, Required = required, Label = label, Detail = failDetail, Fix = fix };
        }

        private static Result Optional(string label, bool used, string detail)
        {
            return new Result
            {
                Status = used ? Status.Pass : Status.Optional,
                Required = false,
                Label = label,
                Detail = used ? detail : detail + " (not used)",
            };
        }

        private static string ReadSdkVersion(string path)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), "var SDK_VERSION = \"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : "an unversioned build (pre-1.0)";
            }
            catch { return "unknown"; }
        }

        private static string SaveModeHint(SaveMode mode)
        {
            switch (mode)
            {
                case SaveMode.Platform: return "The platform stores your save and syncs it across devices. Checks you use the save API.";
                case SaveMode.OwnBackend: return "You store saves on your own server, keyed by the player identity. Checks you read PlayerId.";
                default: return "The game does not save progress. No save requirement.";
            }
        }

        // ---- Verdict + drawing ---------------------------------------------------

        private int RequiredFailures()
        {
            var n = 0;
            foreach (var r in _static) if (r.Required && r.Status == Status.Fail) n++;
            foreach (var r in _live) if (r.Required && r.Status == Status.Fail) n++;
            return n;
        }

        private void DrawVerdict()
        {
            var failures = RequiredFailures();
            var liveNote = !_liveRan && !EditorApplication.isPlaying
                ? "  (live checks not run — enter Play Mode for the full verdict)"
                : "";

            if (failures == 0)
            {
                EditorGUILayout.HelpBox(
                    (_liveRan ? "All requirements met — this build should publish." : "Static checks pass.") + liveNote,
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    failures + (failures == 1 ? " requirement not met" : " requirements not met") +
                    " — the platform would refuse this build." + liveNote,
                    MessageType.Error);
            }
            EditorGUILayout.Space(4);
        }

        private void DrawSection(string title, List<Result> results, string emptyNote = null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            if (results.Count == 0)
            {
                EditorGUILayout.LabelField(emptyNote ?? "—", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            foreach (var r in results)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var prev = GUI.color;
                        GUI.color = StatusColor(r.Status);
                        EditorGUILayout.LabelField(StatusGlyph(r.Status), GUILayout.Width(20));
                        GUI.color = prev;
                        EditorGUILayout.LabelField(r.Label, EditorStyles.boldLabel);
                    }
                    if (!string.IsNullOrEmpty(r.Detail))
                        EditorGUILayout.LabelField(r.Detail, EditorStyles.wordWrappedMiniLabel);
                    if (r.Status == Status.Fail && !string.IsNullOrEmpty(r.Fix))
                        EditorGUILayout.LabelField("Fix: " + r.Fix, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private static string StatusGlyph(Status s)
        {
            switch (s)
            {
                case Status.Pass: return "✓";
                case Status.Fail: return "✗";
                case Status.Optional: return "·";
                default: return "–";
            }
        }

        private static Color StatusColor(Status s)
        {
            switch (s)
            {
                case Status.Pass: return new Color(0.30f, 0.78f, 0.40f);
                case Status.Fail: return new Color(0.90f, 0.35f, 0.30f);
                default: return new Color(0.60f, 0.60f, 0.60f);
            }
        }

        // ---- Report file ---------------------------------------------------------

        private void SaveReport()
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var path = EditorUtility.SaveFilePanel(
                "Save SDK report", "", "ArsmiSdkReport-" + stamp + ".md", "md");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("# Arsmi Games — SDK Integration Report");
            sb.AppendLine();
            sb.AppendLine("- Project: " + PlayerSettings.productName);
            sb.AppendLine("- Publishing as: " + _saveMode);
            sb.AppendLine("- Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();

            var failures = RequiredFailures();
            sb.AppendLine("**Verdict: " +
                (failures == 0
                    ? (_liveRan ? "all requirements met" : "static checks pass (live checks not run)")
                    : failures + (failures == 1 ? " requirement not met" : " requirements not met")) +
                "**");
            sb.AppendLine();

            AppendSection(sb, "Build settings", _static);
            AppendSection(sb, "Live protocol", _live);

            File.WriteAllText(path, sb.ToString());
            EditorUtility.RevealInFinder(path);
        }

        private static void AppendSection(StringBuilder sb, string title, List<Result> results)
        {
            if (results.Count == 0) return;
            sb.AppendLine("## " + title);
            sb.AppendLine();
            foreach (var r in results)
            {
                sb.AppendLine("- " + StatusGlyph(r.Status) + " **" + r.Label + "** — " + r.Detail);
                if (r.Status == Status.Fail && !string.IsNullOrEmpty(r.Fix))
                    sb.AppendLine("  - Fix: " + r.Fix);
            }
            sb.AppendLine();
        }
    }
}
