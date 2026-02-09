using System;
using FluentAssertions;
using Xunit;

namespace JellySTRMprobe.Tests;

public class PluginConfigurationTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new PluginConfiguration();

        config.EnableCatchUpMode.Should().BeTrue();
        config.ProbeParallelism.Should().Be(5);
        config.ProbeTimeoutSeconds.Should().Be(60);
        config.ProbeCooldownMs.Should().Be(200);
        config.SelectedLibraryIds.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ClampsParallelism_WhenTooHigh()
    {
        var config = new PluginConfiguration { ProbeParallelism = 50 };

        config.Validate();

        config.ProbeParallelism.Should().Be(20);
    }

    [Fact]
    public void Validate_ClampsTimeout_WhenTooLow()
    {
        var config = new PluginConfiguration { ProbeTimeoutSeconds = 0 };

        config.Validate();

        config.ProbeTimeoutSeconds.Should().Be(10);
    }

    [Fact]
    public void Validate_ClampsCooldown_WhenNegative()
    {
        var config = new PluginConfiguration { ProbeCooldownMs = -1 };

        config.Validate();

        config.ProbeCooldownMs.Should().Be(0);
    }

    [Fact]
    public void Validate_DoesNotModify_ValidValues()
    {
        var config = new PluginConfiguration
        {
            ProbeParallelism = 10,
            ProbeTimeoutSeconds = 120,
            ProbeCooldownMs = 500,
        };

        config.Validate();

        config.ProbeParallelism.Should().Be(10);
        config.ProbeTimeoutSeconds.Should().Be(120);
        config.ProbeCooldownMs.Should().Be(500);
    }
}
