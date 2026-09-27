# HeadView — Windows first version

Godot 4.6.1 .NET first-person prototype with automatic single-/dual-screen startup, in-scene SubViewports sharing one world, and a camera built into the game process for head-based focus control. No separate tracker service is needed.

## Run

1. Open this directory in **Godot 4.6.1 .NET** (the Mono/.NET editor, not the standard editor).
2. Let Godot restore the C# NuGet packages, then build the C# project and run the main scene.
3. Startup automatically selects dual-screen mode when at least two monitors are connected: one borderless window spans the two leftmost monitors and each viewport fills one monitor. With one monitor, a borderless window fills the primary monitor and renders one forward-facing view. On a dual-monitor system, shoot the `SCREEN MODE` toggle on the 3D board to switch between dual and single views; single mode uses the primary monitor, and switching back restores the spanning window.

Command-line build on Windows:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' restore HeadView.csproj
& 'C:\Program Files\dotnet\dotnet.exe' build HeadView.csproj --no-restore
```

The game runs the complete MediaPipe Face Landmarker Tasks pipeline inside the Godot process through its Windows C API. The official `face_landmarker.task` model is bundled as a Godot resource and copied to `user://` at startup. `native/libmediapipe.dll` comes from the official MediaPipe 1.0.1 Windows x64 wheel and is copied next to the built .NET assembly. No Python installation, tracker service, or bridge process is needed at runtime. MediaPipe's license and third-party notices are in `assets/MEDIAPIPE-LICENSE.txt` and `assets/MEDIAPIPE-NOTICE.txt`.

## Controls

| Control | Action |
|---|---|
| Mouse | Rotate the whole player body and current view |
| WASD | Move |
| Shift | Run |
| Space | Jump |
| Left click | Shoot the ray at the aim point; hit 3D buttons to change settings |
| C | Set the current detected head pose as neutral |
| `[` / `]` | Decrease / increase the horizontal seam gap by 0.5° |
| B | Toggle the focus blur |
| Esc | Release the mouse; click the game window to capture it again |
| Backspace | Quit the game |

The large circular crosshair is centered on the seam in dual mode or on the screen in single mode. The hit ray follows that center direction. Targets flash when hit, and the HUD counts hits. A compact 3D settings board stands ahead and to the left of the spawn point; in single mode it moves closer to screen center. Shoot its minus/plus buttons to adjust head-direction sensitivity and mouse sensitivity in 0.1x steps (0.1x–3.0x), or clear radius in 0.05 steps (0.10–1.25). The `HUD CAMERA` toggle shows/hides only the camera image; landmark markers and pose values stay visible. The `SCREEN MODE` toggle switches the current display mode when two monitors are available. Toggle labels stay unchanged and their color indicates state. Button hits do not count as target hits.

## View geometry

Both cameras occupy the same point at the player's head. `HorizontalFovDeg` is the horizontal FOV **of each active screen**; `GapHorizontalDeg` is the blind angle between their inner edges in dual mode. Both are exported properties on `Main` in the Godot Inspector. The default is 70° per screen with a 0° gap. Godot's camera property is a vertical FOV, so the code derives it from each SubViewport's aspect ratio. In dual mode camera yaw offsets are half of `(HorizontalFovDeg + GapHorizontalDeg)` in opposite directions. In single mode the one visible camera faces straight ahead and the gap is ignored until dual mode is restored.

Head tracking never rotates either camera. It moves a clear circular area across the two rendered views. The default clear radius is `0.64` in screen-height units and can be changed from the 3D settings board; outside it the view becomes progressively blurrier and darker. `B` disables the entire focus effect. If no face is visible, the clear area returns to the seam center. The board's head sensitivity multiplier scales both yaw and pitch after neutral-pose calibration; it defaults to 1.00x.

