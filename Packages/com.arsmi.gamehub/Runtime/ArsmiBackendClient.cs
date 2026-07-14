using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ArsmiGames
{
    /// <summary>
    /// Talks to *the game's own* backend — this is what "Yes, linked to a game account
    /// on your own backend" means in the publish wizard. Arsmi Games stores nothing in
    /// this mode; it only tells us who the player is.
    ///
    /// The example backend here is Supabase over its REST API, because that is what a
    /// developer would most plausibly reach for. Swap the two coroutines for your own
    /// endpoints and nothing else in the game changes.
    ///
    /// Rows are keyed on <c>player_id</c> — the pseudonymous, per-game id the platform
    /// hands us. Never key on the raw platform user id: it is the same value in every
    /// game, so any two backends could compare notes and identify the person.
    /// </summary>
    [Serializable]
    public class ArsmiBackendClient
    {
        public string Url;      // https://<project>.supabase.co
        public string AnonKey;  // the publishable anon key — safe in a client build
        public string Table = "game_platform_demo_quiz_saves";

        public bool IsConfigured => !string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(AnonKey);

        private string Endpoint => $"{Url.TrimEnd('/')}/rest/v1/{Table}";

        private void ApplyHeaders(UnityWebRequest req)
        {
            req.SetRequestHeader("apikey", AnonKey);
            req.SetRequestHeader("Authorization", "Bearer " + AnonKey);
            req.SetRequestHeader("Content-Type", "application/json");
        }

        /// <summary>Reads this player's save row. A player who has never saved is not an
        /// error — it yields an empty map.</summary>
        public IEnumerator Load(string playerId, Action<Dictionary<string, string>, string> done)
        {
            if (!IsConfigured) { done(null, "Backend is not configured (URL / anon key)."); yield break; }

            var url = $"{Endpoint}?player_id=eq.{UnityWebRequest.EscapeURL(playerId)}&select=data";
            using (var req = UnityWebRequest.Get(url))
            {
                ApplyHeaders(req);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    done(null, $"Backend load failed: {req.responseCode} {req.error}");
                    yield break;
                }

                var body = req.downloadHandler.text ?? "[]";
                var open = body.IndexOf('{');
                if (open < 0)
                {
                    done(new Dictionary<string, string>(), null); // no row yet
                    yield break;
                }

                var map = GameHubBridge.ParseStringMap(body, "data");
                done(map ?? new Dictionary<string, string>(), null);
            }
        }

        /// <summary>Upserts the player's whole save map.</summary>
        public IEnumerator Save(string playerId, Dictionary<string, string> map, Action<string> done)
        {
            if (!IsConfigured) { done("Backend is not configured (URL / anon key)."); yield break; }

            var json = new StringBuilder();
            json.Append("{\"player_id\":\"").Append(EscapeJson(playerId)).Append("\",\"data\":{");
            var first = true;
            foreach (var pair in map)
            {
                if (!first) json.Append(',');
                first = false;
                json.Append('"').Append(EscapeJson(pair.Key)).Append("\":\"").Append(EscapeJson(pair.Value)).Append('"');
            }
            json.Append("}}");

            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            using (var req = new UnityWebRequest(Endpoint, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(bytes);
                req.downloadHandler = new DownloadHandlerBuffer();
                ApplyHeaders(req);
                // Upsert: one row per player, overwritten in place.
                req.SetRequestHeader("Prefer", "resolution=merge-duplicates,return=minimal");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    done($"Backend save failed: {req.responseCode} {req.error} {req.downloadHandler.text}");
                    yield break;
                }
                done(null);
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
