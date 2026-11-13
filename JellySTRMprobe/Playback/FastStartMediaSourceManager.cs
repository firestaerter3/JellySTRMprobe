using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using JellySTRMprobe.Cache;
using JellySTRMprobe.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe.Playback;

/// <summary>
/// Wraps Jellyfin's <see cref="IMediaSourceManager"/> so that playback of a STRM item whose
/// streams are already known skips the probe and full metadata refresh Jellyfin runs on every
/// STRM playback. Every other call goes to the wrapped instance unchanged.
/// </summary>
public sealed class FastStartMediaSourceManager : IMediaSourceManager, IDisposable
{
    private readonly IMediaSourceManager _inner;
    private readonly bool _ownsInner;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FastStartMediaSourceManager> _logger;
    private readonly Func<PluginConfiguration?> _getConfiguration;
    private ILibraryManager? _libraryManager;
    private IStreamCache? _streamCache;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FastStartMediaSourceManager"/> class.
    /// </summary>
    /// <param name="inner">The wrapped media source manager.</param>
    /// <param name="ownsInner">Whether this instance created <paramref name="inner"/> and must dispose it.</param>
    /// <param name="serviceProvider">Service provider, used to resolve the library manager lazily.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="getConfiguration">Returns the current plugin configuration, or <c>null</c> when it is not available.</param>
    public FastStartMediaSourceManager(
        IMediaSourceManager inner,
        bool ownsInner,
        IServiceProvider serviceProvider,
        ILogger<FastStartMediaSourceManager> logger,
        Func<PluginConfiguration?> getConfiguration)
    {
        _inner = inner;
        _ownsInner = ownsInner;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _getConfiguration = getConfiguration;
    }

