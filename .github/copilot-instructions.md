# GitHub Copilot / AI Agent Instructions for CustomSpineLoader

Purpose: give an AI coding agent the minimal, actionable knowledge to be productive in this repository.

1) Big picture
- This repository is a BepInEx plugin for Cult of the Lamb that loads custom Spine skeletons and textures at runtime. See [Plugin.cs](Plugin.cs) for the entry/registration points. The plugin: registers follower commands (`CustomFollowerCommandManager.Add`), loads building overrides and uses Harmony patches.
- Runtime flow: `PlayerSpineLoader.LoadAllPlayerSpines()` and `FollowerSpineLoader.LoadAllFollowerSpines()` scan folders under the plugin installation path (`Plugin.PluginPath`) named `PlayerSkins` and `FollowerSkins`, read `.json` (Spine skeleton), `.atlas`, and `.png` files, build Spine runtime assets and register them via `CustomSkinManager`.

2) Key files & directories (quick jump)
- [Plugin.cs](Plugin.cs): registration, plugin path, logging hook.
- [README.md](README.md): user-facing installation and skin folder layout (PlayerSkins/PlayerName/config.json examples).
- [SpineLoaderHelper/PlayerSpineLoader.cs](SpineLoaderHelper/PlayerSpineLoader.cs): canonical example of how player skins are discovered, parsed, and registered.
- [SpineLoaderHelper/FollowerSpineLoader.cs](SpineLoaderHelper/FollowerSpineLoader.cs): similar for follower skins (layered default skins, `FollowerSpineConfig`).
- [Commands/CustomColorCommand.cs](Commands/CustomColorCommand.cs): demonstrates UI construction patterns and slot names used to recolor skins (e.g. `ARM_LEFT_SKIN`, `LEG_LEFT_SKIN`, `HEAD_SKIN_BTM`).
- `lib/COTL_API.dll`: referenced API used for `CustomFollowerCommand`, `CustomSkinManager`, and helpers.

3) Project-specific conventions & patterns
- Skin folders: under the plugin install path create one folder per skin (e.g. `PlayerSkins/DEBUGSKIN`) containing: one `.json` (skeleton), one `.atlas`, packed `.png` textures, and a `config.json` describing `DefaultSkin` and `Skins`.
- `config.json` format: see examples in README and classes `PlayerSpineConfig` / `FollowerSpineConfig` in helper files.
- Shader/material: runtime assets are created with `new Material(Shader.Find("Spine/Skeleton"))` — preserve or inspect in-game shader expectations if debugging render issues.
- Logging: use `Plugin.Log` (set in `Awake`) for diagnostic output.

4) Build / test / debug workflow (discoverable from repo)
- Build: this is a .NET project targeting `netstandard2.0`. Run `dotnet build` from repo root (or open the solution in Visual Studio). Output DLL appears under `bin\\Debug\\CustomSpineLoader\\CustomSpineLoader.dll` (see `CustomSpineLoader.csproj` OutputPath). Copy that DLL to your game's `BepInEx\\plugins\\CotLSpineLoader\\` to test in-game.
- Run-time testing: launch Cult of the Lamb with BepInEx installed and the plugin DLL present; check BepInEx console/logs and `Plugin.Log` messages for progress on loading skins.

5) Integration points & external dependencies
- Relies on `BepInEx` and `COTL_API` (`lib/COTL_API.dll`) for registration and custom commands.
- Uses `Spine.Unity` runtime APIs to create `SpineAtlasAsset` and `SkeletonDataAsset` at runtime (see loader helpers).
- Uses `Newtonsoft.Json` for parsing `config.json`.

6) What to watch for when editing
- UI code in `CustomColorCommand` manipulates game UI hierarchies; changes here should be validated in-game rather than by unit tests.

7) Concrete examples to use during edits
- To add support for a new skin format, follow the pattern in `SpineLoaderHelper/PlayerSpineLoader.cs` (scan folder, read files, create runtime assets, call `CustomSkinManager.AddPlayerSpine`).
- To add a new follower command, register it in `Plugin.Awake()` with `CustomFollowerCommandManager.Add(new YourCommand())` like `CustomColorCommand`.

8) When to ask the human
- If a change requires running the game to validate rendering, ask the user to run Cult of the Lamb with BepInEx. AI should not guess render/shader fixes without runtime verification.
- If `lib/COTL_API.dll` behavior is unclear (missing symbols), ask for the DLL or its reference docs.

If any section is unclear or you'd like me to include more examples (e.g., exact config.json examples, or a checklist for packaging to Thunderstore), tell me which area to expand and I will iterate.
