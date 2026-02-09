# JellySTRMprobe

A Jellyfin plugin that extracts media information from STRM files by probing remote streams.

## The Problem

Jellyfin treats STRM files as "shortcuts" and **never probes them during library scans**. This means movies and episodes sourced from STRM files show up with no duration, codec, resolution, or audio information — even after a full library scan. Media info is only populated on first playback.

**Without this plugin:**

- No duration, codec, resolution, or audio info in the UI
- Movies may be marked as "played" after a few seconds (no duration known)
- Filter/search by resolution or codec doesn't work
- The version picker can't distinguish between quality variants

## How It Works

The plugin calls Jellyfin's internal `RefreshSingleItem()` with `EnableRemoteContentProbe = true` — the same flag Jellyfin sets during playback. This triggers ffprobe against the STRM target URL without requiring the user to play every item first.

## Features

### Scheduled Task
A scheduled task (Dashboard > Scheduled Tasks > **Probe STRM Media Info**) that:
- Finds all STRM items with no media stream data
- Probes them in parallel with configurable concurrency
- Runs daily at 4:00 AM by default (customizable)
- Reports progress and is cancellable from the Dashboard

### Catch-Up Mode
Automatically probes new STRM items as they're added during library scans:
- Subscribes to Jellyfin's `ItemAdded` event
- Debounces for 30 seconds to batch process items
- Enabled by default (can be disabled in settings)

### Configuration
All settings are accessible from Dashboard > Plugins > JellySTRMprobe:

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| Catch-Up Mode | Enabled | On/Off | Auto-probe new STRM items when added |
| Parallelism | 5 | 1–20 | Concurrent probe operations |
| Timeout | 60s | 10–300s | Per-item probe timeout |
| Cooldown | 200ms | 0–5000ms | Delay between probes (prevents upstream overload) |
| Libraries | All | Multi-select | Which libraries to probe |

## Installation

### From Plugin Repository (Recommended)

1. In Jellyfin, go to **Dashboard > Plugins > Repositories**
2. Add repository URL:
   ```
   https://firestaerter3.github.io/jellyfin-plugin-repo/manifest.json
   ```
3. Go to **Catalog**, find **JellySTRMprobe**, and install
4. Restart Jellyfin

### Manual Installation

1. Download the latest release from [Releases](https://github.com/firestaerter3/JellySTRMprobe/releases)
2. Extract `JellySTRMprobe.dll` to your Jellyfin plugins directory:
   ```
   <jellyfin-data>/plugins/JellySTRMprobe/JellySTRMprobe.dll
   ```
3. Restart Jellyfin

## Requirements

- Jellyfin **10.11.0** or later
- .NET 9.0

## Building from Source

```bash
dotnet build -c Release
dotnet test -c Release
dotnet publish JellySTRMprobe -c Release -o ./publish
```

## Prior Art

| Project | Platform | Notes |
|---------|----------|-------|
| [StrmAssistant](https://github.com/sjtuross/StrmAssistant) | Emby | Emby-only, incompatible with Jellyfin |
| [JellyfinStrmExtract](https://github.com/gauthier-th/JellyfinStrmExtract) | Jellyfin | Sequential processing, targets Jellyfin 10.9.x |

## License

Licensed under the GPL-3.0 License. See [LICENSE](LICENSE) for details.
