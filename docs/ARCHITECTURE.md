# JellySTRMprobe — Architecture

## Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Jellyfin Server                          │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │              JellySTRMprobe Plugin                     │  │
│  │                                                       │  │
│  │  ┌──────────────┐    ┌─────────────────────────────┐  │  │
│  │  │   Plugin.cs   │    │  PluginConfiguration.cs     │  │  │
│  │  │  Entry point   │    │  Settings model             │  │  │
│  │  └──────────────┘    └─────────────────────────────┘  │  │
│  │                                                       │  │
│  │  ┌──────────────────────────────────────────────────┐ │  │
│  │  │              ProbeService.cs                      │ │  │
│  │  │  - GetUnprobedItems() — query + filter            │ │  │
│  │  │  - ProbeItemAsync() — single item probe           │ │  │
│  │  │  - ProbeBatchAsync() — parallel batch probe       │ │  │
│  │  └───────────────────┬──────────────────────────────┘ │  │
│  │                      │                                │  │
│  │       ┌──────────────┼────────────────┐               │  │
│  │       │              │                │               │  │
│  │  ┌────▼────┐  ┌──────▼──────┐  ┌─────▼──────────┐    │  │
│  │  │Scheduled│  │  Catch-up   │  │  Config UI     │    │  │
│  │  │  Task   │  │ EntryPoint  │  │  (HTML + JS)   │    │  │
│  │  └─────────┘  └─────────────┘  └────────────────┘    │  │
│  │                                                       │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  Jellyfin APIs used:                                        │
│  - IProviderManager.RefreshSingleItem()                     │
│  - ILibraryManager.GetItemList()                            │
│  - ILibraryManager.ItemAdded event                          │
│  - IFileSystem (DirectoryService)                           │
│  - BaseItem.GetMediaStreams()                                │
└─────────────────────────────────────────────────────────────┘
```

## Project Structure

```
JellySTRMprobe/
├── JellySTRMprobe/                          # Main plugin project
│   ├── JellySTRMprobe.csproj
│   ├── Plugin.cs                            # BasePlugin<PluginConfiguration>, IHasWebPages
│   ├── PluginConfiguration.cs               # Settings: parallelism, timeout, cooldown, libraries
│   ├── PluginServiceRegistrator.cs          # DI: ProbeService, ProbeStrmTask
│   ├── Service/
│   │   ├── IProbeService.cs                 # Service interface
│   │   └── ProbeService.cs                  # Core probing logic
│   ├── Tasks/
│   │   └── ProbeStrmTask.cs                 # IScheduledTask implementation
│   ├── EntryPoint/
│   │   └── CatchUpEntryPoint.cs             # IHostedService, ItemAdded handler
│   └── Configuration/Web/
│       ├── config.html                      # Plugin settings page
│       └── config.js                        # Settings form logic
├── JellySTRMprobe.Tests/                    # Unit tests
│   ├── JellySTRMprobe.Tests.csproj
│   └── Service/
│       └── ProbeServiceTests.cs
├── jellyfin.ruleset                         # Code analysis rules
├── JellySTRMprobe.sln                       # Solution file
├── CLAUDE.md                                # Project context for Claude
├── README.md                                # User-facing documentation
└── docs/
    ├── REQUIREMENTS.md                      # Functional & non-functional requirements
    ├── ARCHITECTURE.md                      # This file
    └── TESTCASES.md                         # Test specifications
```

## Core Mechanism

### How Jellyfin Blocks STRM Probing

In Jellyfin's `ProbeProvider.cs`:
```csharp
if (!item.IsShortcut || options.EnableRemoteContentProbe)
```

- STRM files → `IsShortcut = true`
- Library scan → `EnableRemoteContentProbe = false` (default)
- Result: STRM files are skipped during library scan probing

### How This Plugin Bypasses the Gate

```csharp
var refreshOptions = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
{
    EnableRemoteContentProbe = true,       // KEY: enables ffprobe for STRM
    MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
    ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
    ReplaceAllMetadata = false,            // Don't touch existing metadata
    ReplaceAllImages = false,              // Don't touch existing images
};
await _providerManager.RefreshSingleItem(item, refreshOptions, cancellationToken);
```

This triggers Jellyfin's internal ffprobe pipeline:
1. Reads the URL from the STRM file
2. Runs ffprobe against the remote URL
3. Parses video/audio/subtitle stream info
4. Stores MediaStreams in the database
5. Item now shows codec, resolution, duration, audio channels in the UI

## Data Flow

### Scheduled Task Flow

```
ProbeStrmTask.ExecuteAsync()
    │
    ├─ ProbeService.GetUnprobedItems()
    │   ├─ ILibraryManager.GetItemList(MediaType.Video, Recursive)
    │   ├─ Filter: path.EndsWith(".strm")
    │   └─ Filter: GetMediaStreams().Count == 0
    │
    ├─ ProbeService.ProbeBatchAsync(items, parallelism, timeout, cooldown)
    │   ├─ Parallel.ForEachAsync(parallelism) controls concurrency
    │   ├─ For each item (in parallel):
    │   │   ├─ ProbeService.ProbeItemCoreAsync(item, timeout, directoryService)
    │   │   │   ├─ CancellationTokenSource with timeout
    │   │   │   ├─ IProviderManager.RefreshSingleItem(item, options)
    │   │   │   │   └─ Jellyfin internally runs ffprobe
    │   │   │   └─ Returns success/failure
    │   │   ├─ Increment probed/failed counter
    │   │   ├─ Report progress
    │   │   └─ Cooldown delay
    │   └─ Return (probed, failed)
    │
    └─ Log results
