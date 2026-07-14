using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArsmiGames.Demo
{
    /// <summary>
    /// Every call the SDK exposes, behind a button, over a live log of what actually
    /// crossed the bridge.
    ///
    /// The log is the point. A WebGL build misbehaving inside the platform's iframe has no
    /// debugger to attach, so being able to see "I sent X, the platform answered Y" is the
    /// difference between a five-minute fix and a day of guessing — and it is the only way
    /// to tell a working game from one whose SDK never loaded, because the second one looks
    /// completely fine and simply says nothing.
    /// </summary>
    public class SdkFunctionPanel : MonoBehaviour
    {
        private TextMeshProUGUI _log;
        private TextMeshProUGUI _status;
        private ScrollRect _logScroll;
        private readonly List<string> _lines = new List<string>();
        private const int MaxLines = 120;

        public void Build(Transform parent)
        {
            var host = DemoUI.Node("SdkCard", parent);
            DemoUI.Box(host, DemoUI.Panel);
            DemoUI.Fill(host);

            // Buttons on top, log pinned to the bottom third. Both are anchored fractions,
            // so the log stays visible at any frame size instead of being pushed off.
            var top = DemoUI.Node("Calls", host.transform);
            DemoUI.Anchor(top, new Vector2(0f, 0.42f), new Vector2(1f, 1f), new Vector4(0, 0, 0, 0));

            var bottom = DemoUI.Node("Log", host.transform);
            DemoUI.Anchor(bottom, new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector4(16, 16, 16, 8));

            BuildCalls(DemoUI.Scroll(top.transform, out _, 18));
            BuildLog(bottom.transform);

            Subscribe();
            Refresh();
        }

        private void BuildCalls(Transform col)
        {
            _status = DemoUI.Label(col, "Waiting for the platform…", 15, DemoUI.Muted);
            DemoUI.Height(_status.gameObject, 60);

            DemoUI.Section(col, "Identity");
            var identity = DemoUI.Row(col, 46);
            DemoUI.Btn(identity.transform, "Who am I?", Refresh, null, null, 46, 16);
            DemoUI.Btn(identity.transform, "Request login", () => Hub()?.RequestLogin("demo"), null, null, 46, 16);

            DemoUI.Section(col, "Window");
            var window = DemoUI.Row(col, 46);
            DemoUI.Btn(window.transform, "Fullscreen", () => Hub()?.RequestFullscreen("landscape"), null, null, 46, 16);
            DemoUI.Btn(window.transform, "Mute", () => Hub()?.SetMuted(true), null, null, 46, 16);
            DemoUI.Btn(window.transform, "Unmute", () => Hub()?.SetMuted(false), null, null, 46, 16);

            DemoUI.Section(col, "Ads (platform overlay)");
            DemoUI.Btn(col, "Show rewarded ad", () => { Log("→ ad:show"); Hub()?.ShowRewardedAd("sdk-panel"); },
                       new Color(DemoUI.Gold.r, DemoUI.Gold.g, DemoUI.Gold.b, 0.16f), DemoUI.Gold, 46, 16);

            // No "set balance" button on purpose. The game can read the wallet and ask to
            // spend from it; it cannot decide what is in it. Coins are earned through
            // rewarded ads and achievements, which the platform grants.
            DemoUI.Section(col, "Wallet — server-authoritative, never from a save");
            var wallet = DemoUI.Row(col, 46);
            DemoUI.Btn(wallet.transform, "Get balance", () => { Hub()?.WalletGet(); Log("→ wallet:get"); }, null, null, 46, 16);
            DemoUI.Btn(wallet.transform, "Spend 5", () => { Hub()?.WalletSpend(5, "sdk-panel"); Log("→ wallet:spend 5"); }, null, null, 46, 16);

            DemoUI.Section(col, "Achievements");
            var ach = DemoUI.Row(col, 46);
            DemoUI.Btn(ach.transform, "Define", DefineAchievements, null, null, 46, 16);
            DemoUI.Btn(ach.transform, "+1 progress", () => { Hub()?.AchievementProgress("quiz_correct", 1); Log("→ achievement:progress"); }, null, null, 46, 16);

            DemoUI.Section(col, "Leaderboard");
            var lb = DemoUI.Row(col, 46);
            DemoUI.Btn(lb.transform, "Define", () =>
            {
                Hub()?.LeaderboardDefineJson("{\"boards\":[{\"metricKey\":\"quiz_score\",\"metricLabel\":\"Quiz score\",\"sortDirection\":\"desc\"}]}");
                Log("→ leaderboard:define");
            }, null, null, 46, 16);
            DemoUI.Btn(lb.transform, "Submit 100", () => { Hub()?.LeaderboardScore(100, "quiz_score", "Quiz score"); Log("→ leaderboard:score 100"); }, null, null, 46, 16);

            DemoUI.Section(col, "Save data");
            var data = DemoUI.Row(col, 46);
            DemoUI.Btn(data.transform, "Read all", DumpSave, null, null, 46, 16);
            DemoUI.Btn(data.transform, "Write key", () =>
            {
                Hub()?.SetItem("demo_ping", System.DateTime.UtcNow.ToString("HH:mm:ss"));
                Log("data.setItem(demo_ping) — debounced, flushes in ~1s");
            }, null, null, 46, 16);

            var data2 = DemoUI.Row(col, 46);
            DemoUI.Btn(data2.transform, "Flush now", () => { Hub()?.FlushData(); Log("→ data:set (forced)"); }, null, null, 46, 16);
            DemoUI.Btn(data2.transform, "Clear save", () => { Hub()?.ClearData(); Log("→ data:clear"); },
                       new Color(DemoUI.Bad.r, DemoUI.Bad.g, DemoUI.Bad.b, 0.16f), DemoUI.Bad, 46, 16);

            DemoUI.Section(col, "Logging");
            DemoUI.Btn(col, "Send a log line to the platform",
                       () => { Hub()?.LogInfo("Hello from the Unity demo"); Log("→ bridge:log"); }, null, null, 46, 16);
        }

        private void BuildLog(Transform parent)
        {
            var head = DemoUI.Node("LogHead", parent);
            DemoUI.Anchor(head, new Vector2(0f, 0.88f), new Vector2(1f, 1f));

            var title = DemoUI.Label(head.transform, "BRIDGE LOG", 15, DemoUI.Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            title.characterSpacing = 6f;
            DemoUI.Anchor(title.gameObject, new Vector2(0f, 0f), new Vector2(0.6f, 1f));

            var clearGo = DemoUI.Node("Clear", head.transform);
            DemoUI.Anchor(clearGo, new Vector2(0.78f, 0.05f), new Vector2(1f, 0.95f));
            var clearImage = DemoUI.Box(clearGo, DemoUI.PanelSoft);
            clearImage.raycastTarget = true;
            var clearBtn = clearGo.AddComponent<Button>();
            clearBtn.targetGraphic = clearImage;
            clearBtn.onClick.AddListener(() =>
            {
                _lines.Clear();
                if (_log != null) _log.text = "";
            });
            var clearLabel = DemoUI.Label(clearGo.transform, "Clear", 14, DemoUI.Muted, TextAlignmentOptions.Center, FontStyles.Bold);
            DemoUI.Fill(clearLabel.gameObject, 4f);

            var body = DemoUI.Node("LogBody", parent);
            DemoUI.Anchor(body, new Vector2(0f, 0f), new Vector2(1f, 0.86f));

            var content = DemoUI.Scroll(body.transform, out _logScroll, 10);
            _log = DemoUI.Label(content, "", 14, DemoUI.Muted);
        }

        private static GameHubBridge Hub() => GameHubBridge.Instance;

        private void Subscribe()
        {
            var hub = Hub();
            if (hub == null) return;

            hub.OnUserChanged += () => { Refresh(); Log("← user:state"); };
            hub.OnDataChanged += () => { Refresh(); Log($"← data:state — mode={hub.SaveMode}, {Count(hub.Keys)} keys"); };
            hub.OnDataError += message => Log($"← data:error — {message}");
            hub.OnAdStarted += () => Log("← ad:started");
            hub.OnAdFinished += (rewarded, balance) =>
                Log(rewarded ? $"← ad:rewarded (balance {balance})" : "← ad:dismissed");
            hub.OnWalletChanged += balance => Log($"← wallet:state — {balance} flux");
            hub.OnWalletError += message => Log($"← wallet:error — {message}");
            hub.OnMuteChanged += (muted, fromPlatform) =>
            {
                // The demo has no audio, but a real game mutes its AudioListener here —
                // and it must do so whichever side asked, or the platform's volume button
                // does nothing.
                AudioListener.pause = muted;
                Log($"{(fromPlatform ? "←" : "→")} audio {(muted ? "muted" : "unmuted")} " +
                    $"({(fromPlatform ? "platform" : "game")})");
            };
        }

        private static int Count(IEnumerable<string> items)
        {
            var n = 0;
            foreach (var _ in items) n++;
            return n;
        }

        private void Refresh()
        {
            var hub = Hub();
            if (_status == null) return;

            if (hub == null)
            {
                _status.text = "<color=#e65b5b>No bridge.</color> The SDK did not load.";
                return;
            }

            if (!hub.LoggedIn)
            {
                _status.text = "<b>Guest.</b> Progress is kept on the platform and merges into the account on login.";
                _status.color = DemoUI.Muted;
                return;
            }

            var playerId = string.IsNullOrEmpty(hub.PlayerId) ? "—" : hub.PlayerId.Substring(0, 12) + "…";
            _status.text =
                $"<b>{hub.DisplayName}</b>   save mode <color=#cc785c>{hub.SaveMode}</color>\n" +
                $"<size=13><color=#6a6978>playerId (own backend): {playerId}</color></size>";
            _status.color = DemoUI.Text;
        }

        private void DumpSave()
        {
            var hub = Hub();
            if (hub == null) return;
            if (!hub.DataReady) { Log("save has not arrived yet"); return; }

            var any = false;
            foreach (var key in hub.Keys)
            {
                Log($"   {key} = {hub.GetItem(key)}");
                any = true;
            }
            if (!any) Log("save is empty");
        }

        // Every field here is load-bearing. The platform's importer skips any entry that is
        // missing one — silently, with no error and no log line — so an achievement with no
        // rewardFlux, or without shareWithPlatform, simply never comes into existence and
        // the game has no way to find out. The two easy ones to forget:
        //
        //   shareWithPlatform  the importer's opt-in filter; without it the entry is dropped
        //   rewardFlux         must be > 0, or the entry is dropped
        //
        // The SDK test bench checks a manifest against these same rules and says which
        // entries would be thrown away.
        private void DefineAchievements()
        {
            Hub()?.AchievementsDefine(
                "{\"achievements\":[" +
                "{\"key\":\"quiz_first_correct\",\"title\":\"Bright spark\",\"description\":\"Answer your first question correctly.\"," +
                "\"metric\":\"quiz_correct\",\"target\":1,\"rewardFlux\":10,\"type\":\"daily\",\"shareWithPlatform\":true}," +
                "{\"key\":\"quiz_ten_correct\",\"title\":\"Quiz whiz\",\"description\":\"Answer 10 questions correctly.\"," +
                "\"metric\":\"quiz_correct\",\"target\":10,\"rewardFlux\":50,\"type\":\"daily\",\"shareWithPlatform\":true}" +
                "]}");
            Log("→ achievements:manifest");
        }

        public void Log(string line)
        {
            _lines.Add($"<color=#6a6978>{System.DateTime.Now:HH:mm:ss}</color>  {line}");
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            if (_log == null) return;

            _log.text = string.Join("\n", _lines);
            // Stick to the newest line, which is the one you are waiting for.
            if (_logScroll != null) Canvas.ForceUpdateCanvases();
            if (_logScroll != null) _logScroll.verticalNormalizedPosition = 0f;
        }
    }
}
