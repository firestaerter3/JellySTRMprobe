using System;
using System.Linq;
using JellySTRMprobe.Cache;
using JellySTRMprobe.EntryPoint;
using JellySTRMprobe.Playback;
using JellySTRMprobe.Service;
using JellySTRMprobe.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JellySTRMprobe;

/// <summary>
/// Registers services for the JellySTRMprobe plugin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IProbeService, ProbeService>();
        serviceCollection.AddSingleton<IPlaybackGate, PlaybackGate>();
        serviceCollection.AddSingleton<StreamCacheService>();
        serviceCollection.AddSingleton<IStreamCache>(sp => sp.GetRequiredService<StreamCacheService>());
        serviceCollection.AddHostedService(sp => sp.GetRequiredService<StreamCacheService>());
        serviceCollection.AddSingleton<IScheduledTask, ProbeStrmTask>();
        serviceCollection.AddHostedService<CatchUpEntryPoint>();

        // Plugins register after the server's own services, so the IMediaSourceManager
        // registration is already in the collection here and can be wrapped.
        DecorateMediaSourceManager(serviceCollection);
    }

    /// <summary>
    /// Replaces the <see cref="IMediaSourceManager"/> registration with
    /// <see cref="FastStartMediaSourceManager"/> wrapping the original implementation.
    /// </summary>
    /// <param name="serviceCollection">The service collection.</param>
    internal static void DecorateMediaSourceManager(IServiceCollection serviceCollection)
    {
        var descriptor = serviceCollection.LastOrDefault(d => d.ServiceType == typeof(IMediaSourceManager));
        if (descriptor == null)
        {
            return;
        }

        serviceCollection.Remove(descriptor);
        serviceCollection.Add(new ServiceDescriptor(
            typeof(IMediaSourceManager),
            sp =>
            {
                var (inner, ownsInner) = CreateInner(sp, descriptor);
                return new FastStartMediaSourceManager(
                    inner,
                    ownsInner,
                    sp,
                    sp.GetRequiredService<ILogger<FastStartMediaSourceManager>>(),
                    GetConfiguration);
            },
            descriptor.Lifetime));
    }

    private static PluginConfiguration? GetConfiguration()
    {
        try
        {
            return Plugin.Instance.Configuration;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static (IMediaSourceManager Inner, bool OwnsInner) CreateInner(IServiceProvider serviceProvider, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IMediaSourceManager instance)
        {
            return (instance, false);
        }

        if (descriptor.ImplementationFactory != null)
        {
            return ((IMediaSourceManager)descriptor.ImplementationFactory(serviceProvider), false);
        }

        if (descriptor.ImplementationType != null)
        {
            return ((IMediaSourceManager)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType), true);
        }

        throw new InvalidOperationException("The original IMediaSourceManager registration cannot be resolved.");
    }
}
