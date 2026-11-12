using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using Moq;

namespace JellySTRMprobe.Tests;

/// <summary>
/// Shared test helpers for creating mock objects.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Ensures Plugin.Instance is initialized for testing.
    /// </summary>
    public static void EnsurePluginInstance()
    {
        var mockPaths = new Mock<IApplicationPaths>();
        mockPaths.Setup(p => p.PluginConfigurationsPath).Returns("/tmp/jellyfin-test/plugin-configs");
        mockPaths.Setup(p => p.DataPath).Returns("/tmp/jellyfin-test/data");
        mockPaths.Setup(p => p.ConfigurationDirectoryPath).Returns("/tmp/jellyfin-test/config");
        mockPaths.Setup(p => p.PluginsPath).Returns("/tmp/jellyfin-test/plugins");
        mockPaths.Setup(p => p.ProgramDataPath).Returns("/tmp/jellyfin-test");
        mockPaths.Setup(p => p.LogDirectoryPath).Returns("/tmp/jellyfin-test/log");
        mockPaths.Setup(p => p.CachePath).Returns("/tmp/jellyfin-test/cache");

        var config = new PluginConfiguration();
        var mockSerializer = new Mock<IXmlSerializer>();
        mockSerializer
            .Setup(s => s.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>()))
            .Returns(config);

        _ = new Plugin(mockPaths.Object, mockSerializer.Object);
    }

    /// <summary>
    /// Puts every setting of the shared plugin configuration back to its default,
    /// then applies <paramref name="change"/>.
    /// </summary>
    public static void ResetConfiguration(Action<PluginConfiguration>? change = null)
    {
        EnsurePluginInstance();
        var config = Plugin.Instance.Configuration;
        var defaults = new PluginConfiguration();
        foreach (var property in typeof(PluginConfiguration).GetProperties())
        {
            if (property.CanRead && property.CanWrite)
            {
                property.SetValue(config, property.GetValue(defaults));
            }
        }

        change?.Invoke(config);
    }

    /// <summary>
    /// Creates a BaseItem mock whose stored media streams are <paramref name="streams"/>.
    /// </summary>
    public static BaseItem CreateTestItemWithStreams(string name, string? path, params MediaStream[] streams)
    {
        var mock = new Mock<BaseItem>() { CallBase = true };
        mock.Setup(m => m.GetMediaStreams()).Returns(new List<MediaStream>(streams));
        mock.Object.Name = name;
        mock.Object.Path = path;
        mock.Object.Id = Guid.NewGuid();
        return mock.Object;
    }

    /// <summary>
    /// Creates a BaseItem mock with properties set directly.
    /// </summary>
    public static BaseItem CreateTestItem(string name, string? path = null)
    {
        var mock = new Mock<BaseItem>() { CallBase = true };
        mock.Setup(m => m.GetMediaStreams()).Returns(new List<MediaStream>());
        mock.Object.Name = name;
        mock.Object.Path = path;
        mock.Object.Id = Guid.NewGuid();
        return mock.Object;
    }
}

/// <summary>
/// A progress reporter that calls back on the reporting thread, so tests need no delay.
/// </summary>
internal sealed class SynchronousProgress : IProgress<double>
{
    private readonly Action<double> _report;

    public SynchronousProgress(Action<double> report)
    {
        _report = report;
    }

    public void Report(double value) => _report(value);
}

/// <summary>
/// Tests that read or change the shared <see cref="Plugin.Instance"/> configuration run one at a time.
/// </summary>
[Xunit.CollectionDefinition("PluginInstance", DisableParallelization = true)]
public sealed class PluginInstanceCollection
{
}
