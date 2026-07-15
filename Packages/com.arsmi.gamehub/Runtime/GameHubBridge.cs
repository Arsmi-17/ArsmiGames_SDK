using System.Runtime.InteropServices;
using UnityEngine;

public class GameHubBridge : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void GameHubBridge_Init(string gameObjectName);
    [DllImport("__Internal")] private static extern void GameHubBridge_Emit(string eventName, string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_Log(string level, string message, string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_RequestFullscreen(string orientation);
    [DllImport("__Internal")] private static extern void GameHubBridge_RequestLogin(string reason);
    [DllImport("__Internal")] private static extern void GameHubBridge_SetMuted(int muted);
    [DllImport("__Internal")] private static extern void GameHubBridge_ReportWiring(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_Ack(string eventName, int handled);
    [DllImport("__Internal")] private static extern void GameHubBridge_ChallengeReady(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_ChallengeState(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_ChallengeResult(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_PocketReady(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_PocketSchema(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_LeaderboardDefine(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_LeaderboardScore(string json);
    [DllImport("__Internal")] private static extern void GameHubBridge_DataSetItem(string key, string value);
    [DllImport("__Internal")] private static extern void GameHubBridge_DataRemoveItem(string key);
    [DllImport("__Internal")] private static extern void GameHubBridge_DataClear();
    [DllImport("__Internal")] private static extern void GameHubBridge_DataFlush();
    [DllImport("__Internal")] private static extern void GameHubBridge_ShowRewardedAd(string json);
#endif

    public static GameHubBridge Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_Init(gameObject.name);
#endif
    }

    private System.Collections.IEnumerator Start()
    {
        // Wait one frame. Every other Start() has run by then, so a game that subscribes in
        // Start (most do) is counted. Reporting in our own Start would race them, and a game
        // that had wired everything up correctly could be told it had not.
        yield return null;
        ReportWiring();
    }

    // ---- what this game actually wired up ---------------------------------------
    //
    // The platform will not publish a game that ignores the volume button or drops the
    // player's save. It cannot tell that from the outside: the .jslib subscribes to every
    // platform message on the game's behalf whether or not the C# does anything with them,
    // so from JavaScript every Unity build looks compliant.
    //
    // Only C# knows the truth, and it is a simple truth: did the game attach a handler? A
    // null event means nobody is listening, which means the platform's volume button does
    // nothing and the player's progress goes nowhere.

    private bool _usedSaveApi;

    /// <summary>Tells the platform which requirements this game has actually implemented.
    /// Called automatically after the first frame. Call it again yourself if you subscribe
    /// to bridge events later than that.</summary>
    public void ReportWiring()
    {
        var json =
            "{" +
            $"\"mute\":{Bool(OnMuteChanged != null)}," +
            $"\"fullscreen\":{Bool(OnFullscreenChanged != null)}," +
            $"\"data\":{Bool(OnDataChanged != null || _usedSaveApi)}," +
            $"\"user\":{Bool(OnUserChanged != null)}," +
            $"\"wallet\":{Bool(OnWalletChanged != null)}," +
            $"\"ads\":{Bool(OnAdFinished != null)}," +
            $"\"leaderboard\":{Bool(_definedLeaderboard)}" +
            "}";

#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_ReportWiring(json);
#else
        Debug.Log($"[GameHubBridge] wiring {json}");
#endif
    }

    private bool _definedLeaderboard;

    private static string Bool(bool value) => value ? "true" : "false";

    // ---- Acknowledgements -------------------------------------------------------
    //
    // The platform sends set_mute and set_fullscreen and then waits to hear what happened.
    // It has to: a game *receiving* a message and honouring it sends nothing back, so from
    // the outside a game that mutes itself and a game that ignores the volume button look
    // exactly the same.
    //
    // JavaScript cannot answer this for us. GameHubBridge.jslib subscribes to both events on
    // the game's behalf whether or not this C# does anything with them, so a JS-side answer
    // would say "handled" for every Unity build ever made. Only we can see the truth, and it
    // is a simple one: a null event means nobody is listening.

    /// <summary>Answers a platform message: did this game actually do anything with it?</summary>
    private static void Ack(string eventName, bool handled)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_Ack(eventName, handled ? 1 : 0);
#else
        Debug.Log($"[GameHubBridge] ack {eventName} handled={handled}");
#endif
    }

    public void LogInfo(string message) => Log("info", message, "{}");
    public void LogWarning(string message) => Log("warn", message, "{}");
    public void LogError(string message) => Log("error", message, "{}");

    public void Emit(string eventName, string json = "{}")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_Emit(eventName, string.IsNullOrEmpty(json) ? "{}" : json);
#else
        Debug.Log($"[GameHubBridge] Emit {eventName} {json}");
#endif
    }

    public void Log(string level, string message, string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_Log(level, message, string.IsNullOrEmpty(json) ? "{}" : json);
#else
        Debug.Log($"[GameHubBridge] {level}: {message} {json}");
#endif
    }

    public void RequestFullscreen(string orientation = "auto")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_RequestFullscreen(orientation);
#endif
    }

    public void RequestLogin(string reason = "game")
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_RequestLogin(reason);
#else
        Debug.Log($"[GameHubBridge] RequestLogin {reason}");
#endif
    }

    // ---- Mute -------------------------------------------------------------------
    //
    // Mute is two-way, and both directions matter. The platform's volume button sends
    // set_mute, and the game has to honour it or the button is a lie. When the game
    // mutes itself, it says so, and the platform's icon changes to match — otherwise
    // the player sees a speaker icon while hearing nothing.

    /// <summary>The current mute state, whoever last changed it.</summary>
    public bool IsMuted { get; private set; }

    /// <summary>muted, and whether the platform asked for it (false = the game did).</summary>
    public event System.Action<bool, bool> OnMuteChanged;

    /// <summary>Tells the platform the game muted or unmuted itself.</summary>
    public void SetMuted(bool muted)
    {
        // The platform echoes its own set_mute back to us. Without this the echo would
        // bounce out again as audio_muted and the two would ping-pong forever.
        if (muted == IsMuted) return;
        IsMuted = muted;

#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_SetMuted(muted ? 1 : 0);
#else
        Debug.Log($"[GameHubBridge] SetMuted {muted}");
#endif
        OnMuteChanged?.Invoke(muted, false);
    }

    /// <summary>The platform's volume button. Mute your AudioListener here.</summary>
    public void OnGameHubMuted(string json)
    {
        // Answer first, and answer whether or not the state actually changes. The platform
        // checks this game handles mute by sending the state it is already in — a probe that
        // is silent to the player. Acking only on a change would report that game as broken.
        Ack("set_mute", OnMuteChanged != null);

        var muted = ReadJsonBool(json, "muted");
        if (muted == IsMuted) return;
        IsMuted = muted;
        OnMuteChanged?.Invoke(muted, true);
    }

    public void RequestUserState(string game = "")
    {
        var json = string.IsNullOrEmpty(game) ? "{}" : $"{{\"game\":\"{Escape(game)}\"}}";
        Emit("gamehub:user:get", json);
    }

    // ---- Wallet -----------------------------------------------------------------
    //
    // Flux Coins are real currency, so the balance is whatever the SERVER says it is,
    // never what the game says it is.
    //
    // Read it, and ask to spend it. There is no way to add to it, and that is not an
    // oversight: coins are bought from the platform, or granted by the platform for
    // watching a PLATFORM ad or claiming an achievement the PLATFORM defined. A game that
    // could add to the balance would be a game printing money.
    //
    // That includes rewarded ads your game asks for. They pay out in YOUR currency — clear
    // the boss, unlock the skin — which your own code grants. They do not pay Flux.

    /// <summary>The last balance the platform sent. -1 until the first reply arrives.</summary>
    public int FluxCoins { get; private set; } = -1;

    public string WalletCurrency { get; private set; } = "flux";

    /// <summary>Fires with the new balance whenever the platform reports it — after a
    /// spend, after a rewarded ad, or after another tab changed it.</summary>
    public event System.Action<int> OnWalletChanged;

    /// <summary>A wallet call was refused. The commonest reason is that the player
    /// cannot afford the spend — so do not hand over whatever they were buying.</summary>
    public event System.Action<string> OnWalletError;

    /// <summary>Asks the platform for the current balance. The answer arrives on
    /// OnWalletChanged.</summary>
    public void WalletGet(string currency = "flux", double rate = 1, string game = "")
    {
        var json = $"{{\"currency\":\"{Escape(currency)}\",\"rate\":{Invariant(rate)},\"game\":\"{Escape(game)}\"}}";
        Emit("gamehub:wallet:get", json);
    }

    /// <summary>Spends coins. The server checks the player can afford it, so this can
    /// fail — wait for OnWalletChanged before granting anything, and handle
    /// OnWalletError.</summary>
    public void WalletSpend(int amount, string reason = "game")
    {
        if (amount <= 0)
        {
            OnWalletError?.Invoke("Spend amount must be a positive number.");
            return;
        }
        var json = $"{{\"amount\":{amount},\"reason\":\"{Escape(reason)}\"}}";
        Emit("gamehub:wallet:spend", json);
    }

    // WalletSet is gone. It wrote an absolute balance and was trusted as-is, so any game
    // could mint currency with one call — and the platform now refuses the event outright.
    // Take coins with WalletSpend. There is no counterpart that gives them.

    public void ChallengeReady(int maxPlayers, string mode = "ranked", bool ranked = true)
    {
        var json = $"{{\"maxPlayers\":{maxPlayers},\"mode\":\"{Escape(mode)}\",\"ranked\":{ranked.ToString().ToLowerInvariant()}}}";
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_ChallengeReady(json);
#else
        Debug.Log($"[GameHubBridge] ChallengeReady {json}");
#endif
    }

    public void ChallengeState(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_ChallengeState(string.IsNullOrEmpty(json) ? "{}" : json);
#endif
    }

    public void ChallengeResult(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_ChallengeResult(string.IsNullOrEmpty(json) ? "{}" : json);
#endif
    }

    public void PocketReady(int maxPlayers, string layout = "dpad-buttons", string schemaJson = "{}")
    {
        var json = $"{{\"maxPlayers\":{maxPlayers},\"layout\":\"{Escape(layout)}\",\"schema\":{(string.IsNullOrEmpty(schemaJson) ? "{}" : schemaJson)}}}";
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_PocketReady(json);
#else
        Debug.Log($"[GameHubBridge] PocketReady {json}");
#endif
    }

    public void PocketSchema(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_PocketSchema(string.IsNullOrEmpty(json) ? "{}" : json);
#endif
    }

    // Achievements were removed from the platform. Track them in your own game and reward the
    // player in your own currency — that was already the only thing a game's achievements could
    // do, since they were never worth any Flux.

    public void LeaderboardDefineJson(string json)
    {
        _definedLeaderboard = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_LeaderboardDefine(string.IsNullOrEmpty(json) ? "{}" : json);
#else
        Debug.Log($"[GameHubBridge] LeaderboardDefine {json}");
#endif
    }

    public void LeaderboardDefine(string metricKey = "score", string metricLabel = "Score", string sortDirection = "desc")
    {
        var json = $"{{\"metricKey\":\"{Escape(metricKey)}\",\"metricLabel\":\"{Escape(metricLabel)}\",\"sortDirection\":\"{Escape(sortDirection)}\"}}";
        LeaderboardDefineJson(json);
    }

    public void LeaderboardScoreJson(string json)
    {
        // Submitting a score IS using the leaderboard. A score message carries its own board
        // definition (metricKey, label, sortDirection), so a game never has to call
        // LeaderboardDefine separately — and most do not. Without this line, a game that only
        // submits scores reported leaderboard:false in its wiring, so the platform's assessment
        // (and the in-editor test runner) both showed "leaderboard not used" for a game visibly
        // posting scores. The flag means "has this game touched the leaderboard", and a score is
        // the most direct way there is to touch it.
        _definedLeaderboard = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_LeaderboardScore(string.IsNullOrEmpty(json) ? "{}" : json);
#else
        Debug.Log($"[GameHubBridge] LeaderboardScore {json}");
#endif
    }

    public void LeaderboardScore(double score, string metricKey = "score", string metricLabel = "Score", string sortDirection = "desc", string metadataJson = "{}")
    {
        var json = $"{{\"metricKey\":\"{Escape(metricKey)}\",\"metricLabel\":\"{Escape(metricLabel)}\",\"score\":{Invariant(score)},\"sortDirection\":\"{Escape(sortDirection)}\",\"metadata\":{(string.IsNullOrEmpty(metadataJson) ? "{}" : metadataJson)}}}";
        LeaderboardScoreJson(json);
    }

    // ---- Save data ------------------------------------------------------------
    //
    // The game stays the source of truth: keep saving locally exactly as you do
    // now, and mirror the same values here. Arsmi Games backs them up to the
    // player's account so their progress follows them to another device.
    //
    // Reads are served from this local copy, so calling GetItem in Update() is
    // free — it never crosses into JavaScript. Writes are debounced by the SDK
    // and forced out when the tab is hidden or closed.
    //
    // Requires the game to be published with "Save progress" set to the Data
    // Module option; otherwise every write here is a no-op.

    private readonly System.Collections.Generic.Dictionary<string, string> _saveData =
        new System.Collections.Generic.Dictionary<string, string>();

    /// <summary>True once the player's save has arrived. Don't read progress before this.</summary>
    public bool DataReady { get; private set; }

    /// <summary>"no", "sdk" or "backend" — what this game was published with.</summary>
    public string SaveMode { get; private set; } = "no";

    public bool LoggedIn { get; private set; }

    /// <summary>Fires when the save arrives, and again if the platform replaces it —
    /// after a guest signs in and their progress is merged up, or when another
    /// device turns out to be further ahead. Re-read your values when it fires.</summary>
    public event System.Action OnDataChanged;

    public event System.Action<string> OnDataError;

    public string GetItem(string key, string fallback = null)
    {
        _usedSaveApi = true;
        return _saveData.TryGetValue(key ?? "", out var value) ? value : fallback;
    }

    public bool HasItem(string key) => _saveData.ContainsKey(key ?? "");

    public System.Collections.Generic.IEnumerable<string> Keys => _saveData.Keys;

    public void SetItem(string key, string value)
    {
        _usedSaveApi = true;
        if (string.IsNullOrEmpty(key)) return;
        _saveData[key] = value ?? "";
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_DataSetItem(key, value ?? "");
#else
        Debug.Log($"[GameHubBridge] SetItem {key}={value}");
#endif
    }

    public void SetInt(string key, int value) => SetItem(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public void SetFloat(string key, float value) => SetItem(key, Invariant(value));
    public void SetBool(string key, bool value) => SetItem(key, value ? "1" : "0");

    public int GetInt(string key, int fallback = 0)
    {
        var raw = GetItem(key);
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public float GetFloat(string key, float fallback = 0f)
    {
        var raw = GetItem(key);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public bool GetBool(string key, bool fallback = false)
    {
        var raw = GetItem(key);
        if (string.IsNullOrEmpty(raw)) return fallback;
        return raw == "1" || raw == "true" || raw == "True";
    }

    public void RemoveItem(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        _saveData.Remove(key);
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_DataRemoveItem(key);
#else
        Debug.Log($"[GameHubBridge] RemoveItem {key}");
#endif
    }

    public void ClearData()
    {
        _saveData.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_DataClear();
#else
        Debug.Log("[GameHubBridge] ClearData");
#endif
    }

    /// <summary>Writes pending changes now instead of waiting for the debounce.
    /// You rarely need this — the SDK already forces a write when the tab is hidden
    /// or closed.</summary>
    public void FlushData()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_DataFlush();
#else
        Debug.Log("[GameHubBridge] FlushData");
#endif
    }

    /// <summary>When the platform last accepted a write, ISO-8601. Null if it never has.</summary>
    public string SaveUpdatedAt { get; private set; }

    public void OnGameHubDataState(string json)
    {
        SaveMode = ReadJsonString(json, "mode") ?? SaveMode;
        LoggedIn = ReadJsonBool(json, "loggedIn");
        SaveUpdatedAt = ReadJsonString(json, "updatedAt") ?? SaveUpdatedAt;

        var map = ReadJsonStringMap(json, "data");
        if (map != null)
        {
            _saveData.Clear();
            foreach (var pair in map) _saveData[pair.Key] = pair.Value;
        }

        DataReady = true;
        OnDataChanged?.Invoke();
    }

    public void OnGameHubDataError(string json)
    {
        var message = ReadJsonString(json, "message") ?? "Save failed.";
        Debug.LogWarning($"[GameHubBridge] Data error: {message}");
        OnDataError?.Invoke(message);
    }

    // JsonUtility cannot deserialize a dictionary, and the save map is by definition
    // arbitrary keys, so the values we care about are pulled out by hand. Our own API
    // is the only producer of this payload and it always sends a flat string->string
    // map, which is what keeps this small.

    private static string ReadJsonString(string json, string field)
    {
        var at = FindField(json, field);
        if (at < 0) return null;
        while (at < json.Length && json[at] != '"' && json[at] != ',' && json[at] != '}') at++;
        if (at >= json.Length || json[at] != '"') return null;
        return ReadString(json, ref at);
    }

    private static double? ReadJsonNumber(string json, string field)
    {
        var at = FindField(json, field);
        if (at < 0) return null;

        var start = at;
        while (at < json.Length && (char.IsDigit(json[at]) || json[at] == '-' || json[at] == '+' ||
                                    json[at] == '.' || json[at] == 'e' || json[at] == 'E')) at++;
        if (at == start) return null; // null, or a string where a number was expected

        return double.TryParse(json.Substring(start, at - start), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : (double?)null;
    }

    private static bool ReadJsonBool(string json, string field)
    {
        var at = FindField(json, field);
        if (at < 0) return false;
        while (at < json.Length && char.IsWhiteSpace(json[at])) at++;
        return at + 4 <= json.Length && json.Substring(at, 4) == "true";
    }

    /// <summary>Pulls a flat string-to-string object out of a JSON payload.
    /// Exposed because a game with its own backend gets the same shape back from it
    /// and should not have to bring in a JSON library just to read a save map.</summary>
    public static System.Collections.Generic.Dictionary<string, string> ParseStringMap(string json, string field)
    {
        return ReadJsonStringMap(json, field);
    }

    private static System.Collections.Generic.Dictionary<string, string> ReadJsonStringMap(string json, string field)
    {
        var at = FindField(json, field);
        if (at < 0) return null;
        while (at < json.Length && json[at] != '{' && json[at] != 'n') at++;
        if (at >= json.Length || json[at] != '{') return null;
        at++;

        var map = new System.Collections.Generic.Dictionary<string, string>();
        while (at < json.Length)
        {
            while (at < json.Length && (char.IsWhiteSpace(json[at]) || json[at] == ',')) at++;
            if (at >= json.Length || json[at] == '}') break;
            if (json[at] != '"') break;

            var key = ReadString(json, ref at);
            while (at < json.Length && (char.IsWhiteSpace(json[at]) || json[at] == ':')) at++;
            if (at >= json.Length || json[at] != '"') break;

            map[key] = ReadString(json, ref at);
        }
        return map;
    }

    /// <summary>Index just past the colon of a top-level "field": in the payload.</summary>
    private static int FindField(string json, string field)
    {
        if (string.IsNullOrEmpty(json)) return -1;
        var needle = "\"" + field + "\"";
        var at = json.IndexOf(needle, System.StringComparison.Ordinal);
        if (at < 0) return -1;
        at += needle.Length;
        while (at < json.Length && (char.IsWhiteSpace(json[at]) || json[at] == ':')) at++;
        return at;
    }

    /// <summary>Reads one JSON string starting at the opening quote, honouring escapes.
    /// Leaves the index just past the closing quote.</summary>
    private static string ReadString(string json, ref int at)
    {
        var sb = new System.Text.StringBuilder();
        at++; // opening quote
        while (at < json.Length)
        {
            var c = json[at++];
            if (c == '"') break;
            if (c != '\\') { sb.Append(c); continue; }
            if (at >= json.Length) break;

            var esc = json[at++];
            switch (esc)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (at + 4 <= json.Length &&
                        int.TryParse(json.Substring(at, 4), System.Globalization.NumberStyles.HexNumber,
                                     System.Globalization.CultureInfo.InvariantCulture, out var code))
                    {
                        sb.Append((char)code);
                        at += 4;
                    }
                    break;
                default: sb.Append(esc); break; // covers \" \\ \/
            }
        }
        return sb.ToString();
    }

    public void OnGameHubChallengeStart(string json) => Debug.Log($"[GameHubBridge] Challenge start: {json}");
    public void OnGameHubChallengeLeaderboard(string json) => Debug.Log($"[GameHubBridge] Challenge leaderboard: {json}");
    public void OnGameHubChallengeEnd(string json) => Debug.Log($"[GameHubBridge] Challenge end: {json}");
    public void OnGameHubContext(string json) => Debug.Log($"[GameHubBridge] Context: {json}");
    /// <summary>True while the platform is showing the game fullscreen.</summary>
    public bool IsFullscreen { get; private set; }

    /// <summary>The platform entered or left fullscreen. Re-layout if your UI needs it.
    /// Subscribing is what tells the platform you handle fullscreen at all.</summary>
    public event System.Action<bool> OnFullscreenChanged;

    public void OnGameHubFullscreen(string json)
    {
        Ack("set_fullscreen", OnFullscreenChanged != null);

        var next = ReadJsonBool(json, "fullscreen");
        if (next == IsFullscreen) return;
        IsFullscreen = next;
        OnFullscreenChanged?.Invoke(next);
    }
    // ---- Ads --------------------------------------------------------------------
    //
    // Rewarded ads are a PLATFORM overlay, drawn over the game frame. The game does not
    // render the ad, does not time it, and does not get to say whether it was watched — a
    // game cannot be trusted to report that, so the decision stays outside the iframe.
    //
    // What the ad PAYS is entirely yours. An ad your game asked for grants no Flux Coins:
    // you clear the boss level, you unlock the skin, you refill the lives — whatever your
    // own economy says, granted by your own code when rewarded is true.
    //
    // (The platform has its own "watch an ad for Flux" button in its UI. That one belongs
    // to the platform, the player starts it deliberately, and it has nothing to do with
    // your game.)
    //
    // Pause and mute yourself when OnAdStarted fires; the platform will not do it for you
    // beyond muting the frame. Only pay out when OnAdFinished reports rewarded: true.

    /// <summary>True while a platform ad is on screen.</summary>
    public bool AdShowing { get; private set; }

    public event System.Action OnAdStarted;

    /// <summary>The ad finished. true = the player watched it to the end, so grant whatever
    /// YOUR game promised. false = they skipped it or it failed, so grant nothing.
    ///
    /// There is no balance here any more, because an ad your game asked for does not move
    /// the player's Flux balance at all.</summary>
    public event System.Action<bool> OnAdFinished;

    public void ShowRewardedAd(string placement = "game")
    {
        var json = $"{{\"type\":\"rewarded\",\"placement\":\"{Escape(placement)}\"}}";
#if UNITY_WEBGL && !UNITY_EDITOR
        GameHubBridge_ShowRewardedAd(json);
#else
        Debug.Log($"[GameHubBridge] ShowRewardedAd {json} (no platform in the editor)");
#endif
    }

    public void OnGameHubAdState(string json)
    {
        var status = ReadJsonString(json, "status") ?? "";

        if (status == "started")
        {
            AdShowing = true;
            OnAdStarted?.Invoke();
            return;
        }

        AdShowing = false;
        OnAdFinished?.Invoke(status == "rewarded");
    }

    // ---- Player identity --------------------------------------------------------

    /// <summary>Pseudonymous id for this player *in this game*. Null until the user
    /// state arrives, and null for guests.
    ///
    /// This is what a game with its own backend keys its records on. It is derived
    /// from the platform user and the game, so it is stable forever for this player
    /// here, and two games cannot compare ids to work out they have the same person.
    /// Never use the raw platform user id for that.</summary>
    public string PlayerId { get; private set; }

    public string DisplayName { get; private set; }
    public string Username { get; private set; }
    public string AvatarPath { get; private set; }

    /// <summary>Fires whenever the platform reports who is playing — including the
    /// moment a guest signs in mid-session.</summary>
    public event System.Action OnUserChanged;

    public void OnGameHubUserState(string json)
    {
        LoggedIn = ReadJsonBool(json, "loggedIn");
        PlayerId = ReadJsonString(json, "playerId");
        Username = ReadJsonString(json, "username");
        DisplayName = ReadJsonString(json, "displayName");
        AvatarPath = ReadJsonString(json, "avatarPath");
        OnUserChanged?.Invoke();
    }
    public void OnGameHubWalletState(string json)
    {
        WalletCurrency = ReadJsonString(json, "currency") ?? WalletCurrency;

        var raw = ReadJsonNumber(json, "fluxCoins");
        if (!raw.HasValue) return;

        FluxCoins = (int)raw.Value;
        OnWalletChanged?.Invoke(FluxCoins);
    }

    public void OnGameHubWalletError(string json)
    {
        var message = ReadJsonString(json, "message") ?? "Wallet call failed.";
        Debug.LogWarning($"[GameHubBridge] Wallet error: {message}");
        OnWalletError?.Invoke(message);
    }

    public void OnGameHubPocketInput(string json) => Debug.Log($"[GameHubBridge] Pocket input: {json}");
    public void OnGameHubPocketPlayerJoined(string json) => Debug.Log($"[GameHubBridge] Pocket player joined: {json}");
    public void OnGameHubPocketPlayerReconnected(string json) => Debug.Log($"[GameHubBridge] Pocket player reconnected: {json}");
    public void OnGameHubPocketPlayerLeft(string json) => Debug.Log($"[GameHubBridge] Pocket player left: {json}");
    public void OnGameHubLeaderboardSharing(string json) => Debug.Log($"[GameHubBridge] Leaderboard sharing: {json}");

    private static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string Invariant(double value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
