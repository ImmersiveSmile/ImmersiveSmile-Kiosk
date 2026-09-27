# Immersive Smile kiosk

Standalone touchscreen remote controller for the ImmersiveSmile organization. Includes all 16 scene recordings and their simulation validation traces.

Portrait-first, static HTML remote controller. Run from this directory:

```sh
python3 -m http.server 5173 --bind 127.0.0.1
```

Open http://127.0.0.1:5173. No installation or frontend build is required. Landscape and narrow layouts are supported. For an installed kiosk, serve these files on HTTPS and use the browser's managed kiosk mode.

## Experience

The kiosk opens directly on a four-card world gallery. Tap a world to see an automatic silent preview; tap Start to begin. No welcome gate or search keyboard is needed. Nature, magic and adventure filters help visitors browse, with clear Previous/More controls. The Start action stays visible at the bottom of the preview screen, including on phones.

All 16 scene IDs remain available as 12 world cards: Garden's four versions and Valley's two versions are grouped inside their preview screens. The exact selected variant is sent to the backend. MainScene (0) remains the technical lobby. Next world lets visitors compare previews without returning to the gallery. Age guidance, staff setup and preview sound are secondary controls. Motion-sensitive visitors' reduced-motion preference disables automatic preview playback and decorative transitions. Active sessions never idle-reset; other screens return to the first gallery page after three minutes.

The session screen keeps the recording, status and End action; the unavailable interaction meter has been removed. Space Island's empty scene remains previewable but cannot be started from the visitor screen.

Only one video element is created at a time. The kiosk uses ordinary muted, looping video playback; it never renders the Unity scene or streams the headset. Preview pause affects only the video. Scene recordings cannot represent live interaction/adaptation, and are explicitly labelled prerecorded.

## Unity recordings

All 16 playable scenes have new, distinct 24-second Unity Play Mode recordings at 960×540, 24 fps, H.264/AAC. Each clip captures the real scene camera and Unity audio mix. The kiosk plays one video at a time; sound is muted until the visitor enables it.

The recording harness feeds repeatable randomized stress-ball packets (Newtons, BPM and GSR) through BLEController and BleSensorInput. It applies three scripted targets through AdaptivePresentation: light calming, stronger calming, then easing back. This demonstrates interaction and the real presentation layer; it is **not trained DQN inference, online learning, patient data or a live headset feed**. The same provenance is burned into each video.

Per-scene CSV traces and capture reports live in `recordings/`. Reports include the camera, squeeze-trigger count, audio samples and runtime errors. `recordings.js` maps each exact Unity scene name to its own video and poster. Garden variants have separate recordings. Clips begin after a brief scene warmup; StarHeart warms through its opening cave sequence.

To regenerate (inside the separate ImmersiveSmilePlusVR Unity project; see [Unity integration](unity/README.md)):

1. Exit Unity Play Mode and use **ImmersiveSmile → Kiosk → Record All Scene Previews (Simulated Input)**.
2. Run `python3 Web/Kiosk/tools/encode_recordings.py --watch` from the Unity project root. For a deliberate full re-encode, first replace `Web/Kiosk/recordings.js` with `window.KIOSK_RECORDINGS = {};`.
3. Watch `Temp/KioskRecordings/status.txt`; refresh the kiosk when encoding finishes.

The tool refuses scene dependencies containing the project's Firebase clients, uses a temporary Play Mode start scene, and restores it afterward. It never saves scene assets. Temporary JPEG frames and raw audio stay under `Temp/KioskRecordings` and can be removed after confirming the encoded videos. Play Mode renders are not a Quest performance benchmark. The recordings show the current authored scenes, including their existing visual limitations. `spaceIsland` currently contains only the default sky/camera/light and editor painter settings, without the island environment. Its video honestly shows that empty scene. Farm completed with eight existing mesh-readability errors from animal outline effects (plus Animator parameter warnings). These issues are called out in the scene detail pages. If Unity has Error Pause enabled, resume Play Mode to finish Farm capture after those errors; the tool preserves that editor preference.

Staff can override any scene's recording with a playable MP4/WebM under 200 MB. Files remain in the kiosk browser's IndexedDB; clearing browser data removes overrides. The generated welcome illustration is concept artwork. Age suggestions remain provisional content guidance, not clinical approval or verified content ratings.

## Remote configuration

Staff setup takes the API base URL ending in `/api`, a hospital-member bearer token, patient UUID and an online headset. Credentials remain in memory, not localStorage. Configure the backend CORS allowlist for the deployed kiosk origin. The staff panel is configuration UI, not an authentication boundary; the backend must enforce permissions.

- GET `/scenarios`: resolves exact `scene_index` + `unity_scene_name` to backend UUID.
- GET `/scenarios/headsets`: lists online headsets and polls reported scene every five seconds.
- POST `/scenario-sessions`: creates and dispatches the selected session.
- POST `/scenario-sessions/{id}/end`: completes the session and requests lobby return.

The UI distinguishes dispatched commands from reported current scene. A session end response confirms backend completion, not that the headset has rendered its lobby. Network failures retain the active session and permit an end retry. If launch times out, verify the backend portal before doing anything else: a server-side launch may have succeeded. Backend-side idempotency is required for guaranteed duplicate prevention across browser restarts or ambiguous failures. Reloading a remote session loses its in-memory session; a browser warning discourages this. Use the existing staff portal to recover/stop a session after a crash or reload.

No physiological readings, connected stress-ball state, DQN learning or live adaptation are fabricated. This version does not expose that telemetry; the session screen directs staff to the headset for current status.

## Validation

JavaScript syntax and local browser flow are checked. Live hardware operation requires valid backend credentials and an available headset; no live session was started during implementation.
