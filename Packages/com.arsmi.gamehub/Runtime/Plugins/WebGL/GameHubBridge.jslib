mergeInto(LibraryManager.library, {
  GameHubBridge_Init: function (gameObjectNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    window.__gameHubUnityReceiver = gameObjectName;

    if (!window.GameHubBridge && window.GameHubSDK) {
      window.GameHubBridge = window.GameHubSDK.create({
        capabilities: { challenge: true, pocketConsole: true, fullscreen: true, mute: true, achievements: true, leaderboard: true },
      });
    }

    window.__gameHubUnitySend = function (method, payload) {
      try {
        if (!window.__gameHubUnityReceiver || typeof SendMessage !== "function") return;
        SendMessage(window.__gameHubUnityReceiver, method, JSON.stringify(payload || {}));
      } catch (err) {
        console.warn("[GameHubUnity] SendMessage failed", err);
      }
    };

    if (window.GameHubBridge && !window.__gameHubUnityHandlersInstalled) {
      window.__gameHubUnityHandlersInstalled = true;
      if (window.GameHubBridge.onContext) window.GameHubBridge.onContext(function (payload) {
        window.__gameHubUnitySend("OnGameHubContext", payload);
      });
      window.GameHubBridge.on("set_mute", function (payload) {
        window.__gameHubUnitySend("OnGameHubMuted", payload);
      });
      window.GameHubBridge.on("set_fullscreen", function (payload) {
        window.__gameHubUnitySend("OnGameHubFullscreen", payload);
      });
      window.GameHubBridge.on("gamehub:user:state", function (payload) {
        window.__gameHubUnitySend("OnGameHubUserState", payload);
      });
      window.GameHubBridge.on("gamehub:wallet:state", function (payload) {
        window.__gameHubUnitySend("OnGameHubWalletState", payload);
      });
      window.GameHubBridge.on("gamehub:wallet:error", function (payload) {
        window.__gameHubUnitySend("OnGameHubWalletError", payload);
      });
      window.GameHubBridge.challenge.onStart(function (payload) {
        window.__gameHubUnitySend("OnGameHubChallengeStart", payload);
      });
      window.GameHubBridge.challenge.onLeaderboard(function (payload) {
        window.__gameHubUnitySend("OnGameHubChallengeLeaderboard", payload);
      });
      window.GameHubBridge.challenge.onEnd(function (payload) {
        window.__gameHubUnitySend("OnGameHubChallengeEnd", payload);
      });
      window.GameHubBridge.pocket.onInput(function (payload) {
        window.__gameHubUnitySend("OnGameHubPocketInput", payload);
      });
      window.GameHubBridge.pocket.onPlayerJoined(function (payload) {
        window.__gameHubUnitySend("OnGameHubPocketPlayerJoined", payload);
      });
      window.GameHubBridge.pocket.onPlayerReconnected(function (payload) {
        window.__gameHubUnitySend("OnGameHubPocketPlayerReconnected", payload);
      });
      window.GameHubBridge.pocket.onPlayerLeft(function (payload) {
        window.__gameHubUnitySend("OnGameHubPocketPlayerLeft", payload);
      });
      if (window.GameHubBridge.achievements && window.GameHubBridge.achievements.onSharing) window.GameHubBridge.achievements.onSharing(function (payload) {
        window.__gameHubUnitySend("OnGameHubAchievementsSharing", payload);
      });
      if (window.GameHubBridge.leaderboard && window.GameHubBridge.leaderboard.onSharing) window.GameHubBridge.leaderboard.onSharing(function (payload) {
        window.__gameHubUnitySend("OnGameHubLeaderboardSharing", payload);
      });
      // Save data. The whole map is pushed to C#, which keeps its own copy — so a
      // game reading a save key in Update() never crosses the JS boundary, and we
      // never have to malloc a string back into Unity on every read.
      window.GameHubBridge.on("gamehub:data:state", function (payload) {
        window.__gameHubUnitySend("OnGameHubDataState", payload);
      });
      window.GameHubBridge.on("gamehub:data:error", function (payload) {
        window.__gameHubUnitySend("OnGameHubDataError", payload);
      });
      // Ads are a platform overlay. Unity just gets told when one starts and how it
      // ended; it never renders or times the ad itself.
      window.GameHubBridge.on("gamehub:ad:state", function (payload) {
        window.__gameHubUnitySend("OnGameHubAdState", payload);
      });
    }
  },

  GameHubBridge_ShowRewardedAd: function (jsonPtr) {
    var payload = {};
    try { payload = JSON.parse(UTF8ToString(jsonPtr) || "{}"); } catch (_) {}
    if (window.GameHubBridge && window.GameHubBridge.ads) {
      window.GameHubBridge.ads.showRewarded(payload);
    }
  },

  GameHubBridge_DataSetItem: function (keyPtr, valuePtr) {
    var key = UTF8ToString(keyPtr);
    var value = UTF8ToString(valuePtr);
    window.GameHubBridge && window.GameHubBridge.data && window.GameHubBridge.data.setItem(key, value);
  },

  GameHubBridge_DataRemoveItem: function (keyPtr) {
    var key = UTF8ToString(keyPtr);
    window.GameHubBridge && window.GameHubBridge.data && window.GameHubBridge.data.removeItem(key);
  },

  GameHubBridge_DataClear: function () {
    window.GameHubBridge && window.GameHubBridge.data && window.GameHubBridge.data.clear();
  },

  GameHubBridge_DataFlush: function () {
    window.GameHubBridge && window.GameHubBridge.data && window.GameHubBridge.data.flush();
  },

  GameHubBridge_Emit: function (eventNamePtr, jsonPtr) {
    var eventName = UTF8ToString(eventNamePtr);
    var payload = {};
    try { payload = JSON.parse(UTF8ToString(jsonPtr) || "{}"); } catch (_) {}
    window.GameHubBridge && window.GameHubBridge.emit(eventName, payload);
  },

  GameHubBridge_Log: function (levelPtr, messagePtr, jsonPtr) {
    var level = UTF8ToString(levelPtr);
    var message = UTF8ToString(messagePtr);
    var json = jsonPtr ? UTF8ToString(jsonPtr) : "{}";
    var data = {};
    try { data = JSON.parse(json || "{}"); } catch (_) {}
    window.GameHubBridge && window.GameHubBridge.log(level, message, data);
  },

  GameHubBridge_RequestFullscreen: function (orientationPtr) {
    var orientation = UTF8ToString(orientationPtr || 0) || "auto";
    window.GameHubBridge && window.GameHubBridge.requestPlatformFullscreen(orientation);
  },

  GameHubBridge_RequestLogin: function (reasonPtr) {
    var reason = UTF8ToString(reasonPtr || 0) || "game";
    window.GameHubBridge && window.GameHubBridge.requestLogin && window.GameHubBridge.requestLogin(reason);
  },

  GameHubBridge_SetMuted: function (muted) {
    window.GameHubBridge && window.GameHubBridge.setMuted(!!muted);
  },

  GameHubBridge_ChallengeReady: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.challenge.ready(payload);
  },

  GameHubBridge_ChallengeState: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.challenge.updateState(payload);
  },

  GameHubBridge_ChallengeResult: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.challenge.submitResult(payload);
  },

  GameHubBridge_PocketReady: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.pocket.ready(payload);
  },

  GameHubBridge_PocketSchema: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.pocket.setControllerSchema(payload);
  },

  GameHubBridge_AchievementsDefine: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.achievements && window.GameHubBridge.achievements.define(payload);
  },

  GameHubBridge_AchievementProgress: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.achievements && window.GameHubBridge.achievements.progress(payload);
  },

  GameHubBridge_LeaderboardDefine: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.leaderboard && window.GameHubBridge.leaderboard.define(payload);
  },

  GameHubBridge_LeaderboardScore: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.leaderboard && window.GameHubBridge.leaderboard.submitScore(payload);
  },
});
