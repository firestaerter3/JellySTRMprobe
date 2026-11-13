# JellySTRMprobe

A Jellyfin plugin that extracts media information from STRM files by probing remote streams.

## The Problem

Jellyfin treats STRM files as "shortcuts" and **never probes them during library scans**. This means movies and episodes sourced from STRM files show up with no duration, codec, resolution, or audio information — even after a full library scan. Media info is only populated on first playback.

**Without this plugin:**

- No duration, codec, resolution, or audio info in the UI
- Movies may be marked as "played" after a few seconds (no duration known)
- Filter/search by resolution or codec doesn't work
- The version picker can't distinguish between quality variants

**Every start is slow, too.** Jellyfin probes a STRM file and runs a full metadata
refresh (probe, NFO, TMDb, OMDb) on **every** playback, even when the streams are already
known. On the server this was measured on, that took about 10 seconds per start.

## How It Works

The plugin calls Jellyfin's internal `RefreshSingleItem()` with `EnableRemoteContentProbe = true` — the same flag Jellyfin sets during playback. This triggers ffprobe against the STRM target URL without requiring the user to play every item first.

## Features

### Fast Start
Playback of a STRM item that already has a video stream stored skips Jellyfin's probe and
metadata refresh, so it starts straight away. The plugin wraps Jellyfin's media source
manager and changes only that one call. An item that was never probed still gets
Jellyfin's normal probe on its first start: without stream information Jellyfin cannot
direct play, and the transcode it falls back to fails.

### Scheduled Task
A scheduled task (Dashboard > Scheduled Tasks > **Probe STRM Media Info**) that:
- Finds STRM items with no media stream data in the selected libraries
- Probes at most *Maximum items per run* of them, the rest in the next run
- Stops after *N* failures in a row (the upstream server is probably down)
- Waits while someone plays a STRM item, so a provider with one connection is not blocked
- Has **no schedule by default**: add a trigger, and a maximum run time, yourself
- Reports progress and is cancellable from the Dashboard

### Catch-Up Mode
Probes new STRM items in the selected libraries shortly after they are added:
- Subscribes to Jellyfin's `ItemAdded` event
- Debounces for 30 seconds to batch process items
- **Off by default**: after a large sync it starts many probes at any time of day

### Configuration
All settings are accessible from Dashboard > Plugins > JellySTRMprobe:

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| Fast Start | On | On/Off | Skip the probe on playback of already probed STRM items |
| Catch-Up Mode | Off | On/Off | Auto-probe new STRM items when added |
| Parallelism | 1 | 1–20 | Concurrent probe operations (each holds an upstream connection) |
| Timeout | 60s | 10–300s | Per-item probe timeout |
| Cooldown | 2000ms | 0–60000ms | Delay after each probe |
| Random extra pause | 0ms | 0–300000ms | Adds random 0–N ms to each cooldown |
| Random order | On | On/Off | Each run probes a random selection instead of A-B-C |
| Random start delay | 0 min | 0–240 min | Scheduled run waits random 0–N minutes first |
| Maximum items per run | 500 | 1–1000000 | Cap per task run or catch-up batch |
| Stop after failures in a row | 10 | 1–10000 | Stops a run when the upstream server seems down |
| Wait while a STRM item plays | On | On/Off | Pause probing during STRM playback |
| Libraries | All | Multi-select | Libraries for probing and fast start |

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

- Jellyfin **12.0** or later, .NET 10 (2.x)
- Jellyfin 10.11: use 1.x (`release/1.x` branch, release v1.2.0.0)

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
