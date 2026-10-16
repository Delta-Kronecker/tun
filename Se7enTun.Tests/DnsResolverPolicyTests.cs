using System;
using System.Linq;
using Se7enPro.Models;
using Se7enPro.Services;
using Xunit;

namespace Se7enTun.Tests;

public sealed class DnsResolverPolicyTests
{
    [Fact]
    public void Default_settings_get_builtin_resolvers()
    {
        var plan = new DnsResolverPolicy().Build(new UserSettings(), hasV6Address: false);

        Assert.False(plan.HasUserEntries);
        Assert.False(plan.Strict);
        Assert.Empty(plan.Rejected);
        Assert.Contains("1.1.1.1", plan.UdpLiterals);
        Assert.Contains("8.8.8.8", plan.UdpLiterals);

        var relayTargets = DnsResolverPolicy.RelayTargets(plan);
        Assert.NotEmpty(relayTargets);
        Assert.All(relayTargets, t => Assert.Equal(DnsTransport.Udp, t.Transport));
        Assert.All(relayTargets, t => Assert.NotNull(t.Address));
    }

    [Fact]
    public void Custom_udp_resolver_is_used_and_defaults_stay_as_fallback()
    {
        var settings = new UserSettings { CustomDnsUdp = "9.9.9.9" };

        var plan = new DnsResolverPolicy().Build(settings, hasV6Address: false);

        Assert.True(plan.HasUserEntries);
        Assert.Empty(plan.Rejected);
        Assert.Equal("9.9.9.9", plan.UdpLiterals[0]);
        Assert.Contains("1.1.1.1", plan.UdpLiterals);

        var relayTargets = DnsResolverPolicy.RelayTargets(plan);
        Assert.Equal("9.9.9.9", relayTargets[0].Host);
    }

    [Fact]
    public void Strict_mode_suppresses_the_builtin_fallbacks()
    {
        var settings = new UserSettings { CustomDnsUdp = "9.9.9.9", CustomDnsStrict = true };

        var plan = new DnsResolverPolicy().Build(settings, hasV6Address: false);

        Assert.True(plan.Strict);
        Assert.True(plan.HasUserEntries);
        Assert.Single(plan.Resolvers, r => r.Transport == DnsTransport.Udp);
        Assert.Equal("9.9.9.9", DnsResolverPolicy.RelayTargets(plan).Single().Host);
    }

    [Fact]
    public void Unusable_entries_are_reported_instead_of_being_silently_dropped()
    {
        var settings = new UserSettings { CustomDnsUdp = "https://not-a-udp-resolver" };

        var plan = new DnsResolverPolicy().Build(settings, hasV6Address: false);

        Assert.Single(plan.Rejected);
        Assert.False(plan.HasUserEntries);
    }

    [Fact]
    public void V6_literals_are_only_offered_when_the_adapter_has_a_v6_address()
    {
        var withoutV6 = new DnsResolverPolicy().Build(new UserSettings(), hasV6Address: false);
        var withV6 = new DnsResolverPolicy().Build(new UserSettings(), hasV6Address: true);

        Assert.DoesNotContain("2606:4700:4700::1111", withoutV6.UdpLiterals);
        Assert.Contains("2606:4700:4700::1111", withV6.UdpLiterals);
    }
}

public sealed class DnsSettingsTests
{
    [Fact]
    public void ValidateEntry_accepts_plain_and_prefixed_resolvers()
    {
        Assert.Null(DnsSettings.ValidateEntry(DnsTransport.Udp, "9.9.9.9"));
        Assert.Null(DnsSettings.ValidateEntry(DnsTransport.Udp, "9.9.9.9:5353"));
        Assert.Null(DnsSettings.ValidateEntry(DnsTransport.Dot, "tls://dns.google"));
        Assert.Null(DnsSettings.ValidateEntry(DnsTransport.Doh, "https://cloudflare-dns.com/dns-query"));
    }

    [Fact]
    public void ValidateEntry_rejects_mismatched_or_empty_entries()
    {
        Assert.NotNull(DnsSettings.ValidateEntry(DnsTransport.Udp, ""));
        Assert.NotNull(DnsSettings.ValidateEntry(DnsTransport.Udp, "https://9.9.9.9"));
        Assert.NotNull(DnsSettings.ValidateEntry(DnsTransport.Dot, "dns.google"));
        Assert.NotNull(DnsSettings.ValidateEntry(DnsTransport.Doh, "http://9.9.9.9"));
    }

    [Fact]
    public void ParseEntry_reads_host_and_port()
    {
        var entry = DnsSettings.ParseEntry(DnsTransport.Udp, "9.9.9.9:5353", out var problem);

        Assert.Null(problem);
        Assert.NotNull(entry);
        Assert.Equal(5353, entry!.Port);
        Assert.Equal("9.9.9.9", entry.Host);
        Assert.Equal(DnsTransport.Udp, entry.Transport);
        Assert.Equal("9.9.9.9:5353", entry.TargetHostPort);
    }

    [Fact]
    public void ParseList_keeps_good_entries_and_reports_bad_ones()
    {
        var entries = DnsSettings.ParseList(DnsTransport.Udp, "9.9.9.9, https://bad, 1.1.1.1", out var problems);

        Assert.Equal(2, entries.Count);
        Assert.Single(problems);
        Assert.Contains(problems, p => p.StartsWith("https://bad", StringComparison.Ordinal));
    }
}
