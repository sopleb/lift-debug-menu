# Lift Debug Menu

A BepInEx plugin that adds a working cheat console to *The Lift* (Steam playtest). The release build ships Quantum Console but strips the developer commands out of the binary. This plugin puts the useful ones back as native console commands.

Built for game build `23709352` (Unity 6000.2.7f2, IL2CPP). Tested on Linux through Proton.

## Install

1. Download the latest `Lift-DebugMenu-*.zip` from [Releases](../../releases).
2. Extract it into the game folder, next to `Handyman.exe`. You should end up with `winhttp.dll`, `dotnet/`, and `BepInEx/` sitting beside the executable.
3. Set the game's Steam launch options so Proton loads the BepInEx injector:
   ```
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```
4. Launch once and wait. The first launch builds BepInEx's interop assemblies and can sit on a black screen for up to two minutes. Let it reach the main menu.

To confirm it loaded, open `BepInEx/LogOutput.log` and look for `Lift Debug Menu: loaded`.

## Use

Load a save first so the player exists in the scene. Press **F1** to open the console, type a command, and press **Enter**:

| Command | Effect |
|---|---|
| `items [filter]` | list item names, or only those matching a filter |
| `give <name> [count]` | add an item to your inventory |
| `noclip` | toggle no-clip movement |
| `tp <x> <y> <z>` | teleport the player |
| `timescale <v>` | set the game speed |

The commands autocomplete next to Quantum Console's built-ins. Two hotkeys exist outside the console: **F2** toggles noclip, **F4** writes diagnostics to the log.

## Build from source

You need the .NET 6 SDK. The reference assemblies in `lib/` let the plugin compile without the game present.

```
dotnet build src/LiftDebug.csproj -c Release
```

Copy `src/bin/Release/LiftDebug.dll` into `BepInEx/plugins/`. The workflow in `.github/workflows/release.yml` does this on every `v*` tag and attaches a ready-to-extract zip to a GitHub release.

## How it works

The cheat commands call the game APIs that survive in the release build: the item database, the player inventory, player movement. The plugin injects a small IL2CPP `MonoBehaviour`, hands its methods to Quantum Console through `QuantumConsoleProcessor.TryAddCommand`, and prints results back with `LogToConsole`.

The dev build's `CheatingService` is compiled out of the release binary, so commands that lived there (device repair, power, recipe unlocks) are not included. Reviving those means rebuilding their logic against lower-level systems.

BepInEx here is the IL2CPP build from [builds.bepinex.dev](https://builds.bepinex.dev/projects/bepinex_be). The version on the BepInEx GitHub releases page is the Mono build and cannot load this game.

## Note

This targets an unreleased playtest. Use it on your own copy.

## License

MIT. See [LICENSE](LICENSE).
