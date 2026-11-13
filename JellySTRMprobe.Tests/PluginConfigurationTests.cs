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

        config.EnableFastStart.Should().BeTrue();
        config.EnableCatchUpMode.Should().BeFalse();
        config.ProbeParallelism.Should().Be(1);
        config.ProbeTimeoutSeconds.Should().Be(60);
        config.ProbeCooldownMs.Should().Be(2000);
        config.MaxItemsPerRun.Should().Be(500);
        config.ProbeCooldownJitterMs.Should().Be(0);
        config.ShuffleOrder.Should().BeTrue();
        config.StartDelayMaxMinutes.Should().Be(0);
        config.MaxConsecutiveFailures.Should().Be(10);
        config.PauseWhileStrmPlaying.Should().BeTrue();
        config.DeleteFailedStrms.Should().BeFalse();
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

    [Fact]
    public void Validate_ClampsNewLimits()
    {
        var config = new PluginConfiguration
        {
            MaxItemsPerRun = 0,
            MaxConsecutiveFailures = -5,
            ProbeCooldownMs = 100000,
        };

        config.Validate();

        config.MaxItemsPerRun.Should().Be(1);
        config.MaxConsecutiveFailures.Should().Be(1);
        config.ProbeCooldownMs.Should().Be(60000);
    }
}
