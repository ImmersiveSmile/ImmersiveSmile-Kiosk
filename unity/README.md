# Unity capture integration

This editor-only source is already installed in the ImmersiveSmilePlusVR project. It depends on that project's BLEController, BleSensorInput, BleTriggerSource, AdaptivePresentation and scene catalogue; this repository is the standalone kiosk, not the full Unity project.

To install in another checkout of ImmersiveSmilePlusVR, place Editor/KioskSceneRecorder.cs inside an Assets/.../Editor directory, replacing an existing copy rather than duplicating the class. Use ImmersiveSmile → Kiosk → Record All Scene Previews (Simulated Input). The source writes frames under Temp/KioskRecordings and reports under Web/Kiosk/recordings in the Unity project. The encoder in tools/encode_recordings.py is intended to run at Web/Kiosk/tools in that Unity project. Copy its completed assets/recordings, recordings and recordings.js back to this kiosk repository after regenerating.

The tool injects synthetic BLE samples and scripted presentation commands in offline Play Mode. It does not train or run DQN inference.
