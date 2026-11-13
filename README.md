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

### Probe Order
With *Prioritize order* on (default), each run probes the items most likely to be watched first:
1. items matching a preferred original language, production country, tag or title prefix;
2. all other items;
3. items rated below the minimum rating, if one is set.

Inside a group the score is `rating weight × rating + recency weight × recency` (an unknown
rating counts as 5; recency runs from 1 for this year to 0 for ten years or older, with a bonus
for items added in the last 30 days). Items within one *randomness band* are probed in random
order, so runs do not walk the catalogue in a fixed sequence.

### Stream Cache
Every time a STRM item gets stream information (by this plugin or by a normal playback), a copy
is written to the plugin's data folder, keyed by the item's path and TMDb id. When the item is
removed and recreated, for example after the source behind the STRM files rebuilt its catalogue,
the streams are put back from that copy without contacting the upstream server. The first start
of a restored item probes once more, in case the file behind the title changed.

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
| Prioritize order | On | On/Off | Preferred items first, then rating and recency, random inside bands |
| Preferred languages | `en` | list | Original languages that go first (empty: none) |
| Preferred countries / tags / title prefixes | empty | lists | Further ways to mark items as preferred |
| Rating weight / Recency weight | 1 / 3 | 0–10 / 0–50 | How much rating and a recent release count |
| Minimum rating | 0 (off) | 0–10 | Items rated below this go last |
| Randomness band | 1 | 0–100 | Score width inside which the order is random |
| Stream cache | On | On/Off | Keep probe results outside the database and restore them without a probe |
| Probe again after restore | On | On/Off | One real probe on the first start of a restored item |
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
