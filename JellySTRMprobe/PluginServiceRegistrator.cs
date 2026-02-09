using JellySTRMprobe.EntryPoint;
using JellySTRMprobe.Service;
using JellySTRMprobe.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

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
        serviceCollection.AddSingleton<IScheduledTask, ProbeStrmTask>();
        serviceCollection.AddHostedService<CatchUpEntryPoint>();
    }
}
