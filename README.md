# All Packed

All Packed is a desktop packing-list app for planning trips, tracking what is already in the bag, and keeping separate lists for different travel profiles.

Built with Avalonia and .NET, it stores lists locally in SQLite so your packing data stays on your Mac.

## Features

- Create and switch between saved packing profiles, such as different trips or people.
- Add items with quantities and categories.
- Check items off as packed, with progress based on item quantities.
- Bulk mark the visible list as packed or unpacked.
- Search by item or category, then filter by all, packed, or unpacked items.
- Expand and collapse category groups to keep long lists manageable.
- Edit items, delete items, and undo the most recent item deletion.
- Autosave changes locally and reopen the last used profile on startup.

## Download And Run

This repository contains the source code. If you already have a packaged Mac build, open `All Packed.app` like any other macOS application.

On first launch, macOS may warn that the app is from an unidentified developer if the build is not notarized. Right-click the app, choose `Open`, then confirm once.

## Build From Source

Requirements:

- A .NET SDK that can target `net10.0`
- macOS for the packaged `.app` workflow shown below

Build the solution:

```bash
dotnet build PackingTracker.sln
```

Run from source:

```bash
dotnet run --project PackingTracker.Gui/PackingTracker.Gui.csproj
```

Publish a self-contained Apple Silicon build:

```bash
dotnet publish PackingTracker.Gui/PackingTracker.Gui.csproj \
  -c Release \
  -r osx-arm64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o artifacts/publish/osx-arm64
```

The published native executable is `artifacts/publish/osx-arm64/PackingTracker.Gui`. To distribute it as a double-clickable Mac app, wrap the publish output in a standard `.app` bundle.

## Local Data

All Packed saves data locally in `all-packed.db`.

- When running from this repo or its build output, data is stored in `data/all-packed.db` at the repository root.
- When running as a standalone app outside the repo, data is stored in `~/Library/Application Support/All Packed/all-packed.db`.

The database is intentionally ignored by git. Replacing or rebuilding the app should not delete saved packing lists as long as the storage folder and database name stay the same.

## Roadmap

Near-term polish:

- Package a fresh Mac `.app` build from the current source.
- Add a real app icon and review window/app metadata.
- Manually test a clean install, existing saved data, and the packaged app workflow.

Accessibility and layout:

- Improve keyboard navigation and shortcuts, such as `Enter` to add an item and `Cmd+S` to save.
- Review focus states, tooltips, labels, and screen-reader friendliness.
- Make the layout more responsive for smaller windows and future tablet-style screen sizes.

Future feature ideas:

- Add import/export or backup for packing profiles.
- Add optional category management.
- Add drag-and-drop item ordering.
- Explore a more modern multi-page or tabbed layout if the app grows beyond the single-dashboard workflow.
