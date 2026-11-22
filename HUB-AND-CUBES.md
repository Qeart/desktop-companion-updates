# Floating Aurum menu and Hidden Cube compass

Click the round A icon to expand the menu; drag the icon to move it. Its location is saved
in user-data/settings.json. The expanded panel opens to the other side near the screen edge.
The menu and icon are visible over AION2/Aurum, and hide when another application is foreground.

Menu actions:
- DPS / HEAL: show or hide the existing meter. Capture continues when its window is hidden.
- Pet map: open/close the existing Aurum map.
- Hidden Cube: enable/disable the compass immediately and persist the choice.
- Soul pickups: enable/disable the portrait and progress notifications.
- Cloud Market: enable/disable the shop card counters.

Shortcuts: Ctrl+Shift+A menu, Ctrl+Shift+M meter, Ctrl+Shift+G map, Ctrl+Shift+H compass.
Existing Ctrl+Shift+P shop refresh and tray actions remain available.

## Compass data and validity

HiddenCubeTracker consumes world-object spawn/despawn and zone/identity events from the
existing Aurum capture pipeline. It uses PlayerPositionTracker's own-player coordinates.
No second Npcap session or separate CubeArrow application is started.

The camera sector detector reuses the tested local CubeArrow Navigation implementation.
It reads only the minimap, off the UI thread, and only when a known cube is within 100 m.
Legacy minimap calibration is imported from ../Aion2-Cube-Arrow/settings.json if available;
otherwise the existing 1920x1080 north-up calibration is scaled with client height.

The compass shows direction relative to the camera, horizontal distance, and vertical
difference when at least 6 m. It uses a shared rotating needle with a subtle halo pulse.
It hides when disabled, outside the game, during the Aurum map/shop, without a cube in
range, without a reliable camera sector, or when own-player position is over 3 seconds old.
Zone changes, teleport packets and character changes clear old cube locations. Despawn
removes the opened/disappeared cube. No locations are invented or restored from stale disk data.

After starting in an already loaded zone, objects already sent before startup may need to
enter the client's visibility again (approach another cube or change zone). Skills/effects
refresh own-player position; continuous movement without such packets is not inferred.

Diagnostics are under session-status.json / HiddenCube and Hub. Preview commands:
--render-hub <directory>, --check-cube-camera <screenshot> <report.json>.
Unit tests cover distance/bearing units, range, invalid positions, freshness, map mismatch,
despawn, teleport/zone invalidation, north wrap, and ambiguous camera sectors. Live verification
is still required for a real nearby cube, pickup/despawn, and camera turns in the current client.
