# JellySTRMprobe — Requirements

## Overview

JellySTRMprobe is a standalone Jellyfin plugin that extracts media information (codec, resolution, duration, audio channels) from STRM file targets by probing the remote streams. Jellyfin's built-in library scan never probes STRM files — this plugin fills that gap.

## Background

### The Problem

Jellyfin treats STRM files as "shortcuts" (`IsShortcut = true`). During library scans, the Probe Provider checks:

```csharp
if (!item.IsShortcut || options.EnableRemoteContentProbe)
```

`EnableRemoteContentProbe` defaults to `false` during scans. It is only set to `true` in `MediaSourceManager.GetPlaybackMediaSources()` — meaning STRM files are only probed on **first playback**, never during library scans.

**Impact:**
- Movies show no duration, codec, resolution, or audio info in the UI
- Jellyfin's version picker can't distinguish between quality variants
- Movies may be marked as "played" after seconds (no duration known)
- Search/filter by resolution or codec is impossible

### Prior Art

| Project | Platform | Status | Limitations |
|---------|----------|--------|-------------|
| [StrmAssistant](https://github.com/sjtuross/StrmAssistant) | Emby | Active, 1900+ stars | Emby-only, incompatible with Jellyfin |
| [JellyfinStrmExtract](https://github.com/gauthier-th/JellyfinStrmExtract) | Jellyfin | Minimal, 5 commits | Sequential processing, no catch-up mode, targets Jellyfin 10.9.x |
| [StrmExtract](https://github.com/faush01/StrmExtract) | Emby | Original | JellyfinStrmExtract is based on this |

## Functional Requirements

### FR-1: Scheduled Probing Task

| ID | Requirement |
|----|-------------|
| FR-1.1 | The plugin SHALL register a scheduled task visible in Dashboard > Scheduled Tasks |
| FR-1.2 | The task SHALL query all video items in configured libraries |
| FR-1.3 | The task SHALL filter for items with `.strm` file extension AND zero media streams |
| FR-1.4 | The task SHALL call `IProviderManager.RefreshSingleItem()` with `EnableRemoteContentProbe = true` for each unprobed item |
| FR-1.5 | The task SHALL support parallel probing with configurable concurrency (default: 5) |
| FR-1.6 | The task SHALL report progress to Jellyfin's task system (0-100%) |
| FR-1.7 | The task SHALL default to running daily at 4:00 AM |
| FR-1.8 | The task SHALL be cancellable via the Dashboard |
| FR-1.9 | The task SHALL log the count of probed, failed, and skipped items |

### FR-2: Catch-up Mode (Auto-Probe)

| ID | Requirement |
|----|-------------|
| FR-2.1 | The plugin SHALL optionally subscribe to `ILibraryManager.ItemAdded` events |
| FR-2.2 | When a new `.strm` item is added, the plugin SHALL queue it for probing |
| FR-2.3 | The plugin SHALL debounce queue processing — wait 30 seconds after the last add event before processing |
| FR-2.4 | Queue processing SHALL use the same parallelism settings as the scheduled task |
| FR-2.5 | Catch-up mode SHALL be enabled/disabled via plugin configuration (default: enabled) |
| FR-2.6 | The plugin SHALL log catch-up probe results |

### FR-3: Configuration

| ID | Requirement |
|----|-------------|
| FR-3.1 | The plugin SHALL provide a configuration page accessible from the Jellyfin Dashboard |
| FR-3.2 | Configurable: Catch-up mode enable/disable (default: true) |
| FR-3.3 | Configurable: Probe parallelism 1-20 (default: 5) |
| FR-3.4 | Configurable: Per-item probe timeout 10-300 seconds (default: 60) |
| FR-3.5 | Configurable: Cooldown between probes 0-5000 ms (default: 200) |
| FR-3.6 | Configurable: Library selection — which libraries to probe (default: all) |
| FR-3.7 | The configuration UI SHALL display a library multi-selector populated from Jellyfin's virtual folders |

### FR-4: Metadata Preservation

| ID | Requirement |
|----|-------------|
| FR-4.1 | Probing SHALL NOT replace existing metadata (title, year, description, etc.) |
| FR-4.2 | Probing SHALL NOT replace existing images (posters, backdrops, etc.) |
| FR-4.3 | Probing SHALL only add/update media stream information (video, audio, subtitle streams) |
| FR-4.4 | Items that already have media streams SHALL be skipped |

## Non-Functional Requirements

### NFR-1: Performance

| ID | Requirement |
|----|-------------|
| NFR-1.1 | Probing 15,000 items at parallelism=5 with 200ms cooldown SHALL complete within reasonable time (~18 hours) |
| NFR-1.2 | The plugin SHALL NOT block Jellyfin's main thread or degrade UI responsiveness |
| NFR-1.3 | Cooldown between probes SHALL prevent connection exhaustion on upstream servers |

### NFR-2: Reliability

| ID | Requirement |
|----|-------------|
| NFR-2.1 | Individual probe failures SHALL NOT stop the overall batch |
| NFR-2.2 | Timeout per probe SHALL prevent hanging on unresponsive remote streams |
| NFR-2.3 | Failed items SHALL remain unprobed and be retried on the next scheduled task run |
| NFR-2.4 | The plugin SHALL handle Jellyfin restarts gracefully (catch-up re-subscribes on startup) |

### NFR-3: Compatibility

| ID | Requirement |
|----|-------------|
| NFR-3.1 | Target: .NET 9.0 |
| NFR-3.2 | Target: Jellyfin 10.11.0+ |
| NFR-3.3 | The plugin SHALL work alongside other Jellyfin plugins without conflicts |
| NFR-3.4 | The plugin SHALL work with any STRM URL format (HTTP, HTTPS, local file paths) |

### NFR-4: Code Quality

| ID | Requirement |
|----|-------------|
| NFR-4.1 | TreatWarningsAsErrors enabled |
| NFR-4.2 | Nullable reference types enabled |
| NFR-4.3 | StyleCop analyzers enabled |
| NFR-4.4 | XML documentation for all public members |
