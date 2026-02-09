# JellySTRMprobe - Project Context

## Project Overview

A standalone Jellyfin plugin that extracts media information (codec, resolution, duration, audio) from STRM file targets by probing remote streams. Jellyfin never probes STRM files during library scans — this plugin fills that gap.

## Project Structure

```
JellySTRMprobe/
├── JellySTRMprobe/                          # Main plugin project
│   ├── JellySTRMprobe.csproj
│   ├── Plugin.cs                            # Plugin entry point
│   ├── PluginConfiguration.cs               # Settings model
│   ├── PluginServiceRegistrator.cs          # DI registration
│   ├── Service/
│   │   └── ProbeService.cs                  # Core probing logic
│   ├── Tasks/
│   │   └── ProbeStrmTask.cs                 # Scheduled task
│   ├── EntryPoint/
│   │   └── CatchUpEntryPoint.cs             # Auto-probe new items
│   └── Configuration/Web/
│       ├── config.html                      # Config UI
│       └── config.js                        # Config UI logic
├── JellySTRMprobe.Tests/                    # Unit tests
│   ├── JellySTRMprobe.Tests.csproj
│   └── Service/
│       └── ProbeServiceTests.cs
├── docs/                                    # Documentation
│   ├── REQUIREMENTS.md
│   ├── ARCHITECTURE.md
│   └── TESTCASES.md
├── jellyfin.ruleset                         # Code analysis rules
└── JellySTRMprobe.sln                       # Solution file
```

## Build & Test

```bash
# Build
dotnet build -c Release

# Run tests
dotnet test -c Release

# Publish for deployment
dotnet publish JellySTRMprobe -c Release -o /tmp/jellystrm-release
```

## Release Process

### 1. Update Version
Edit `JellySTRMprobe/JellySTRMprobe.csproj`:
```xml
<AssemblyVersion>X.Y.Z.0</AssemblyVersion>
<FileVersion>X.Y.Z.0</FileVersion>
```

### 2. Build Release
```bash
dotnet publish JellySTRMprobe -c Release -o /tmp/jellystrm-release
cd /tmp/jellystrm-release
zip -j /tmp/jellystrm-probe_X.Y.Z.0.zip JellySTRMprobe.dll
```

### 3. Deploy to Jellyfin
Copy `JellySTRMprobe.dll` to Jellyfin's plugin directory and restart.

## Core Mechanism

The plugin calls `IProviderManager.RefreshSingleItem()` with `EnableRemoteContentProbe = true` — the same flag Jellyfin sets during playback. This triggers ffprobe against the STRM target URL without requiring the user to play the content first.

## Target Framework
- .NET 9.0
- Jellyfin 10.11.0+

## Key Dependencies
- Jellyfin.Controller 10.11.0
- Jellyfin.Model 10.11.0

## Related Projects

| Project | Purpose |
|---------|---------|
| [Jellyfin-Xtream-Library](../Jellyfin-Xtream-Library/) | Sibling plugin — generates the STRM files this plugin probes |
| [StrmAssistant](https://github.com/sjtuross/StrmAssistant) | Emby equivalent (inspiration, not compatible) |
| [JellyfinStrmExtract](https://github.com/gauthier-th/JellyfinStrmExtract) | Basic Jellyfin predecessor (targets 10.9.x) |
