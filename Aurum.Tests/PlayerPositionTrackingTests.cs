using System.Buffers.Binary;
using AionMeter.Core.Cubes;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using AionMeter.Core.PetMaps;

namespace Aurum.Tests;

public class PlayerPositionTrackingTests
{
    private static PlayerMapPosition Fix(long at, double x = 100, double y = 200) => new(1110, x, y, 10, at, "OwnEffect");
    private static PlayerPositionTracker Tracker(PositionTrackingOptions? options = null)
    { var tracker = new PlayerPositionTracker(options); tracker.SeedIdentity(42, 1110); return tracker; }
    private static PlayerPositionTracker Moving()
    { var tracker = Tracker(); Assert.True(tracker.Observe(Fix(9000))); Assert.True(tracker.Observe(Fix(10000, 106))); return tracker; }
    private static HiddenCubeTracker Cubes()
    { var cubes = new HiddenCubeTracker(); cubes.Process(new ZoneChangedEvent(0, 1110, "", false)); cubes.Process(new WorldObjectSeenEvent(10000, 50, 1301199, 13000, 20000, 1000)); return cubes; }

    [Fact]
    public void One_fix_never_invents_velocity_or_an_estimated_heading()
    {
        var tracker = Tracker(); tracker.Observe(Fix(10000));
        Assert.Equal(PositionState.Live, tracker.Tracking(11000).State);
        var stale = tracker.Tracking(11001);
        Assert.Equal(PositionState.Stale, stale.State); Assert.False(stale.HasVelocity);
        Assert.Equal(100, stale.Position!.XMetres); Assert.False(stale.Position.IsEstimated);
        Assert.Equal(Fix(10000), tracker.Snapshot()); Assert.Equal(10000, stale.LastReliableAt);
    }
    [Theory]
    [InlineData(10050, PositionState.Estimated, 106.3, 50)]
    [InlineData(10999, PositionState.Estimated, 111.994, 999)]
    [InlineData(11001, PositionState.Estimated, 112.006, 1001)]
    [InlineData(11500, PositionState.Estimated, 115, 1500)]
    [InlineData(12000, PositionState.Estimated, 118, 2000)]
    [InlineData(13000, PositionState.Estimated, 118, 2000)]
    [InlineData(13001, PositionState.Stale, 118, 2000)]
    [InlineData(60000, PositionState.Stale, 118, 2000)]
    public void Dead_reckoning_is_bounded_and_does_not_refresh_the_confirmed_timestamp(long now, PositionState state, double x, int horizon)
    {
        var tracker = Moving(); var result = tracker.Tracking(now);
        Assert.Equal(state, result.State); Assert.Equal(x, result.Position!.XMetres, 5);
        Assert.Equal(horizon, result.PredictionMilliseconds); Assert.True(result.Position.IsEstimated);
        Assert.Equal(10000, result.Position.At); Assert.Equal(10000, result.LastReliableAt);
        Assert.Equal(106, tracker.Snapshot()!.XMetres); Assert.Equal(6, result.VelocityX);
        Assert.Equal(0, result.VelocityY); Assert.Equal(state == PositionState.Stale ? 0 : 1 - (now - 10000) / 3000d, result.Position.Confidence, 6);
    }
    [Fact]
    public void Prediction_confidence_decays_and_a_new_fix_replaces_the_estimate()
    {
        var tracker = Moving(); Assert.True(tracker.Tracking(11200).Position!.Confidence > tracker.Tracking(11800).Position!.Confidence);
        tracker.Observe(Fix(12000, 109, 204)); var fix = tracker.Tracking(12000);
        Assert.Equal(PositionState.Live, fix.State); Assert.False(fix.Position!.IsEstimated);
        Assert.Equal(109, fix.Position.XMetres); Assert.Equal(12000, fix.LastReliableAt);
    }
    [Theory]
    [InlineData(50)] [InlineData(3000)] [InlineData(29000)]
    public void Too_close_or_too_sparse_samples_do_not_create_velocity(int gap)
    {
        var tracker = Tracker(); tracker.Observe(Fix(10000)); tracker.Observe(Fix(10000 + gap, 100.1));
        Assert.False(tracker.Tracking(11001 + gap).HasVelocity);
        Assert.Equal(PositionState.Stale, tracker.Tracking(11001 + gap).State);
    }
    [Fact]
    public void Stops_update_velocity_to_zero_without_drifting()
    {
        var tracker = Moving(); tracker.Observe(Fix(11000, 106));
        Assert.Equal(0, tracker.Tracking(12500).VelocityX); Assert.Equal(106, tracker.Tracking(12500).Position!.XMetres);
    }
    [Fact]
    public void Teleport_is_immediate_and_resets_velocity()
    {
        var tracker = Moving(); Assert.True(tracker.Observe(Fix(10100, 1000), discontinuity: true));
        Assert.Equal(1000, tracker.Snapshot()!.XMetres); Assert.False(tracker.Tracking(11000).HasVelocity);
        Assert.False(tracker.Observe(Fix(10099, 106)));
    }
    [Fact]
    public void Rejected_fixes_cannot_overwrite_the_confirmed_position()
    {
        var tracker = Moving(); var confirmed = tracker.Snapshot();
        foreach (var bad in new[] { Fix(9999, 101), Fix(10000, 999), Fix(11000) with { MapId = 1010 },
            Fix(11000) with { IsEstimated = true }, Fix(11000, double.NaN), Fix(11000) with { Confidence = 0 },
            Fix(11000) with { Confidence = double.NaN }, Fix(11000, 10000) })
        { Assert.False(tracker.Observe(bad)); Assert.Equal(confirmed, tracker.Snapshot()); }
    }
    [Fact]
    public void Future_fix_is_not_usable_for_navigation()
    {
        var tracker = Tracker(); tracker.Observe(Fix(10000));
        Assert.Null(tracker.Tracking(9000).Position); Assert.Null(Cubes().Navigate(tracker.Tracking(9000)));
    }
    [Fact]
    public void Identity_or_map_change_invalidates_velocity_and_old_updates()
    {
        var tracker = Moving(); tracker.Process(new ZoneChangedEvent(11000, 1010, "", false));
        Assert.Null(tracker.Snapshot()); Assert.False(tracker.Tracking(12000).HasVelocity);
        Assert.False(tracker.Observe(Fix(12000)));
        Assert.False(tracker.Observe(new(1010, 1, 2, 3, 10999, "OwnEffect")));
        tracker.Observe(new(1010, 1, 2, 3, 12000, "OwnEffect"));
        tracker.Process(new SelfIdentifiedEvent(13000, 77, "Other", 1, GameClass.Chanter)); Assert.Null(tracker.Snapshot());
    }
    [Fact]
    public void Map_position_survives_the_real_identity_loss_and_zone_event_order()
    {
        var tracker = Moving(); var resolver = new LocalPlayerResolver(); resolver.EventDecoded += tracker.Process;
        var body = new byte[28]; BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 1010);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(16), 12345); BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(20), -9876);
        tracker.Raw(12000, 0x2136, body); resolver.Process(new ZoneChangedEvent(12000, 1010, "", false));
        Assert.Equal("MapLoad", tracker.Snapshot()!.Source); Assert.Equal(123.45, tracker.Snapshot()!.XMetres, 5);
        Assert.False(tracker.Tracking(12000).HasVelocity);
        tracker.Raw(11000, 0x2136, body); Assert.Equal(12000, tracker.Snapshot()!.At);
        tracker.Raw(13000, 0x2136, new byte[3]); Assert.Equal(12000, tracker.Snapshot()!.At);
    }
    [Fact]
    public void A_later_identity_loss_does_not_preserve_a_map_load_from_an_old_session()
    {
        var tracker = Tracker(); tracker.Observe(Fix(10000) with { Source = "MapLoad" });
        tracker.Process(new LocalIdentityLostEvent(10001)); Assert.Null(tracker.Snapshot());
    }
    [Fact]
    public void Stale_navigation_keeps_cube_but_never_a_current_numeric_distance()
    {
        var tracker = Moving(); var cubes = Cubes();
        var live = cubes.Navigate(tracker.Tracking(10000))!;
        Assert.Equal(24d, live.DistanceMetres); Assert.Equal(PositionState.Live, live.State);
        var estimated = cubes.Navigate(tracker.Tracking(11500))!;
        Assert.Equal(15d, estimated.DistanceMetres); Assert.Equal(PositionState.Estimated, estimated.State);
        var stale = cubes.Navigate(tracker.Tracking(20000))!;
        Assert.Null(stale.DistanceMetres); Assert.Equal(estimated.Target, stale.Target); Assert.Equal(0, stale.Confidence);
        Assert.Equal(1, cubes.Count);
        cubes.Process(new WorldObjectGoneEvent(20001, 50)); Assert.Null(cubes.Navigate(tracker.Tracking(20002)));
    }
    [Fact]
    public void Stale_single_fix_can_show_a_detected_cube_as_history_on_first_render()
    {
        var tracker = Tracker(); tracker.Observe(Fix(10000)); var cubes = Cubes();
        var nav = cubes.Navigate(tracker.Tracking(20000)); Assert.NotNull(nav); Assert.Null(nav.DistanceMetres);
        cubes.Process(new ZoneChangedEvent(20001, 1010, "", false)); Assert.Null(cubes.Navigate(tracker.Tracking(20002)));
    }
    [Fact]
    public void Prediction_crossing_range_boundary_does_not_erase_a_detected_cube()
    {
        var tracker = Tracker(); tracker.Observe(Fix(9000, 106)); tracker.Observe(Fix(10000)); var cubes = Cubes();
        Assert.NotNull(cubes.Navigate(tracker.Tracking(10000), 31));
        Assert.NotNull(cubes.Navigate(tracker.Tracking(11500), 31));
        Assert.NotNull(cubes.Navigate(tracker.Tracking(20000), 31));
        tracker.Observe(Fix(20000, 80)); // real confirmed position outside the range
        Assert.Null(cubes.Navigate(tracker.Tracking(20000), 31));
    }
    [Fact]
    public void Camera_freshness_is_separate_and_failed_or_late_reads_do_not_rotate_the_arrow()
    {
        var camera = new CameraOrientationTracker(); Assert.False(camera.TryHeading(10000, 400, out _));
        Assert.True(camera.Observe(new(350, .8), 10000)); Assert.True(camera.TryHeading(10400, 400, out var heading)); Assert.Equal(350, heading);
        Assert.False(camera.TryHeading(10401, 400, out _)); Assert.False(camera.TryHeading(9999, 400, out _));
        Assert.False(camera.Observe(new(90, .8), 9999)); Assert.Equal(350, camera.Reading!.Value.Heading);
        camera.Observe(null, 10402); Assert.False(camera.TryHeading(10402, 400, out _));
        camera.Observe(new(20, .01), 10403); Assert.Null(camera.Reading);
        camera.Observe(new(20, .9), 10404); camera.Clear(); Assert.Null(camera.Reading);
    }
    [Fact]
    public void Timing_options_are_configurable_and_bounded()
    {
        var tracker = Tracker(new() { LiveMilliseconds = 200, StaleMilliseconds = 1500, MaxPredictionMilliseconds = 500 });
        tracker.Observe(Fix(9000)); tracker.Observe(Fix(10000, 106));
        Assert.Equal(PositionState.Live, tracker.Tracking(10000).State);
        Assert.Equal(PositionState.Estimated, tracker.Tracking(10200).State);
        Assert.Equal(PositionState.Estimated, tracker.Tracking(10201).State);
        Assert.Equal(109, tracker.Tracking(11000).Position!.XMetres);
        Assert.Equal(PositionState.Stale, tracker.Tracking(11501).State);
        var stationary = Tracker(new() { LiveMilliseconds = 200 }); stationary.Observe(Fix(10000));
        Assert.Equal(PositionState.Live, stationary.Tracking(10200).State);
        Assert.Equal(PositionState.Stale, stationary.Tracking(10201).State);
    }
    [Fact]
    public void Verified_live_buff_records_are_positions_but_a_cast_direction_vector_is_not()
    {
        // Minimal anonymous records from the 2026-11-23 capture: temporary entity id only.
        var tracker = new PlayerPositionTracker(); tracker.SeedIdentity(11766, 1110);
        var a = Convert.FromHexString("F65B01119310C80000002C010000000000004E65490AA2010000F65B01007761D94735F7A84700006745");
        var b = Convert.FromHexString("F65B01139510EDEBCC0AC80000000000000064D6490AA2010000F65B01CC7D1401004B4ED2471168AC4700507445");
        tracker.Raw(10000, 0x2A38, a);
        Assert.Equal(1112.989296875, tracker.Snapshot()!.XMetres, 6);
        tracker.Raw(39050, 0x2A38, b);
        Assert.Equal(1076.765859375, tracker.Snapshot()!.XMetres, 6);
        Assert.False(tracker.Tracking(40100).HasVelocity); // 29-second gap is not a velocity sample
        var confirmed = tracker.Snapshot();
        tracker.Raw(40000, 0x0238, Convert.FromHexString("F65B000F9515010102F65B000000000000A2C2C055694200000000"));
        tracker.Raw(40000, 0x0137, a); tracker.Raw(40000, 0x0037, a); tracker.Raw(40000, 0x0E38, a);
        Assert.Equal(confirmed, tracker.Snapshot()); // No guessed position decoder for unverified opcodes.
    }
}
