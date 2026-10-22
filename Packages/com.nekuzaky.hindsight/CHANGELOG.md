# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

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
