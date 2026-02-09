using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace JellySTRMprobe.Tests;

public class PluginTests
{
    public PluginTests()
    {
        TestHelpers.EnsurePluginInstance();
    }

    [Fact]
    public void Plugin_HasCorrectName()
    {
        Plugin.Instance.Name.Should().Be("JellySTRMprobe");
    }

    [Fact]
    public void Plugin_HasValidGuid()
    {
        Plugin.Instance.Id.Should().NotBe(Guid.Empty);
        Plugin.Instance.Id.Should().Be(Guid.Parse("b8f5e3a1-d4c7-4f2e-9a6b-1c8d3e5f7a9b"));
    }

    [Fact]
    public void Plugin_ReturnsConfigPages()
    {
        var pages = Plugin.Instance.GetPages().ToList();

        pages.Should().HaveCount(2);
        pages.Should().Contain(p => p.Name == "config.html");
        pages.Should().Contain(p => p.Name == "config.js");
    }
}
