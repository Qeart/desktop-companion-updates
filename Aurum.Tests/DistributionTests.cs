using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Aurum.Distribution;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Aurum.Tests;

public sealed class DistributionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "desktop-companion-test-" + Guid.NewGuid().ToString("N"));
    private string Dir(string name) { var path = Path.Combine(_root, name); Directory.CreateDirectory(path); return path; }

    [Fact]
    public void Installed_data_lives_outside_replaced_application_files()
    {
        var app = Path.Combine(_root, "DesktopCompanion", "current");
        var data = UserDataMigration.DataDirectory(app, _root, true);
        Assert.Equal(Path.Combine(_root, "DesktopCompanionData"), data);
        Assert.False(data.StartsWith(app + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Path.Combine(app, "user-data"), UserDataMigration.DataDirectory(app, _root, false));
    }
    [Fact]
    public void Import_preserves_existing_data_and_does_not_copy_captures_or_programs()
    {
        var source = Dir("old"); var target = Dir("data");
        File.WriteAllText(Path.Combine(source, "settings.json"), "{\"Width\":400}");
        File.WriteAllText(Path.Combine(target, "settings.json"), "{\"Width\":250}");
        File.WriteAllText(Path.Combine(source, "pets-cache.json"), "{\"Pets\":{\"1041\":{\"Level\":1,\"Points\":10}}}");
        File.WriteAllText(Path.Combine(source, "traffic.pcap"), "private capture");
        File.WriteAllText(Path.Combine(source, "unrelated.exe"), "program");
        var result = UserDataMigration.Import(source, target);
        Assert.Equal(1, result.Copied); Assert.Equal(1, result.Existing); Assert.Empty(result.Errors);
        Assert.Contains("250", File.ReadAllText(Path.Combine(target, "settings.json")));
        Assert.True(File.Exists(Path.Combine(target, "pets-cache.json")));
        Assert.False(File.Exists(Path.Combine(target, "traffic.pcap"))); Assert.False(File.Exists(Path.Combine(target, "unrelated.exe")));
    }
    [Fact]
    public void Invalid_cache_is_not_imported_and_can_be_retried_after_repair()
    {
        var source = Dir("old"); var target = Dir("data");
        File.WriteAllText(Path.Combine(source, "pets-cache.json"), "{broken");
        Assert.Single(UserDataMigration.Import(source, target).Errors);
        Assert.False(File.Exists(Path.Combine(target, "pets-cache.json")));
        File.WriteAllText(Path.Combine(source, "pets-cache.json"), "{}");
        Assert.Equal(1, UserDataMigration.Import(source, target).Copied);
    }
    [Fact]
    public void Interrupted_history_import_resumes_without_replacing_existing_fights()
    {
        var source = Dir("old"); var target = Dir("data");
        Directory.CreateDirectory(Path.Combine(source, "fights")); Directory.CreateDirectory(Path.Combine(target, "fights"));
        File.WriteAllText(Path.Combine(source, "fights", "one.json"), "{\"damage\":10}");
        File.WriteAllText(Path.Combine(source, "fights", "two.json"), "{\"damage\":20}");
        File.WriteAllText(Path.Combine(source, "fights", "index.jsonl"), "{\"FileName\":\"one.json\"}\n{\"FileName\":\"two.json\"}\n");
        File.Copy(Path.Combine(source, "fights", "one.json"), Path.Combine(target, "fights", "one.json"));
        var result = UserDataMigration.Import(source, target);
        Assert.Equal(2, result.Copied); Assert.Equal(1, result.Existing); Assert.Empty(result.Errors);
        Assert.Equal(File.ReadAllText(Path.Combine(source, "fights", "index.jsonl")), File.ReadAllText(Path.Combine(target, "fights", "index.jsonl")));
        Assert.Equal(0, UserDataMigration.Import(source, target).Copied);
    }
    [Theory]
    [InlineData("stable", "win-stable")] [InlineData("beta", "win-beta")]
    [InlineData("arbitrary-url", "win-stable")] [InlineData(null, "win-stable")]
    public void Settings_can_only_select_supported_release_channels(string? channel, string expected)
        => Assert.Equal(expected, ReleaseChannels.Feed(channel));

    private (UpdateManager Manager, string Package, string Cache) Feed(string current = "0.2.0", bool corrupted = false)
    {
        var source = Dir("feed"); var cache = Dir("packages");
        const string name = "DesktopCompanion-0.2.1-full.nupkg";
        var package = Path.Combine(source, name);
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("DesktopCompanion.nuspec").Open());
            writer.Write("<package><metadata><id>DesktopCompanion</id><version>0.2.1</version><authors>Test</authors><description>Test</description></metadata></package>");
        }
        var bytes = File.ReadAllBytes(package);
        File.WriteAllText(Path.Combine(source, "releases.win-stable.json"), JsonSerializer.Serialize(new { Assets = new[] { new {
            PackageId = "DesktopCompanion", Version = "0.2.1", Type = "Full", FileName = name, Size = bytes.LongLength,
            SHA1 = Convert.ToHexString(SHA1.HashData(bytes)), SHA256 = Convert.ToHexString(SHA256.HashData(bytes)) } } }));
        if (corrupted) File.AppendAllText(package, "tampered");
        var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(source)),
            new UpdateOptions { ExplicitChannel = "win-stable", AllowVersionDowngrade = false }, new TestVelopackLocator("DesktopCompanion", current, cache));
        return (manager, package, cache);
    }
    [Fact]
    public async Task Update_feed_downloads_verified_newer_package_without_touching_user_data()
    {
        var data = Dir("userdata"); var settings = Path.Combine(data, "settings.json"); File.WriteAllText(settings, "{\"Width\":250}");
        var (manager, _, cache) = Feed(); var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update); Assert.Equal("0.2.1", update.TargetFullRelease.Version.ToString());
        await manager.DownloadUpdatesAsync(update);
        Assert.True(File.Exists(Path.Combine(cache, update.TargetFullRelease.FileName)));
        Assert.Equal("{\"Width\":250}", File.ReadAllText(settings)); Assert.Equal("0.2.0", manager.CurrentVersion!.ToString());
    }
    [Fact]
    public async Task Older_feed_does_not_downgrade_a_newer_installation()
    {
        var (manager, _, _) = Feed("0.3.0"); Assert.Null(await manager.CheckForUpdatesAsync());
    }
    [Fact]
    public async Task Corrupt_download_is_rejected_before_it_can_be_applied()
    {
        var (manager, _, _) = Feed(corrupted: true); var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update);
        await Assert.ThrowsAsync<Velopack.Exceptions.ChecksumFailedException>(() => manager.DownloadUpdatesAsync(update));
        Assert.Null(manager.UpdatePendingRestart); Assert.Equal("0.2.0", manager.CurrentVersion!.ToString());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
