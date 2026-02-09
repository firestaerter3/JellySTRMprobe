# JellySTRMprobe — Test Cases

## Test Framework

- **Framework**: xUnit
- **Mocking**: Moq
- **Assertions**: xUnit built-in + FluentAssertions (optional)
- **Pattern**: AAA (Arrange, Act, Assert)

## Unit Tests

### 1. ProbeService Tests

#### 1.1 GetUnprobedItems

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-1.1.1 | Returns only STRM items with no media streams | 3 items: .strm/0 streams, .strm/2 streams, .mkv/0 streams | Returns only the first item |
| TC-1.1.2 | Filters by selected library IDs | 2 items in lib A, 1 item in lib B, selected = [A] | Returns only 2 items from lib A |
| TC-1.1.3 | Returns all libraries when selection is empty | Items in multiple libraries, selection = [] | Returns all unprobed STRM items |
| TC-1.1.4 | Returns empty list when no unprobed items | All items have media streams | Returns empty list |
| TC-1.1.5 | Handles null path gracefully | Item with null path in results | Skips item, no exception |

#### 1.2 ProbeItemAsync

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-1.2.1 | Successful probe returns true | Mock RefreshSingleItem succeeds | Returns true |
| TC-1.2.2 | Timeout returns false and logs warning | RefreshSingleItem hangs beyond timeout | Returns false, log contains "timed out" |
| TC-1.2.3 | Exception returns false and logs warning | RefreshSingleItem throws HttpRequestException | Returns false, log contains "failed" |
| TC-1.2.4 | Cancellation propagates when parent token cancelled | Parent CancellationToken is cancelled | Throws OperationCanceledException |
| TC-1.2.5 | Correct MetadataRefreshOptions passed | Capture options passed to RefreshSingleItem | EnableRemoteContentProbe=true, ReplaceAllMetadata=false, ReplaceAllImages=false |

#### 1.3 ProbeBatchAsync

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-1.3.1 | Probes all items and reports correct counts | 5 items, all succeed | (Probed=5, Failed=0, Skipped=0) |
| TC-1.3.2 | Mixed success/failure counts | 3 succeed, 2 fail | (Probed=3, Failed=2, Skipped=0) |
| TC-1.3.3 | Respects parallelism limit | 10 items, parallelism=2 | Max 2 concurrent probes at any time |
| TC-1.3.4 | Reports progress incrementally | 4 items | Progress reported at ~25%, ~50%, ~75%, ~100% |
| TC-1.3.5 | Applies cooldown between probes | 3 items, cooldown=100ms | Total time >= 300ms (3 * 100ms cooldown) |
| TC-1.3.6 | Cancellation stops batch processing | Cancel after 2nd item | Returns partial results, no more probes |
| TC-1.3.7 | Empty list returns zero counts | 0 items | (Probed=0, Failed=0, Skipped=0) |

### 2. ProbeStrmTask Tests

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-2.1 | Task has correct metadata | — | Name="Probe STRM Media Info", Category="STRM Probe" |
| TC-2.2 | Default trigger is daily at 4 AM | — | DailyTrigger with TimeOfDayTicks = 4 hours |
| TC-2.3 | Execute probes unprobed items | 3 unprobed items | ProbeBatchAsync called with 3 items |
| TC-2.4 | Execute skips when no items found | 0 unprobed items | Progress reports 100%, no ProbeBatchAsync call |
| TC-2.5 | Execute passes config values | Config: parallelism=10, timeout=120, cooldown=500 | ProbeBatchAsync called with matching params |

