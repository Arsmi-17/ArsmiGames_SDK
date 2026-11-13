# ArsmiGames platform save contract

Games using **Save progress through ArsmiGames** (`game_data_save_preference = "sdk"`) mirror a complete `Record<string, string>` progress snapshot through the Data Module. The game's local save remains useful for offline play. The account snapshot lets progress follow a signed-in player to another device.

1. Treat the save map as one atomic snapshot. Related keys must be written together.
2. Wait for the platform save before writing. The SDK rejects writes made before `init()` (web) or `DataReady` (Unity).
3. Do not create a save for a player who has no progress.
4. On restore, adopt the platform snapshot whole. Do not blend individual keys with a local snapshot.
5. When a guest signs in, the platform chooses one whole snapshot; an existing account save wins over a guest save.

The SDK Data Module is active only in `sdk` save mode. In `no` and `backend` modes, progress writes to ArsmiGames are disabled. Identity/profile requests use `GameHubBridge.user.fetch()` on web or `RequestUserState()` in Unity; they do not include save data. Wallet data also uses separate SDK calls.

For examples and current APIs, see the Web or Unity integration guide distributed with your SDK. During upload, run the playable Preview & QA stage and verify a restore on a second browser or device before requesting publication.
