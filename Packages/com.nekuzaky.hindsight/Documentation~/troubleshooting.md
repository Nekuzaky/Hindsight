# Limitations and troubleshooting

## What Hindsight does not do

- **It does not resume the game from the past.** Rewinding shows the past; resuming always continues from the live
  state. Physics, animation, particles, audio and your scripts' variables are not written back.
- **Only Transforms are moved.** Velocities and tracked fields are shown in the window and the graph, not applied.
- **Destroyed objects cannot be brought back.** They appear as ghosts while rewinding.
- **Objects deactivated since** stay inactive: Hindsight never calls `SetActive`, to avoid running your `OnEnable` and
  `OnDisable` code.
- **Reparenting.** Transforms are recorded in local space. If an object changed parent during the recording, the frames
  before the change are applied relative to its current parent.
- **Editor only.** Hindsight does not record in player builds. `Hindsight.Bookmark` and `[HindsightTrack]` are removed
  from builds.
- **ECS / DOTS entities** are not recorded; Unity's Entities Journaling covers them.
- **Console messages from other threads** (jobs, async code) are not turned into markers.

## Questions

**The timeline stays empty.**
Check that **Record in Play Mode** is on (the record button in the window header is red), and that the game is not
paused before its first frame.

**An object is not recorded.**
Select it: the panel on the right says why. Usually it is static (see **Ignore static objects**), on an excluded layer,
or the object limit or memory budget was reached (the header shows the object count).

**The history is shorter than the history length.**
The memory budget is full: a scene where many objects move every frame uses more memory per second. Raise the budget
in the preferences; it applies at the next Play Mode.

**A field marked `[HindsightTrack]` does not show up.**
Check that its type is supported (see [Recording your own values](tracked-values.md)) and that the component was on
the object when the object first appeared. A warning in the Console names unsupported fields.

**I see editor errors on the timeline.**
They were logged during a frame. Messages logged between frames by editor code are ignored unless **Editor messages**
is on.

**Can I leave Hindsight installed for the whole team?**
Yes. It records only in the editor, and each person can turn recording off in their own preferences.

**Does it work with Enter Play Mode Options (no domain reload)?**
Yes. The recorder is created when Play Mode starts and disposed when it ends, whatever the reload settings.
