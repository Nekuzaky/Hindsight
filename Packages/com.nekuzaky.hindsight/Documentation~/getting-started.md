# Getting started

## Requirements

- Unity **6000.3** or newer (Unity 6.3 LTS). Built-in, URP and HDRP are all supported.
- The package pulls in `com.unity.burst`, which most projects already have.

## Install

In Unity: **Window > Package Manager > + > Install package from git URL...** and paste:

```
https://github.com/Nekuzaky/Hindsight.git?path=Packages/com.nekuzaky.hindsight
```

To stay on a given release, add its tag at the end:

```
https://github.com/Nekuzaky/Hindsight.git?path=Packages/com.nekuzaky.hindsight#v1.0.0
```

You can also add the line to `Packages/manifest.json` yourself:

```json
"com.nekuzaky.hindsight": "https://github.com/Nekuzaky/Hindsight.git?path=Packages/com.nekuzaky.hindsight#v1.0.0"
```

## Your first rewind, with the demo

1. In the Package Manager, select **Hindsight**, open **Samples** and import **Bouncing Balls Demo**.
2. Open the scene `Hindsight Demo` from the imported sample and press **Play**.
3. Open **Tools > Hindsight > Open Hindsight** (or **Window > Analysis > Hindsight**, shortcut `Ctrl+Alt+H`).
   The timeline fills up as the game runs: each purple bar shows how much moved on that frame.
4. Every seventh ball is a red comet launched so fast that it skips over the floor. After a few seconds a **red
   marker** appears on the timeline: the comet's error.
5. Click the red marker, or the error in the **Events** list. The game pauses and the whole scene goes back to that
   frame.
6. Select the comet in the Hierarchy. The Scene view draws its path, straight through the floor, and the right panel
   shows its velocity at that frame: about 140 m/s.
7. Press the Left and Right arrow keys on the timeline to step one frame at a time, then **Live** (or `End`) to come
   back to the present. Resume the game whenever you want: it always continues from the live state.

## In your own project

There is nothing to set up. Enter Play Mode and Hindsight records. When something looks wrong:

- **Pause** the game (`Ctrl+Shift+P`), or press **F9** to drop a bookmark at that moment, or tick **Error Pause** in
  the Console so the game stops on the first error.
- Drag the playhead back, jump between events with `Shift+Left` and `Shift+Right`, and select objects to read their
  values at that frame.

Next: [the Hindsight window](window.md).
