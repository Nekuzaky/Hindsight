# Scene view

## Rewinding the real scene

While you rewind, Hindsight moves the real objects to where they were. Materials, lights, post-processing, cameras and
the Game view are all right, because nothing is redrawn by Hindsight: Unity renders your scene as usual.

- Objects that **did not exist yet** at the displayed frame are hidden (their renderers only, nothing else is touched).
- Objects that were **destroyed since** cannot be moved back, so a ghost marks where they were (see below).
- When you resume the game, step a frame, or leave Play Mode, every object is put back exactly where it was before
  you started rewinding. The game never runs on top of a rewound scene.

Only Transforms are rewound. Rigidbody velocities, animation state, particles and your scripts' variables keep their
live values: Hindsight shows the recorded ones in its window instead of writing them back.

## Motion trail

Select up to eight recorded objects to see their path over the whole recording. The part already travelled at the
displayed frame is solid, the rest is faint. A dot marks the displayed frame; red and blue dots on the path mark the
errors and bookmarks.

## Ghosts of destroyed objects

While rewinding, a dotted cross marks where each destroyed root object was at the displayed frame, for example the
bullets that hit something since. Toggle them in the [preferences](settings.md).

## Overlay

The **Hindsight** overlay in the Scene view shows the recording status, how far back the scene
is, and, while paused, buttons to step frames, jump between events, go live and open the window. Like every overlay,
you can move it, collapse it, or hide it from the Scene view's overlay menu (`` ` ``). The **Scene view overlay**
preference shows or hides it everywhere.