### 3. CatchUpEntryPoint Tests

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-3.1 | Subscribes to ItemAdded when enabled | Config: EnableCatchUpMode=true | ItemAdded event handler registered |
| TC-3.2 | Does not subscribe when disabled | Config: EnableCatchUpMode=false | ItemAdded event handler NOT registered |
| TC-3.3 | Queues STRM items on add | Add event with .strm path | Item added to pending queue |
| TC-3.4 | Ignores non-STRM items | Add event with .mkv path | Queue remains empty |
| TC-3.5 | Debounces processing (30 seconds) | Add 3 items within 1 second | ProcessQueue not called until 30s after last add |
| TC-3.6 | Processes all queued items in batch | Queue 5 items, timer fires | ProbeBatchAsync called with 5 items |
| TC-3.7 | Filters already-probed items before processing | Queue item that already has media streams | Item excluded from ProbeBatchAsync call |
| TC-3.8 | Dispose unsubscribes from events | Call Dispose() | ItemAdded event handler removed |

### 4. PluginConfiguration Tests

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-4.1 | Default values are correct | New PluginConfiguration() | CatchUp=true, Parallelism=5, Timeout=60, Cooldown=200, Libraries=[] |
| TC-4.2 | Validate clamps parallelism | Parallelism=50 | Clamped to 20 |
| TC-4.3 | Validate clamps timeout | Timeout=0 | Clamped to 10 |
| TC-4.4 | Validate clamps cooldown | Cooldown=-1 | Clamped to 0 |

### 5. Plugin Tests

| ID | Test Case | Setup | Expected |
|----|-----------|-------|----------|
| TC-5.1 | Plugin has correct name | — | Name == "JellySTRMprobe" |
| TC-5.2 | Plugin has valid GUID | — | Id is a valid, non-empty Guid |
| TC-5.3 | Plugin returns config pages | — | GetPages() returns config.html and config.js |

## Integration Test Scenarios (Manual)

These tests require a running Jellyfin instance with STRM files.

### IT-1: Scheduled Task Probing

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Install plugin, restart Jellyfin | Plugin visible in Dashboard > Plugins |
| 2 | Navigate to Dashboard > Scheduled Tasks | "Probe STRM Media Info" task visible under "STRM Probe" category |
| 3 | Click Run on the task | Task starts, progress bar advances |
| 4 | Check Jellyfin logs | "Found N unprobed STRM items" logged |
| 5 | Wait for completion | "Probe complete: X succeeded, Y failed" logged |
| 6 | Check a probed movie in Jellyfin UI | Duration, resolution, codec, audio channels visible |

### IT-2: Catch-up Mode

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Enable catch-up mode in plugin config | Setting saved |
| 2 | Restart Jellyfin | "STRM Probe catch-up mode enabled" in logs |
| 3 | Add new STRM files to library folder | — |
| 4 | Trigger library scan | New items discovered |
| 5 | Wait 30+ seconds | "Catch-up: probing N new STRM items" logged |
| 6 | Check new items in UI | Media info visible |

### IT-3: Configuration

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to plugin config page | Settings page loads with defaults |
| 2 | Change parallelism to 10 | Saved, reflected in next task run |
| 3 | Select specific library in library picker | Only that library's items are probed |
| 4 | Disable catch-up mode | Catch-up stops (new items not auto-probed) |

### IT-4: Error Resilience

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Add STRM with invalid URL | Probe fails for that item, others continue |
| 2 | Set timeout to 10 seconds, probe slow stream | Timeout logged, item skipped |
| 3 | Cancel task mid-run | Task stops cleanly, partial results preserved |
| 4 | Restart Jellyfin during catch-up | Catch-up re-subscribes on startup |

### IT-5: Dispatcharr Compatibility

| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set cooldown to 500ms | Probes spaced by 500ms minimum |
| 2 | Run task against Dispatcharr VOD proxy URLs | Movies probed successfully |
| 3 | Check Dispatcharr logs | No connection exhaustion or 503 errors |

## Test Coverage Goals

| Component | Target Coverage |
|-----------|----------------|
| ProbeService | 90%+ (core logic) |
| ProbeStrmTask | 80%+ (task orchestration) |
| CatchUpEntryPoint | 70%+ (event handling, hard to test timers) |
| PluginConfiguration | 100% (simple model) |
| Plugin | 100% (simple entry point) |