    /// <summary>
    /// Decides whether a playback request may skip Jellyfin's probe and metadata refresh.
    /// </summary>
    /// <param name="item">The item about to be played.</param>
    /// <param name="allowMediaProbe">Whether the caller allows a probe.</param>
    /// <param name="config">The plugin configuration, or <c>null</c> when it is not available.</param>
    /// <param name="isInSelectedLibraries">Returns whether the item lies in a selected library.</param>
    /// <param name="needsVerification">Returns whether the item's streams came from the cache and still need one real probe.</param>
    /// <returns><c>true</c> when the probe can be skipped.</returns>
    public static bool ShouldSkipProbe(
        BaseItem item,
        bool allowMediaProbe,
        PluginConfiguration? config,
        Func<BaseItem, bool> isInSelectedLibraries,
        Func<BaseItem, bool>? needsVerification = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(isInSelectedLibraries);

        if (!allowMediaProbe || config == null || !config.EnableFastStart || !LibraryScope.IsStrm(item))
        {
            return false;
        }

        // Only an item whose video stream is already stored may skip the probe. Without it
        // Jellyfin has no codec information, cannot direct play, and the transcode it falls
        // back to fails (seen with never-probed films on 2026-11-12).
        if (!item.GetMediaStreams().Any(stream => stream.Type == MediaStreamType.Video))
        {
            return false;
        }

        if (!isInSelectedLibraries(item))
        {
            return false;
        }

        // Streams put back from the cache get one real probe first, in case the file behind the
        // same title has changed upstream.
        return needsVerification == null || !needsVerification(item);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MediaSourceInfo>> GetPlaybackMediaSources(BaseItem item, User user, bool allowMediaProbe, bool enablePathSubstitution, CancellationToken cancellationToken)
    {
        var skip = false;
        var verify = false;
        var config = _getConfiguration();
        try
        {
            skip = ShouldSkipProbe(item, allowMediaProbe, config, IsInSelectedLibraries);
            if (skip && NeedsVerification(item))
            {
                skip = false;
                verify = true;
            }
        }
        catch (Exception ex)
        {
            // Any doubt means Jellyfin's own behaviour: a slow start is better than no start.
            _logger.LogWarning(ex, "Fast start check failed for {ItemId}; using Jellyfin's normal probe", item?.Id);
        }

        if (verify && await TryVerifyAsync(item!, config!, cancellationToken).ConfigureAwait(false))
        {
            // The probe just ran for real; no need for Jellyfin's refresh on top of it.
            skip = true;
        }

        if (skip)
        {
            _logger.LogDebug("Fast start: skipping probe and refresh for {ItemName} ({ItemId})", item!.Name, item.Id);
            return await _inner.GetPlaybackMediaSources(item, user, false, enablePathSubstitution, cancellationToken).ConfigureAwait(false);
        }

        return await _inner.GetPlaybackMediaSources(item!, user, allowMediaProbe, enablePathSubstitution, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void AddParts(IEnumerable<IMediaSourceProvider> providers) => _inner.AddParts(providers);

    /// <inheritdoc />
    public IReadOnlyList<MediaStream> GetMediaStreams(Guid itemId) => _inner.GetMediaStreams(itemId);

    /// <inheritdoc />
    public IReadOnlyList<MediaStream> GetMediaStreams(MediaStreamQuery query) => _inner.GetMediaStreams(query);

    /// <inheritdoc />
    public IReadOnlyList<MediaAttachment> GetMediaAttachments(Guid itemId) => _inner.GetMediaAttachments(itemId);

    /// <inheritdoc />
    public IReadOnlyList<MediaAttachment> GetMediaAttachments(MediaAttachmentQuery query) => _inner.GetMediaAttachments(query);

    /// <inheritdoc />
    public IReadOnlyList<MediaSourceInfo> GetStaticMediaSources(BaseItem item, bool enablePathSubstitution, User? user = null)
        => _inner.GetStaticMediaSources(item, enablePathSubstitution, user!);

    /// <inheritdoc />
    public Task<MediaSourceInfo> GetMediaSource(BaseItem item, string mediaSourceId, string liveStreamId, bool enablePathSubstitution, CancellationToken cancellationToken)
        => _inner.GetMediaSource(item, mediaSourceId, liveStreamId, enablePathSubstitution, cancellationToken);

    /// <inheritdoc />
    public Task<LiveStreamResponse> OpenLiveStream(LiveStreamRequest request, CancellationToken cancellationToken)
        => _inner.OpenLiveStream(request, cancellationToken);

    /// <inheritdoc />
    public Task<Tuple<LiveStreamResponse, IDirectStreamProvider>> OpenLiveStreamInternal(LiveStreamRequest request, CancellationToken cancellationToken)
        => _inner.OpenLiveStreamInternal(request, cancellationToken);

    /// <inheritdoc />
    public Task<MediaSourceInfo> GetLiveStream(string id, CancellationToken cancellationToken)
        => _inner.GetLiveStream(id, cancellationToken);

    /// <inheritdoc />
    public Task<Tuple<MediaSourceInfo, IDirectStreamProvider>> GetLiveStreamWithDirectStreamProvider(string id, CancellationToken cancellationToken)
        => _inner.GetLiveStreamWithDirectStreamProvider(id, cancellationToken);

    /// <inheritdoc />
    public ILiveStream GetLiveStreamInfo(string id) => _inner.GetLiveStreamInfo(id);

    /// <inheritdoc />
    public ILiveStream GetLiveStreamInfoByUniqueId(string uniqueId) => _inner.GetLiveStreamInfoByUniqueId(uniqueId);

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaSourceInfo>> GetRecordingStreamMediaSources(ActiveRecordingInfo info, CancellationToken cancellationToken)
        => _inner.GetRecordingStreamMediaSources(info, cancellationToken);

    /// <inheritdoc />
    public Task CloseLiveStream(string id) => _inner.CloseLiveStream(id);

    /// <inheritdoc />
    public Task<MediaSourceInfo> GetLiveStreamMediaInfo(string id, CancellationToken cancellationToken)
        => _inner.GetLiveStreamMediaInfo(id, cancellationToken);

    /// <inheritdoc />
    public bool SupportsDirectStream(string path, MediaProtocol protocol) => _inner.SupportsDirectStream(path, protocol);

    /// <inheritdoc />
    public MediaProtocol GetPathProtocol(string path) => _inner.GetPathProtocol(path);

    /// <inheritdoc />
    public void SetDefaultAudioAndSubtitleStreamIndices(BaseItem item, MediaSourceInfo source, User user)
        => _inner.SetDefaultAudioAndSubtitleStreamIndices(item, source, user);

    /// <inheritdoc />
    public Task AddMediaInfoWithProbe(MediaSourceInfo mediaSource, bool isAudio, string cacheKey, bool addProbeDelay, bool isLiveStream, CancellationToken cancellationToken)
        => _inner.AddMediaInfoWithProbe(mediaSource, isAudio, cacheKey, addProbeDelay, isLiveStream, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsInner && _inner is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<bool> TryVerifyAsync(BaseItem item, PluginConfiguration config, CancellationToken cancellationToken)
    {
        try
        {
            var probeService = _serviceProvider.GetService<IProbeService>();
            if (probeService == null
                || !await probeService.ProbeDirectAsync(item, config.ProbeTimeoutSeconds, cancellationToken).ConfigureAwait(false)
                || !item.GetMediaStreams().Any(s => s.Type == MediaStreamType.Video))
            {
                // No proof the file was read; keep the flag and let Jellyfin probe as usual.
                return false;
            }

            StreamCache()?.MarkVerified(item);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Verification of restored streams failed for {ItemId}", item.Id);
            return false;
        }
    }

    private IStreamCache? StreamCache()
    {
        return _streamCache ??= _serviceProvider.GetService<IStreamCache>();
    }

    private bool NeedsVerification(BaseItem item)
    {
        return StreamCache()?.NeedsVerification(item) == true;
    }

    private bool IsInSelectedLibraries(BaseItem item)
    {
        var config = _getConfiguration();
        if (config == null || config.SelectedLibraryIds.Length == 0)
        {
            return true;
        }

        _libraryManager ??= _serviceProvider.GetRequiredService<ILibraryManager>();
        return LibraryScope.IsInSelectedLibraries(_libraryManager, item, config.SelectedLibraryIds);
    }
}
