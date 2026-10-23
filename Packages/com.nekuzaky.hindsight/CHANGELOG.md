# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **Find when:** type a condition such as `y < -5`, `speed > 20`, `Health <= 0` or `any y < -10` and jump to the
  frame where it becomes true; `Enter` for the next match, `Shift+Enter` for the previous one, `Ctrl+F` to focus.
  `any` searches the position of every recorded object and selects the one it finds.
- **Save and open recordings:** the window's Save button writes the recording to a `.hindsight` file; Open shows it
  again outside Play Mode, with the timeline, events, graphs and Find when, and rewinds the objects of the open scene
  found at the same place in the hierarchy. Also in **Tools > Hindsight**.
- **Lifecycle markers:** the timeline marks frames where objects appear or are destroyed ("3 objects appeared") and
  where scenes load. They have their own filter chip and can be turned off in the preferences.

### Changed

- **Recordings take about half the memory.** Only the transform values that changed are stored, bit for bit, so the
  same budget holds a longer history: 12 to 24 bytes per moving object and frame instead of 44. The benchmark scene
  with 1,000 moving objects went from 80.8 MB to 36.9 MB for 30 seconds. Checkpoints are also taken more often when
  frames come very fast, which keeps seeks short.
- The package description says exactly what reaches a build: a 5 KB assembly whose calls are removed.

## [1.0.2] - 2026-10-23

### Fixed

- Selecting an object while the game was running threw an `InvalidOperationException` on every refresh of the window:
  the panel read the recording while the capture job of that frame still owned it. Every read from the window, the
  graph and the Scene view now finishes the pending capture first.
- Messages logged by editor windows between two frames could appear on the timeline as game errors. A frame now ends
  with Hindsight's end-of-frame system or the editor update, whichever comes first.

### Verified

- Visible play session in the same HDRP game on Unity 6000.5.9f1, with the player driven around and the object
  selected while recording: no exception, 0 bytes of managed allocation, the UI renders correctly, and rewinding,
  jumping to a bookmark and going live all work.

## [1.0.1] - 2026-10-23

### Fixed

- The package did not compile on Unity 6000.5 and newer, where `Object.GetInstanceID()` is obsolete as an error.
  Hindsight now identifies objects with `EntityId`, which exists since Unity 6000.3.

### Changed

- The package no longer turns compiler warnings into errors in your project, so a deprecation in a future Unity
  version cannot stop your project from compiling. Hindsight itself is still developed with zero warnings.

### Verified

- Field test in a first-person HDRP game on Unity 6000.5.9f1: 1,803 objects recorded, 0.018 ms per frame on average
  (95th percentile 0.022 ms), no managed allocation over 53,885 frames, 200 random rewinds averaging 0.10 ms, and the
  scene restored exactly every time.

## [1.0.0] - 2026-10-23

First release.

### Added

- Automatic recording in Play Mode of every transform, with Burst jobs and no managed allocation once warmed up.
- Rewinding in place while the game is paused: the real objects move back, objects not spawned yet are hidden, and
  everything returns live before the game runs again.
- Timeline with ruler, per-frame activity, error, warning, log and bookmark markers, zoom and pan.
- Event list with filters, search, full messages and clickable stack trace lines.
- Selected object panel with transform, velocities and tracked fields at the displayed frame, and a min/max graph.
- Rigidbody and Rigidbody2D velocities, `[HindsightTrack]` fields and fields pinned from the Inspector.
- `Hindsight.Bookmark()` and the `F9` shortcut.
- Scene view motion trails, ghosts of destroyed objects and an overlay with frame controls.
- Preferences page for every option, saved per user.
- Bouncing Balls Demo sample.
