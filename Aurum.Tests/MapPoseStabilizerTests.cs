using AionMeter.Core.PetMaps;

namespace Aurum.Tests;

public class MapPoseStabilizerTests
{
    private static PetMapAlignment Pose(double x = 0, double y = 0, double scale = 2) => new(1110, scale, 0, x, y, 40, 50);
    [Fact]
    public void Pan_and_cursor_pivot_zoom_are_applied_immediately()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose());
        var before = state.Current!.Project(500, 300);
        var zoom = new MapMotion(1.2, 900 * (1 - 1.2) + 12, 400 * (1 - 1.2) - 8);
        state.Advance(zoom, .016);
        var after = state.Current!.Project(500, 300);
        Assert.Equal(900 + (before.X - 900) * 1.2 + 12, after.X, 8);
        Assert.Equal(400 + (before.Y - 400) * 1.2 - 8, after.Y, 8);
        Assert.Equal(2.4, state.Current.Scale, 8);
    }
    [Fact]
    public void Small_absolute_correction_cannot_delay_the_measured_motion()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose());
        state.Calibrate(Pose(4));
        Assert.Equal(0, state.Current!.Tx);
        state.Advance(new(1, 50, -10), .016);
        Assert.InRange(state.Current!.Tx, 50, 50.385);
        Assert.Equal(-10, state.Current.Ty);
        Assert.InRange(state.CorrectionPixels, 0, .385);
        for (var i = 0; i < 30; i++) state.Advance(MapMotion.Identity, .016);
        Assert.Equal(54, state.Current.Tx, 6);
    }
    [Fact]
    public void A_long_pause_does_not_turn_calibration_into_a_jump()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose()); state.Calibrate(Pose(8));
        state.Advance(MapMotion.Identity, 5);
        Assert.InRange(state.Current!.Tx, 0, 1.201);
    }
    [Fact]
    public void Large_inconsistent_calibrations_do_not_move_markers()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose());
        Assert.False(state.Calibrate(Pose(40)));
        Assert.False(state.Calibrate(Pose(-40)));
        state.Advance(MapMotion.Identity, .016);
        Assert.Equal(0, state.Current!.Tx); Assert.Equal("AwaitingConfirmation", state.CalibrationState);
    }
    [Fact]
    public void Confirmation_is_transported_with_map_movement()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose());
        Assert.False(state.Calibrate(Pose(30)));
        state.Advance(new(1, 100, 0), .016);
        Assert.True(state.Calibrate(Pose(130)));
        state.Advance(MapMotion.Identity, .016);
        Assert.InRange(state.Current!.Tx, 100, 100.385);
    }
    [Fact]
    public void Confirmed_discontinuity_hides_pose_instead_of_sliding_across_regions()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose());
        var other = Pose() with { MapId = 1010 };
        Assert.False(state.Calibrate(other)); Assert.NotNull(state.Current);
        Assert.False(state.Calibrate(other)); Assert.Null(state.Current);
        Assert.True(state.Calibrate(other)); Assert.Equal(1010, state.Current!.MapId);
    }
    [Fact]
    public void Weak_absolute_match_is_rejected_even_if_its_position_is_plausible()
    {
        var state = new MapPoseStabilizer(1920, 1080);
        Assert.False(state.Calibrate(Pose() with { Inliers = 20 })); Assert.Null(state.Current);
        Assert.False(state.Calibrate(Pose() with { Matches = 120 })); Assert.Null(state.Current);
    }
    [Fact]
    public void Reset_discards_old_zoom_and_pending_corrections()
    {
        var state = new MapPoseStabilizer(1920, 1080); state.Calibrate(Pose()); state.Calibrate(Pose(8)); state.Reset();
        state.Calibrate(Pose(300, -200, 5)); state.Advance(MapMotion.Identity, .016);
        Assert.Equal(Pose(300, -200, 5), state.Current);
        Assert.Equal(0, state.CorrectionPixels);
    }
    [Fact]
    public void Motion_history_transports_a_delayed_calibration_to_the_current_frame()
    {
        var motion = new MapMotion(1.1, -90, -40).Follow(new(1, 35, -7));
        var actual = motion.Apply(Pose(100, 200));
        var expected = Pose(100, 200).Follow(1.1, 0, -90, -40, 40, 50).Follow(1, 0, 35, -7, 40, 50);
        Assert.Equal(expected, actual);
    }
}
