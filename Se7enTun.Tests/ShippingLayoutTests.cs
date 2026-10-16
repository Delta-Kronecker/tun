using System;
using System.IO;
using Xunit;

namespace Se7enTun.Tests;

/// <summary>
/// Shipping contract, asserted in CI:
/// the engine refuses to start without <c>Resources\tun2socks\*</c> next to the host
/// executable (content copied from the library into the consumer's output), and the
/// localisation tables must stay in the repository because consumers copy them into
/// their own application.
/// </summary>
public sealed class ShippingLayoutTests
{
    [Fact]
    public void Data_plane_binaries_are_copied_next_to_the_consumer()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "tun2socks");

        Assert.True(Directory.Exists(dir), $"missing directory: {dir}");
        Assert.True(File.Exists(Path.Combine(dir, "tun2socks.exe")), "tun2socks.exe was not copied to the output");
        Assert.True(File.Exists(Path.Combine(dir, "wintun.dll")), "wintun.dll was not copied to the output");
    }

    [Fact]
    public void The_string_tables_are_shipped_in_the_repository()
    {
        var root = FindRepositoryRoot();

        foreach (var lang in new[] { "en", "ru", "zh" })
        {
            var path = Path.Combine(root, "Se7enTun", "Resources", $"Strings.{lang}.xaml");
            Assert.True(File.Exists(path), $"missing string table: {path}");
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Se7enTun.sln")))
        {
            dir = dir.Parent!;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
