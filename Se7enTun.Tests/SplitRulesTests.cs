using System.Collections.Generic;
using System.Net;
using Se7enPro.Models;
using Se7enPro.Services;
using Xunit;

namespace Se7enTun.Tests;

public sealed class SplitRulesTests
{
    [Theory]
    [InlineData("https://www.Example.com/path?q=1", "www.example.com")]
    [InlineData("http://sub.example.com/", "sub.example.com")]
    [InlineData("*.example.com", "example.com")]
    [InlineData(".example.com", "example.com")]
    [InlineData("EXAMPLE.COM", "example.com")]
    [InlineData("user@example.com", "example.com")]
    [InlineData("example.com:8443", "example.com")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("has space.com", null)]
    public void NormalizeSplitDomain_strips_scheme_path_and_port(string raw, string? expected)
    {
        Assert.Equal(expected, SplitRules.NormalizeSplitDomain(raw));
    }

    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8/32")]
    [InlineData("10.0.0.0/8", "10.0.0.0/8")]
    [InlineData(" 192.168.1.0/24 ", "192.168.1.0/24")]
    [InlineData("2001:db8::1", "2001:db8::1/128")]
    [InlineData("999.1.1.1", null)]
    [InlineData("10.0.0.0/33", null)]
    [InlineData("10.0.0.0/abc", null)]
    [InlineData("", null)]
    public void NormalizeSplitIpCidr_validates_address_and_prefix(string raw, string? expected)
    {
        Assert.Equal(expected, SplitRules.NormalizeSplitIpCidr(raw));
    }

    [Fact]
    public void ParseIpCidr_returns_address_and_prefix()
    {
        var parsed = SplitRules.ParseIpCidr("1.2.3.0/24");
        Assert.NotNull(parsed);
        Assert.Equal(IPAddress.Parse("1.2.3.0"), parsed.Value.Addr);
        Assert.Equal((byte)24, parsed.Value.Prefix);

        var bare = SplitRules.ParseIpCidr("1.2.3.4");
        Assert.NotNull(bare);
        Assert.Equal((byte)32, bare.Value.Prefix);

        Assert.Null(SplitRules.ParseIpCidr("1.2.3.4/33"));
        Assert.Null(SplitRules.ParseIpCidr("not-an-ip/24"));
    }

    [Fact]
    public void Process_names_and_paths_are_recognised()
    {
        Assert.Equal("chrome.exe", SplitRules.NormalizeProcessName("chrome"));
        Assert.Equal("chrome.exe", SplitRules.NormalizeProcessName("chrome.exe"));

        Assert.True(SplitRules.LooksLikeAppPath(@"C:\Program Files\App\app.exe"));
        Assert.True(SplitRules.LooksLikeAppPath("/usr/bin/tool"));
        Assert.False(SplitRules.LooksLikeAppPath("chrome.exe"));
    }

    [Fact]
    public void ClassifySplitEntries_buckets_each_kind_once()
    {
        var settings = new UserSettings();
        settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "ip", Value = "8.8.8.8/32" });
        settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "ip", Value = "8.8.8.8/32" });
        settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "domain", Value = "https://www.digikala.com/" });
        settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "app", Value = "chrome" });
        settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "app", Value = @"C:\Tools\foo.exe" });
        settings.SplitTunnelEntries.Add(new SplitTunnelEntry { Kind = "domain", Value = "   " });

        SplitRules.ClassifySplitEntries(settings, out var domains, out var ipCidrs, out var procNames, out var procPaths);

        Assert.Equal(new[] { "www.digikala.com" }, domains);
        Assert.Equal(new[] { "8.8.8.8/32" }, ipCidrs);
        Assert.Equal(new[] { "chrome.exe" }, procNames);
        Assert.Equal(new[] { @"C:\Tools\foo.exe" }, procPaths);
    }

    [Fact]
    public void ClassifySplitEntries_handles_a_null_settings_object()
    {
        SplitRules.ClassifySplitEntries(null, out var domains, out var ipCidrs, out var procNames, out var procPaths);

        Assert.Empty(domains);
        Assert.Empty(ipCidrs);
        Assert.Empty(procNames);
        Assert.Empty(procPaths);
    }
}
