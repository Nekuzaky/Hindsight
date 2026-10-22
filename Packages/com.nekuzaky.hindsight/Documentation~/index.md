# Hindsight documentation

Hindsight is a rewind debugger for the Unity Editor. While you play, it records every object of your scene. When
something goes wrong, you pause, drag the timeline back, and the scene shows every object exactly as it was, with the
Console errors, your bookmarks, the velocities and your own variables on the same timeline.

It only runs in the editor. Nothing from Hindsight is compiled into your builds.

## Guides

| Page | What you will find |
|---|---|
| [Getting started](getting-started.md) | Install the package and rewind your first bug with the demo. |
| [The Hindsight window](window.md) | Header, transport, timeline, events, selected object and graph, with every mouse and keyboard control. |
| [Scene view](scene-view.md) | Motion trails, ghosts of destroyed objects and the overlay. |
| [Recording your own values](tracked-values.md) | `[HindsightTrack]`, pinning fields from the Inspector, velocities and bookmarks from code. |
| [Preferences](settings.md) | Every option, its default and what it changes. |
| [Performance and how it works](performance.md) | Measured cost, memory, and the architecture behind it. |
| [Limitations and troubleshooting](troubleshooting.md) | What Hindsight does not do, and answers to common questions. |

## At a glance

- **Zero setup.** Install the package and press Play. No component to add, no code to write.
- **Zero cost in builds.** The recorder lives in an editor-only assembly; the two calls you can make from game code are
  removed by the compiler outside the editor.
- **Light in the editor.** Burst jobs read the transforms on worker threads; the main thread spends a few hundredths
  of a millisecond per frame and never allocates managed memory once warmed up.
- **Rewind in place.** The real objects move back, with their real materials, lights and cameras, so the Scene view
  and the Game view both show the past.