The focus position uses a physical screen model: each screen is `ScreenPhysicalWidthMeters` wide (default **0.53 m**), and the head is `HeadToScreenDistanceMeters` from the screen plane (default **0.5 m**). Both properties are on `Main` in the Inspector. For estimated yaw θ and pitch φ, the ray meets the screen plane at `x = distance × tan(θ)` and `y = distance × tan(φ) / cos(θ)`. In dual mode the head is centered in front of the seam, so each screen spans one full physical width to the left or right. In single mode the head is centered in front of the one screen, so its left/right edges are at ±half the physical width; the same angle therefore moves the focus twice as far across the visible screen. Each display's height is derived from its pixel aspect ratio; out-of-screen intersections clamp to the nearest edge.

## Built-in head tracking

The first camera is selected by `headview/camera_index` in `project.godot` (default `0`). The requested capture size is set by `headview/capture_width` and `headview/capture_height` (default **1280×720**); the camera may return a different size, so the actual frame dimensions are printed at startup and shown in the HUD status. OpenCvSharp captures the camera inside the game process and MediaPipe Tasks handles detection and tracking in video mode. It returns 478 landmarks and its own canonical-to-runtime face transformation matrix. The matrix supplies yaw, pitch and roll; no six-point PnP fit remains. Press `C` while looking straight ahead to recalibrate. The focus movement is smoothed and capped, and loss of the face returns the focus to center.

Windows camera privacy settings must allow desktop applications to access the camera. The HUD reports whether the camera is unavailable, searching for a face, active, or has a tracker error. This is a webcam-based approximation of head motion, so exact physical angular calibration is not guaranteed. It does not perform eye tracking.

The left edge of the right screen holds the HUD preview in dual mode; in single mode it sits at the top right of the active screen. It overlays five selected landmarks and a direction arrow on the corresponding, horizontally mirrored camera view. Switching the camera image off removes only that image: the landmarks, direction arrow, and yaw/pitch/roll values remain in place. With the image on, the view shows a tight square crop derived from the detected landmarks while tracking and an aspect-preserving full-frame preview when no face is detected. The preview stays in memory and is not saved to disk. Startup does not take the first detected face as neutral, since that face may already be turned; the initial neutral offset is zero. Press `C` while facing forward to set a custom neutral pose. The focus position uses the estimated head orientation only, not the face's left/right position within the camera frame. MediaPipe's pose matrix uses a virtual camera, so it is not a calibrated measurement of physical gaze.

## Export

The included Windows Desktop export preset includes the `.task` model. Install the Godot 4.6.1 .NET Windows export templates before exporting. The native OpenCV library is supplied by the NuGet Windows runtime package; `libmediapipe.dll` is copied by the C# project file. Export with the project built for Windows x64 and verify that both native DLLs are beside the exported .NET assembly.

## Source

- `Main.tscn`: top-level scene with the world, player, both SubViewportContainers and cameras, and a single full-window HUD. These nodes are visible in the editor Scene tree.
- `World.tscn`: environment, lights, floor, walls, cover, and target instances.
- `Player.tscn`: character collision and head center; the cameras use the head center's transform at runtime.
- `Target.tscn`: reusable target visual and collision.
- `SettingsPanel.tscn`: in-world settings board, labels, six +/- buttons, and camera-image and screen-mode toggles.
- `src/Main.cs`: FPS controls, spanning-window layout, camera geometry, aim ray, HUD state, and focus projection. It references scene nodes instead of creating them.
- `src/SettingButton3D.cs`: visual feedback for shootable settings buttons.
- `src/HeadTracker.cs`: in-process camera capture, MediaPipe tracking state, and debug preview.
- `src/MediaPipeFaceLandmarker.cs`: in-process MediaPipe Tasks C API integration for landmarks and the face matrix.
- `src/FaceLandmarksView.cs`: face landmark and direction overlay on the cropped camera preview in the HUD.
- `assets/focus.gdshader`: clear-circle screen effect.
- `src/SeamCrosshair.cs`: seam-centered circular crosshair.
- `tools/qa_settings_panel.gd`: headless regression check for scene loading and eight shootable button colliders.
