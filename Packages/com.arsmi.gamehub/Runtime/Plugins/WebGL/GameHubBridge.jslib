mergeInto(LibraryManager.library, {
  GameHubBridge_Init: function (gameObjectNamePtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    window.__gameHubUnityReceiver = gameObjectName;

    // Unity mode turns OFF the SDK's automatic wiring detection, and its automatic acks.
    //
    // Below, this file subscribes to set_mute, set_fullscreen and the rest on the game's
    // behalf, unconditionally — it has to, it cannot know what the C# will want. If the SDK
    // inferred "the game handles mute" from those subscriptions, EVERY Unity build would look
    // compliant, including one whose C# ignores the platform's volume button completely. So
    // GameHubBridge.cs looks at its own event subscriptions and reports the truth, through
    // GameHubBridge_ReportWiring and GameHubBridge_Ack.
    //
    // It has to be done in BOTH branches. The WebGL template loads gamehub-sdk.js in <head>,
    // so in a real build window.GameHubBridge is nearly always already here and the create()
    // below never runs — which left every Unity build in auto-wiring mode, quietly reporting
    // that it implemented everything.
    //
    // And it has to happen BEFORE the subscriptions below, or they are what gets counted.
    if (!window.GameHubBridge && window.GameHubSDK) {
      window.GameHubBridge = window.GameHubSDK.create({
        capabilities: { challenge: true, pocketConsole: true, fullscreen: true, mute: true, leaderboard: true },
        engine: "unity",
      });
    } else if (window.GameHubBridge && window.GameHubBridge.setEngine) {
      window.GameHubBridge.setEngine("unity");
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

  /**
   * C# answering for a platform message this file handed it.
   *
   * The handlers above subscribe to set_mute and set_fullscreen unconditionally, so the SDK
   * cannot tell from JavaScript whether the GAME is listening — only C# knows that. It parks
   * the message id and waits for this call. See UNITY_ACKS in the SDK.
   */
  GameHubBridge_Ack: function (eventPtr, handled) {
    var eventName = UTF8ToString(eventPtr);
    if (!window.GameHubBridge || !window.GameHubBridge.ackEvent) return;
    window.GameHubBridge.ackEvent(eventName, !!handled);
  },

  /** C# telling us what its game actually subscribed to. See engine:"unity" above. */
  GameHubBridge_ReportWiring: function (jsonPtr) {
    if (!window.GameHubBridge || !window.GameHubBridge.setWiring) return;
    try {
      window.GameHubBridge.setWiring(JSON.parse(UTF8ToString(jsonPtr)));
    } catch (err) {
      console.warn("[GameHubUnity] bad wiring report", err);
    }
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

  GameHubBridge_LeaderboardDefine: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.leaderboard && window.GameHubBridge.leaderboard.define(payload);
  },

  GameHubBridge_LeaderboardScore: function (jsonPtr) {
    var payload = JSON.parse(UTF8ToString(jsonPtr) || "{}");
    window.GameHubBridge && window.GameHubBridge.leaderboard && window.GameHubBridge.leaderboard.submitScore(payload);
  },
});
