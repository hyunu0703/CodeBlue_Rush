# GamePlay PC / touch input verification

## Cause and fix

`AmbulanceDriveInput` previously enabled the touch controls and read their values only when
`IsMobileBuild` was true. A false browser/platform classification disabled all driving touch
input, while Start's ordinary UI Button had no such gate. The original version also only
recognized native Android/iOS builds; a later revision added WebGL browser detection.

Driving now accepts primary UI pointer events whenever driving is allowed, independently of
platform classification or Touchscreen-specific event metadata. This also accepts left mouse
input and browser pointer compatibility events; secondary buttons are rejected.
Keyboard and touch values are combined, brakes retain priority, and
a held touch wheel takes priority over keyboard steering. Platform detection remains only for
orientation, safe-area presentation, and the gear keyboard hint. No public API, serialized field,
scene, package, or assembly migration is required.

This explains a failure path in the source; the affected phone's browser/device events and its
cached deployed binary were not directly inspected, so a device-specific cause is not proven.

## Executed checks

Unity 6000.3.23f1, Input System 1.20.0, uGUI 2.0.0.
Two separate Unity processes ran the final Play Mode component regression harness:
**26 passed / 0 failed each, exit code 0**. See `acceptance1.txt` and `acceptance2.txt`.

Coverage: false mobile-platform classification, touch pedals, two finger IDs, wrong-finger
release, pedal exit, metadata-free primary pointers, rejected secondary buttons,
W/A/D/Space/E, steering ownership, D/R at a physics callback boundary,
focus loss/recovery, held-key release, pause/resume, and disable/re-enable.

These checks run inside Unity with synthetic Input System keyboard state and direct UI
pointer-handler calls. They explicitly invoke component callbacks, and do not exercise browser
touch delivery, UI raycasting, actual wheel drag geometry, or a physical phone. They are a custom
regression harness, not Unity Test Framework XML results. Earlier harness attempts failed due to
headless input routing and settings cleanup; the final harness handles both.

## Re-run in an isolated copy

1. Copy only `Assets`, `Packages`, and `ProjectSettings` into a fresh short temporary path.
   Do not use an already open Editor project or copy/edit `Library`/`Temp`.
2. Copy `MobileInputVerification.cs` into the copy's `Assets/Editor`. Let Unity generate metadata.
3. Run the matching Unity executable with `-batchmode -nographics -projectPath <copy>`
   `-executeMethod MobileInputVerification.Run -logFile <unique-log>`, **without `-quit`**.
   The harness enters Play Mode and exits with 0 on success and 1 on failure. It depends on this
   project's configured Domain Reload disabled / Scene Reload enabled settings.
4. Check process completion, all 26 assertions, final summary, and errors. Enforce a 30-minute
   timeout on each process. Repeat with a fresh log.
5. To build the copied project, use `-batchmode -nographics -quit -buildTarget WebGL`
   `-projectPath <copy> -executeMethod MobileInputVerification.BuildWebGL -logFile <unique-log>`.
   Output is `<copy>/docs`; verify success before replacing the repository's deployment output.

The harness stays outside `Assets` in the real project, so it needs no `.meta` there and is not
included in the game. Unity-generated metadata and test settings changes are confined to the
isolated copy. Existing scenes and tuning are preserved in the real project.

## Physical-device checks still required

After publishing the rebuilt `docs` output, reload the site with its old cache cleared:

- PC: Start → GamePlay, W acceleration, A/D steering, Space braking, E shifting while stopped.
- Android Chrome / iPhone Safari: Start → GamePlay, hold accelerator while dragging the wheel,
  brake with a second finger, release controls, and tap D/R while stopped.
- Switch browser tabs and return: no stuck pedal/steering; fresh touch works. Repeat a scene entry.

Viewport emulation alone is not a substitute for real touch-event delivery or device testing.
Publishing requires a commit/push; this task does not grant that permission under AGENTS.md.

## AGENTS.md suggestions

Verified executable: `C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe`.
Consider replacing `UNITY_PATH: TODO` with that path and filling the comment-language,
async-style, and timeout TODOs. The policy's PC target list should also acknowledge this task's
WebGL target. Standalone scripting backend remains unknown and was not needed for this fix.
