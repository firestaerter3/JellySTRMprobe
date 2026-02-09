using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellySTRMprobe;

/// <summary>
/// The JellySTRMprobe plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private static volatile Plugin? _instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        _instance = this;
    }

    /// <inheritdoc />
    public override string Name => "JellySTRMprobe";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b8f5e3a1-d4c7-4f2e-9a6b-1c8d3e5f7a9b");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin Instance => _instance ?? throw new InvalidOperationException("Plugin instance not available");

    private static PluginPageInfo CreateStatic(string name) => new()
    {
        Name = name,
        EmbeddedResourcePath = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.Web.{1}",
            typeof(Plugin).Namespace,
            name),
    };

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            CreateStatic("config.html"),
            CreateStatic("config.js"),
        };
    }
}
