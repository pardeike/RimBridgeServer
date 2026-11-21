# Screenshots for autonomous tests

Use `$rimbridge-server` discovery for the current contracts. These are bridge
screenshots of the game's rendered framebuffer, including dialogs, not desktop
captures. On macOS build1.7.4981 use `-NoWindow` for unattended rendered tests.
`-Headless` initializes no rendered framebuffer; the bridge now rejects capture
with a useful error instead of waiting for an image that cannot be produced.
A true headless renderer has not been added.

## Map-cell rectangle

Call `rimworld/screenshot_cell_rect`, for example:

```json
{"x":120,"z":120,"width":6,"height":4,"paddingCells":0,"fileName":"map-detail","outputDirectory":"/absolute/test/output"}
```

x/z are the minimum map-cell coordinates of the rectangle; width and height are
in cells and extend toward increasing x/z. This frames the rectangle, waits for
rendering, captures its screen projection and restores the camera by default.
Check success, fullyVisible and cameraWasReset. A large rectangle may require
an explicit larger rootSize. Overlaid game UI remains visible in a screenshot;
close unrelated dialogs when checking the map. This is not an offscreen map-only
render or a way to reveal occluded content.

## Whole dialog or UI target

Discover `rimworld/get_screen_targets` or `rimworld/get_ui_layout`. Use the actual
windowTargetId, ui-surface or ui-element id; do not invent an id.

```json
{"fileName":"dialog","clipTargetId":"<discovered target>","clipPadding":0,"outputDirectory":"/absolute/test/output"}
```

## Screen rectangle

`rimworld/take_screenshot` accepts clipX, clipY, clipWidth and clipHeight together.
Coordinates are logical game UI coordinates, with origin at the screen's top
left and y increasing downward. They are not map cells, native desktop points,
or the PNG's physical pixel coordinates.

```json
{"fileName":"screen-area","clipX":100,"clipY":80,"clipWidth":240,"clipHeight":160,"clipPadding":0,"outputDirectory":"/absolute/test/output"}
```

## Rectangle relative to a dialog/window

Combine those same four fields with clipTargetId. The origin is the top-left of
the resolved target rectangle, including a window's border; it is not the
DoWindowContents origin. This avoids hardcoding the dialog's screen position.

```json
{"fileName":"dialog-detail","clipTargetId":"<discovered windowTargetId>","clipX":50,"clipY":50,"clipWidth":60,"clipHeight":40,"clipPadding":0,"outputDirectory":"/absolute/test/output"}
```

All four coordinates must be finite; dimensions must be positive. Padding is in
logical UI units and defaults to 8, so use 0 for precise bounds. Captures are clipped
to the rendered screen. A rectangle with no screen intersection fails. Target
coordinates are offsets from the target; they are not restricted to its bounds.

On HiDPI or scaled UI, the output dimensions may differ from the requested
logical dimensions. The returned clipRect is in physical PNG pixels with a
top-left origin; path is the crop and sourcePath is the full source screenshot.
The bridge scales each axis from the actual image and logical UI dimensions.
Always read the returned path. Reused screenshot names overwrite with a fresh
capture, rather than accepting an existing file as completion.

## Repeatable proof

The existing `scripts/live-smoke.sh --waylith-poc --game-root PATH --gabs-bin PATH`
now checks a 6x4 map-cell crop with camera restoration, a whole proof window,
window-relative and screen-relative crops of a known red region, exact sampled
crop/source pixel correspondence, invalid partial-coordinate rejection and fresh
pixels when reusing a screenshot name after shader color changes. It also records
the headless negative case. See [autonomous development](autonomous-development.md).
