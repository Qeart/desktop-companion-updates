using AionMeter.Core.Events;
using AionMeter.Core.Pets;
namespace Aurum.Tests;
public class PetCacheAndShopTests
{
    private static PetProgressSnapshot Saved(string profile = "2314:Hero") => new(profile, 100, true, 1, new() { [1041] = new(1, 10) }, DateTimeOffset.UtcNow);
    [Fact]
    public void Restart_restores_collection_and_applies_next_pickup_without_relog()
    {
        var progress = new PetProgress([]); Assert.True(progress.Restore(Saved()));
        Assert.True(progress.FromCache); Assert.Equal(new PetState(1, 10), progress.State(1041));
        progress.Process(new PetSoulsEvent(1000, [new(1041, 1)]));
        Assert.Equal(new PetState(1, 11), progress.State(1041));
        Assert.True(Assert.Single(progress.TakeNotices(1300)).Synced);
    }
    [Fact]
    public void Different_character_cannot_reuse_saved_pet_counts()
    {
        var progress = new PetProgress([]); Assert.False(progress.Restore(Saved(), "2314:Other"));
        Assert.True(progress.Restore(Saved()));
        progress.Process(new SelfIdentifiedEvent(100, 200, "Other", 2314, GameClass.Chanter));
        Assert.Null(progress.State(1041)); Assert.False(progress.Synced);
    }
    [Fact]
    public void Full_collection_replaces_cached_values_instead_of_adding_them()
    {
        var progress = new PetProgress([]); progress.Restore(Saved());
        progress.Process(new PetCollectionEvent(100, [new(1041, 2)], [new(1041, 22)]));
        Assert.Equal(new PetState(2, 22), progress.State(1041)); Assert.False(progress.FromCache);
    }
    [Fact]
    public void Invalid_or_incomplete_cache_is_rejected()
    {
        var progress = new PetProgress([]);
        Assert.False(progress.Restore(Saved() with { Synced = false }));
        Assert.False(progress.Restore(Saved() with { Profile = "unknown" }));
        Assert.False(progress.Restore(Saved() with { Pets = new() { [1041] = new(-1, 4) } }));
        Assert.False(progress.Restore(Saved() with { Pets = new() { [1041] = new(1, -5) } }));
    }
    [Fact]
    public void Store_roundtrip_and_unsynced_exit_preserves_last_complete_collection()
    {
        var path = Path.Combine(Path.GetTempPath(), "aurum-pets-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new PetStateStore(path); store.Save(Saved());
            store.Save(Saved() with { Synced = false, Pets = new() });
            var restored = store.Load(); Assert.NotNull(restored); Assert.Equal(10, restored.Pets[1041].Points); Assert.True(restored.FromCache);
            File.WriteAllText(path, "{truncated"); Assert.Null(store.Load());
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }
    [Theory]
    [InlineData("Pet: Cave Bat", 1041)]
    [InlineData("pet: cave Bał", 1041)]
    [InlineData("Pet: Baby Odyle...", 1008)]
    [InlineData("Pet: Cave Bat Owned", 1041)]
    [InlineData("Pet: Aberrant...", 0)]
    [InlineData("Cave Bat", 1041)]
    [InlineData("Cave", 0)]
    [InlineData("Vet: Baby Odyle...", 1008)]
    [InlineData("Pet Baby Odyle...", 1008)]
    [InlineData("Pet: Baby Odyle..", 1008)]
    [InlineData("Pet: Baby Odyle.", 1008)]
    [InlineData("Baby Odyle...", 1008)]
    [InlineData("Baby Odyle… Owned", 1008)]
    [InlineData("Aberrant...", 0)]
    [InlineData("Baby Odyle", 0)]
    [InlineData("Baby…", 0)]
    public void Shop_matches_full_or_unique_truncated_pet_titles(string title, int expected)
    {
        PetDefinition[] pets = [new(1041, "Cave Bat", []), new(1008, "Baby Odyle Spider", []), new(1021, "Aberrant Odyle Spider", []), new(1047, "Aberrant Armadon", [])];
        Assert.Equal(expected, ShopPetMatcher.Match(title, pets)?.Id ?? 0);
    }
    [Fact]
    public void Clipped_shop_title_is_not_mistaken_for_a_shorter_full_pet_name()
    {
        PetDefinition[] pets = [new(1147, "Dratona", []), new(1185, "Dratona Tinkerer", [])];
        Assert.Equal(1147, ShopPetMatcher.Match("Pet: Dratona", pets)!.Id);
        Assert.Equal(1147, ShopPetMatcher.Match("Dratona", pets)!.Id);
        Assert.Null(ShopPetMatcher.Match("Pet: Dratona...", pets));
        Assert.Null(ShopPetMatcher.Match("Dratona…", pets));
        Assert.Equal(1185, ShopPetMatcher.Match("Dratona Tink...", pets)!.Id);
    }
    [Fact]
    public void Exact_short_names_still_match_without_the_pet_prefix()
        => Assert.Equal(1, ShopPetMatcher.Match("Krom", [new(1, "Krom", [])])!.Id);
    [Theory]
    [InlineData(1, 10, "10/25")]
    [InlineData(2, 22, "22/75")]
    [InlineData(3, 0, "FULL")]
    public void Shop_counter_uses_current_level_cost(int level, int points, string expected)
        => Assert.Equal(expected, ShopPetMatcher.Counter(new(level, points)));
    [Fact]
    public void Unknown_pet_count_is_not_reported_as_zero() => Assert.Equal("?", ShopPetMatcher.Counter(null));
    [Fact]
    public void Shop_tab_marker_rejects_truncated_or_unrelated_records()
    {
        var body = new byte[45]; body[0] = 0x68; body[1] = 1;
        Assert.Equal(0x68, MarketTabReader.Read(body, 100)!.Tab);
        Assert.Null(MarketTabReader.Read(body.AsSpan(0, 44), 100));
        body[0] = 0; Assert.Null(MarketTabReader.Read(body, 100));
    }
}
