using System;
using System.Linq;
using Se7enPro.Services;
using Xunit;

namespace Se7enTun.Tests;

public sealed class ConnectionMethodTests
{
    [Theory]
    [InlineData("psiphon", ConnectionMethod.Psiphon)]
    [InlineData("wireguard", ConnectionMethod.WireGuard)]
    [InlineData("wg", ConnectionMethod.WireGuard)]
    [InlineData("auto", ConnectionMethod.WireGuard)]
    [InlineData("shard", ConnectionMethod.Shard)]
    [InlineData("chain", ConnectionMethod.PsiphonOverWarp)]
    [InlineData("mim", ConnectionMethod.MasqueInMasque)]
    [InlineData("", ConnectionMethod.WireGuard)]
    [InlineData(null, ConnectionMethod.WireGuard)]
    public void ParseConnectionMethod_handles_aliases_and_falls_back(string? token, ConnectionMethod expected)
    {
        Assert.Equal(expected, ConnectionMethodExtensions.ParseConnectionMethod(token));
    }

    [Fact]
    public void ToToken_round_trips_every_member()
    {
        foreach (ConnectionMethod method in Enum.GetValues<ConnectionMethod>())
        {
            var token = method.ToToken();
            Assert.Equal(method, ConnectionMethodExtensions.ParseConnectionMethod(token));
        }
    }

    [Fact]
    public void Group_predicates_match_the_engine_split()
    {
        Assert.True(ConnectionMethod.WireGuard.IsAether());
        Assert.True(ConnectionMethod.Masque.IsAether());
        Assert.False(ConnectionMethod.Shard.IsAether());

        Assert.True(ConnectionMethod.Shard.IsShard());
        Assert.False(ConnectionMethod.WireGuard.IsShard());

        Assert.True(ConnectionMethod.PsiphonOverWarp.IsChained());
        Assert.True(ConnectionMethod.TorOverV2Ray.IsChained());
        Assert.False(ConnectionMethod.Psiphon.IsChained());

        Assert.True(ConnectionMethod.PsiphonOverV2Ray.IsV2Ray());
        Assert.False(ConnectionMethod.TorOverWarp.IsV2Ray());
    }

    [Fact]
    public void Option_catalogue_and_groups_are_non_empty()
    {
        var options = ConnectionMethodExtensions.CreateOptions();
        Assert.NotEmpty(options);
        Assert.All(options, o => Assert.False(string.IsNullOrWhiteSpace(o.Key)));
        Assert.All(options, o => Assert.False(string.IsNullOrWhiteSpace(o.English)));

        Assert.NotEmpty(ConnectionMethodExtensions.Groups);
        Assert.False(string.IsNullOrWhiteSpace(
            ConnectionMethodExtensions.GroupOf("SHARD (CF FRAGMENTATION)").Key));
    }

    [Fact]
    public void Engine_process_names_are_populated()
    {
        Assert.NotEmpty(EngineProcessNames.All);
        Assert.All(EngineProcessNames.All, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Contains(EngineProcessNames.All, n => n.Contains("Tunnel", StringComparison.Ordinal));
    }

    [Fact]
    public void Display_names_resolve_in_the_default_language()
    {
        var display = ConnectionMethod.WireGuard.ToDisplayName();
        Assert.False(string.IsNullOrWhiteSpace(display));
        Assert.Equal(display, ConnectionMethod.WireGuard.ToDisplayName());
        Assert.NotEqual(display, ConnectionMethod.Shard.ToDisplayName());
    }
}
