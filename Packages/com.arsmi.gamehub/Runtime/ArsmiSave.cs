using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArsmiGames
{
    /// <summary>
    /// Which of the three published "Save progress" answers this game is using.
    /// The platform stores this per game as `game_data_save_preference`.
    /// </summary>
    public enum SaveTarget
    {
        /// <summary>"No, the game does not need progress save".
        /// PlayerPrefs only. Progress dies with the browser profile — clear site data
        /// and it is gone, and it never follows the player to another device.</summary>
        LocalOnly = 0,

        /// <summary>"The game saves data locally and mirrors it to Arsmi Games".
        /// Writes go to PlayerPrefs *and* to the platform. The local copy is what the
        /// game reads; the platform copy is the backup that survives a new device.</summary>
        PlatformMirror = 1,

        /// <summary>"Linked to a game account on your own backend".
        /// The platform stores nothing. We ask it who the player is (PlayerId) and
        /// read/write our own database with that.</summary>
        OwnBackend = 2,
    }

    /// <summary>
    /// One save API over the three modes, so the game's own code does not care which
    /// one it was published with.
    ///
    /// In PlatformMirror the local copy is authoritative for *reads* — the game must
    /// never block a frame on the network. The platform copy is a mirror, and when it
    /// comes back with something newer (the player was on another device, or a guest
    /// just signed in) it overwrites local and raises <see cref="OnExternalChange"/>.
    /// </summary>
    public class ArsmiSave : MonoBehaviour
    {
        public static ArsmiSave Instance { get; private set; }

        private const string PrefsPrefix = "arsmi.save.";

        public SaveTarget Target { get; private set; } = SaveTarget.LocalOnly;

        /// <summary>Raised when the store replaced our values behind the game's back.
        /// Re-read everything you display — ignoring this is how progress gets lost.</summary>
        public event Action OnExternalChange;

        public event Action<string> OnError;

        private ArsmiBackendClient _backend;
        private readonly HashSet<string> _knownKeys = new HashSet<string>();
        private bool _backendLoaded;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public void Configure(SaveTarget target, ArsmiBackendClient backend = null)
        {
            Target = target;
            _backend = backend;
            _backendLoaded = false;

            if (target == SaveTarget.PlatformMirror)
            {
                var hub = GameHubBridge.Instance;
                if (hub != null)
                {
                    hub.OnDataChanged -= AdoptPlatformData;
                    hub.OnDataChanged += AdoptPlatformData;
                    hub.OnDataError -= RaiseError;
                    hub.OnDataError += RaiseError;
                    if (hub.DataReady) AdoptPlatformData();
                }
            }

            if (target == SaveTarget.OwnBackend) LoadFromBackend();
        }

        /// <summary>True when it is safe to read progress. Local modes are ready at once;
        /// the others have to hear back first.</summary>
        public bool IsReady
        {
            get
            {
                switch (Target)
                {
                    case SaveTarget.PlatformMirror: return GameHubBridge.Instance != null && GameHubBridge.Instance.DataReady;
                    case SaveTarget.OwnBackend: return _backendLoaded;
                    default: return true;
                }
            }
        }

        public string GetString(string key, string fallback = "")
        {
            var value = PlayerPrefs.GetString(PrefsPrefix + key, null);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        public int GetInt(string key, int fallback = 0)
        {
            var raw = GetString(key, null);
            return int.TryParse(raw, out var v) ? v : fallback;
        }

        public void SetString(string key, string value)
        {
            _knownKeys.Add(key);

            // The local copy is written in every mode, including OwnBackend — it is
            // what the game reads, so a slow or failed network call can never stall
            // or corrupt gameplay.
            PlayerPrefs.SetString(PrefsPrefix + key, value ?? "");
            PlayerPrefs.Save();

            switch (Target)
            {
                case SaveTarget.PlatformMirror:
                    GameHubBridge.Instance?.SetItem(key, value ?? "");
                    break;
                case SaveTarget.OwnBackend:
                    PushToBackend();
                    break;
            }
        }

        public void SetInt(string key, int value) => SetString(key, value.ToString());

        public void ClearAll()
        {
            foreach (var key in _knownKeys) PlayerPrefs.DeleteKey(PrefsPrefix + key);
            PlayerPrefs.Save();

            switch (Target)
            {
                case SaveTarget.PlatformMirror:
                    GameHubBridge.Instance?.ClearData();
                    break;
                case SaveTarget.OwnBackend:
                    PushToBackend();
                    break;
            }
            OnExternalChange?.Invoke();
        }

        /// <summary>Force the platform mirror out now rather than waiting for the debounce.</summary>
        public void Flush()
        {
            if (Target == SaveTarget.PlatformMirror) GameHubBridge.Instance?.FlushData();
            if (Target == SaveTarget.OwnBackend) PushToBackend();
        }

        // ---- PlatformMirror ------------------------------------------------------

        private void AdoptPlatformData()
        {
            var hub = GameHubBridge.Instance;
            if (hub == null) return;

            // The platform is authoritative when it speaks: this fires on first load,
            // after a guest's progress is merged into their account, and when another
            // device turns out to be ahead of us. Keeping the local copy in any of
            // those cases would roll the player back.
            foreach (var key in hub.Keys)
            {
                _knownKeys.Add(key);
                PlayerPrefs.SetString(PrefsPrefix + key, hub.GetItem(key, ""));
            }
            PlayerPrefs.Save();
            OnExternalChange?.Invoke();
        }

        // ---- OwnBackend ----------------------------------------------------------

        private void LoadFromBackend()
        {
            if (_backend == null)
            {
                RaiseError("No backend configured.");
                return;
            }

            var playerId = GameHubBridge.Instance != null ? GameHubBridge.Instance.PlayerId : null;
            if (string.IsNullOrEmpty(playerId))
            {
                // Guest, or the user state has not arrived yet. Try again when it does.
                if (GameHubBridge.Instance != null)
                {
                    GameHubBridge.Instance.OnUserChanged -= LoadFromBackend;
                    GameHubBridge.Instance.OnUserChanged += LoadFromBackend;
                }
                return;
            }

            StartCoroutine(_backend.Load(playerId, (map, error) =>
            {
                if (error != null) { RaiseError(error); return; }

                foreach (var pair in map)
                {
                    _knownKeys.Add(pair.Key);
                    PlayerPrefs.SetString(PrefsPrefix + pair.Key, pair.Value);
                }
                PlayerPrefs.Save();
                _backendLoaded = true;
                OnExternalChange?.Invoke();
            }));
        }

        private void PushToBackend()
        {
            if (_backend == null) return;
            var playerId = GameHubBridge.Instance != null ? GameHubBridge.Instance.PlayerId : null;
            if (string.IsNullOrEmpty(playerId)) { RaiseError("Sign in to save to the game's backend."); return; }

            var map = new Dictionary<string, string>();
            foreach (var key in _knownKeys) map[key] = PlayerPrefs.GetString(PrefsPrefix + key, "");

            StartCoroutine(_backend.Save(playerId, map, error =>
            {
                if (error != null) RaiseError(error);
            }));
        }

        private void RaiseError(string message) => OnError?.Invoke(message);
    }
}
