using AionMeter.Core.Events;
using AionMeter.Core.PetMaps;
using AionMeter.Core.Pets;

namespace Aurum.Tests;
public class PetTrackerTests
{
    private static readonly PetDefinition[] Pets = [new(1, "Alpha", [new(10, "Mob A", 1)]), new(2, "Beta", [new(20, "Mob B", .5)]),
        new(3, "Gamma", [new(30, "Mob C", 1)]), new(4, "Special", [])];
    private static PetTracker Tracker() => new(Pets, new([
        new(1110, "Altgard", 1000, 1000, [-50000, -50000, 50000, 50000], new() { [10] = [0, -100, 300, 0], [20] = [50, 0] }),
        new(1010, "Verteron", 1000, 1000, [-50000, -50000, 50000, 50000], new() { [30] = [10, 20] })]));
    private static PetProgressSnapshot Collection(Dictionary<int, PetState>? pets = null) => new("2305:Self", 100, true, 1, pets ?? []);
    [Fact]
    public void Auto_list_contains_all_local_unfinished_pets_and_counts_all_souls_to_max()
    {
        var view = Tracker().Build(Collection(new() { [1] = new(2, 40), [2] = new(1, 1) }), new(), 1110);
        Assert.Equal(new[] { 1, 2 }, view.Entries.Select(r => r.Pet.Id)); Assert.Equal(134, view.Remaining);
        Assert.Equal(35, view.Entries[0].Remaining); Assert.Equal("Altgard", view.Region);
    }
    [Fact]
    public void Real_shared_soul_update_removes_a_completed_pet_and_reduces_the_summary()
    {
        var progress = new PetProgress(Pets); progress.Restore(Collection(new() { [1] = new(2, 74), [2] = new(3, 0) }));
        var tracker = Tracker(); Assert.Single(tracker.Build(progress.Snapshot(), new(), 1110).Entries);
        progress.Process(new PetSoulsEvent(1000, [new(1, 1)]));
        var after = tracker.Build(progress.Snapshot(), new(), 1110);
        Assert.Empty(after.Entries); Assert.Equal(0, after.Remaining);
    }
    [Fact]
    public void Manual_selection_is_independent_from_auto_and_keeps_the_chosen_order()
    {
        var tracker = Tracker(); var selected = new[] { 3, 2, 3, 9999 };
        var view = tracker.Build(Collection(), new(false, false, selected), 1110);
        Assert.Equal(new[] { 3, 2 }, view.Entries.Select(r => r.Pet.Id)); Assert.Equal(1010, view.Entries[0].MapId);
        Assert.Empty(tracker.Build(Collection(), new(false, false, []), 1110).Entries);
        Assert.Equal(4, tracker.Build(Collection(), new(true, false, selected), 1110).Entries.Count);
    }
    [Fact]
    public void Manual_full_pets_are_hidden_without_erasing_the_saved_selection()
    {
        var selection = new[] { 1, 2 };
        var view = Tracker().Build(Collection(new() { [1] = new(3, 0) }), new(false, true, selection), 1110);
        Assert.Equal(2, Assert.Single(view.Entries).Pet.Id); Assert.Equal(new[] { 1, 2 }, selection);
    }
    [Fact]
    public void Unsynced_collection_is_unknown_and_never_fabricates_a_list_of_missing_pets()
    {
        var view = Tracker().Build(Collection() with { Synced = false }, new(), 1110);
        Assert.False(view.Synced); Assert.Empty(view.Entries); Assert.Null(view.Remaining);
    }
    [Fact]
    public void Unknown_region_and_global_collection_are_distinguished()
    {
        var tracker = Tracker(); Assert.Empty(tracker.Build(Collection(), new(), 777).Entries);
        var global = tracker.Build(Collection(), new(CurrentRegionOnly: false), 777);
        Assert.Equal(4, global.Entries.Count); Assert.False(global.HasSpawnData);
        Assert.All(global.Entries, r => Assert.Null(r.Distance));
    }
    [Fact]
    public void Navigation_uses_nearest_spawn_in_metres_and_never_a_stale_or_other_map_player()
    {
        var positions = new PlayerPositionTracker(); positions.SeedIdentity(100, 1110);
        positions.Observe(new(1110, 0, 0, 0, 10000, "OwnEffect")); var tracker = Tracker();
        var live = tracker.Build(Collection(), new(), 1110, positions.Tracking(10000));
        Assert.Equal(100, live.Entries[0].Distance!.Metres); Assert.Equal(0, live.Entries[0].Distance!.Bearing);
        Assert.All(tracker.Build(Collection(), new(), 1110, positions.Tracking(14000)).Entries, r => Assert.Null(r.Distance));
        Assert.Null(tracker.Build(Collection(), new(), 1010, positions.Tracking(10000)).Entries[0].Distance);
    }
    [Fact]
    public void Short_predicted_positions_are_explicitly_estimated_and_do_not_resort_rows()
    {
        var positions = new PlayerPositionTracker(); positions.SeedIdentity(100, 1110);
        positions.Observe(new(1110, 0, 0, 0, 9000, "OwnEffect")); positions.Observe(new(1110, 5, 0, 0, 10000, "OwnEffect"));
        var view = Tracker().Build(Collection(), new(), 1110, positions.Tracking(11500));
        Assert.All(view.Entries, r => Assert.True(r.Distance!.Estimated)); Assert.Equal(new[] { 1, 2 }, view.Entries.Select(r => r.Pet.Id));
    }
    [Fact]
    public void Character_change_uses_the_new_collection_instead_of_the_previous_players_progress()
    {
        var tracker = Tracker(); Assert.Single(tracker.Build(Collection(new() { [1] = new(3, 0) }), new(), 1110).Entries);
        var next = tracker.Build(Collection() with { Profile = "2305:Other", FromCache = true }, new(), 1110);
        Assert.Equal(2, next.Entries.Count); Assert.Equal(210, next.Remaining); Assert.True(next.FromCache);
    }
    [Fact]
    public void Invalid_and_out_of_bounds_spawn_coordinates_are_not_navigation_targets()
    {
        var tracker = new PetTracker(Pets, new([new(1110, "Altgard", 100, 100, [-50000, -50000, 50000, 50000],
            new() { [10] = [double.NaN, 0, 1e9, 0], [20] = [10, 20, 10] })]));
        Assert.False(tracker.HasSpawns(1110, 1)); Assert.True(tracker.HasSpawns(1110, 2));
    }
}