```

### Catch-up Flow

```
CatchUpEntryPoint.StartAsync()
    │
    ├─ Create CancellationTokenSource for shutdown
    ├─ Subscribe to ILibraryManager.ItemAdded
    │
    └─ On each ItemAdded event:
        ├─ Check EnableCatchUpMode (supports hot-reload)
        ├─ Filter: path.EndsWith(".strm")
        ├─ Enqueue item to ConcurrentQueue
        └─ Reset debounce timer (30 seconds)
            │
            └─ Timer fires:
                ├─ Drain queue to List
                ├─ Filter: GetMediaStreams().Count == 0
                └─ ProbeService.ProbeBatchAsync(items, ...)
```

## Dependency Injection

### Registered via `PluginServiceRegistrator`

| Service | Lifetime | Description |
|---------|----------|-------------|
| `IProbeService` → `ProbeService` | Singleton | Core probing logic |
| `ProbeStrmTask` | Singleton (as `IScheduledTask`) | Scheduled task |
| `CatchUpEntryPoint` | Hosted Service | IHostedService, starts on server boot |

### Auto-discovered by Jellyfin

| Service | Interface | Description |
|---------|-----------|-------------|
| `Plugin` | `BasePlugin<T>` | Auto-discovered plugin entry point |

### Injected from Jellyfin

| Service | Used By | Purpose |
|---------|---------|---------|
| `IProviderManager` | ProbeService | `RefreshSingleItem()` — triggers ffprobe |
| `ILibraryManager` | ProbeService, CatchUpEntryPoint | Query items, subscribe to events |
| `IFileSystem` | ProbeService | Create `DirectoryService` for `MetadataRefreshOptions` |
| `ILogger<T>` | All components | Structured logging |

## Configuration

### Settings Model (`PluginConfiguration`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `EnableCatchUpMode` | bool | true | Auto-probe new STRM items |
| `ProbeParallelism` | int | 5 | Max concurrent probes |
| `ProbeTimeoutSeconds` | int | 60 | Per-item timeout |
| `ProbeCooldownMs` | int | 200 | Delay between probes |
| `SelectedLibraryIds` | Guid[] | empty | Libraries to probe (empty = all) |

### Configuration UI

The config page is embedded as a resource and registered via `IHasWebPages`. It uses Jellyfin's standard plugin configuration API:

- `GET /Plugins/{PluginId}/Configuration` — load settings
- `POST /Plugins/{PluginId}/Configuration` — save settings
- Library list from `GET /Library/VirtualFolders`

## Error Handling Strategy

| Error | Handling | Recovery |
|-------|----------|----------|
| ffprobe timeout | `CancellationTokenSource.CancelAfter()` | Log warning, skip item, retry next run |
| ffprobe failure (bad URL, 404) | Catch `Exception` in `ProbeItemAsync` | Log warning, skip item, retry next run |
| Task cancellation | `OperationCanceledException` propagates | Task stops cleanly, resume on next run |
| Plugin not initialized | Check `Plugin.Instance` | Return early from task/catch-up |
| No items to probe | Early exit with 100% progress | No-op |

## Concurrency Model

```
Parallel.ForEachAsync(MaxDegreeOfParallelism = ProbeParallelism)
    │
    ├─ Worker 1: probe item A ──── ffprobe ──── cooldown ──── next item
    ├─ Worker 2: probe item B ──── ffprobe ──── cooldown ──── next item
    ├─ Worker 3: probe item C ──── ffprobe ──── cooldown ──── next item
    └─ Items are lazily consumed from the source list

Each worker:
1. Receives next item from source
2. Runs ffprobe (via RefreshSingleItem) with per-item timeout
3. Increments success/failure counter (Interlocked)
4. Reports progress
5. Waits cooldown
6. Receives next item (or completes if none remain)

A shared DirectoryService instance is reused across all workers
to avoid per-item allocation overhead.
```

## Target Platform

| Component | Version |
|-----------|---------|
| .NET | 9.0 |
| Jellyfin.Controller | 10.11.0 |
| Jellyfin.Model | 10.11.0 |
| StyleCop.Analyzers | 1.2.0-beta.556 |
