# Repository Instructions

## Commands
- Requires a .NET SDK that can target `net10.0`; the app project is `PackingTracker.Gui/PackingTracker.Gui.csproj`.
- Build and restore from the repo root with `dotnet build PackingTracker.sln`.
- Run the desktop app with `dotnet run --project PackingTracker.Gui/PackingTracker.Gui.csproj`; this opens the Avalonia GUI, so do not use it as an automated verification step.
- There are no test projects currently. `dotnet test PackingTracker.sln` only restores and exits, so use `dotnet build PackingTracker.sln` as the focused verification unless tests are added.

## Architecture
- This is a single Avalonia desktop app named "All Packed"; `PackingTracker.sln` only includes `PackingTracker.Gui` and maps x86/x64 solution configs to `Any CPU`.
- Startup flow is `Program.cs` -> `App.axaml.cs` -> `MainWindow`; the UI is primarily `MainWindow.axaml` with code-behind in `MainWindow.axaml.cs`.
- There is no MVVM layer in the current code. `MainWindow.axaml.cs` owns profiles, categories, item state, dashboard refreshes, and event handlers.
- Persistence is centralized in `PackingStorage.cs` using `Microsoft.Data.Sqlite`; `PackingItem.cs` is the observable item model.

## Persistence Gotchas
- When running from this repo or build output, `PackingStorage.GetStorageDirectory()` walks upward for `PackingTracker.sln` and writes to repo-root `data/`.
- `data/*.db`, SQLite sidecar files, and legacy `data/*.txt` files are intentionally ignored; do not assume local packing data is safe to delete or commit.
- SQLite schema creation is lazy on first storage access. Legacy `data/packing-list-*.txt` and `data/last-profile.txt` migrate once into SQLite, tracked by `app_state.legacy_text_migration_completed`.
- If no solution file is found, storage falls back to `Environment.SpecialFolder.ApplicationData/All Packed`, then `AppContext.BaseDirectory/data`.
