# The Hindsight window

Open it with **Tools > Hindsight > Open Hindsight**, **Window > Analysis > Hindsight** or `Ctrl+Alt+H`.

![The Hindsight window while rewinding](https://raw.githubusercontent.com/Nekuzaky/Hindsight/main/docs/images/screenshot-rewind.png)

## Header

| Element | Meaning |
|---|---|
| Status | **Idle** outside Play Mode, **Recording**, **Paused** (live scene), **Rewinding** (the scene shows the past) or **Off**. |
| Objects | Objects alive now / slots used. A slot is reused once nothing in the history remembers its previous object. |
| Memory | Memory holding the frames kept now. The tooltip shows how much was reserved (the memory budget). |
| Cost | Main-thread time Hindsight used per captured frame, smoothed. The tooltip adds the 95th percentile. |
| Bookmark | Adds a bookmark at the current frame (`F9`). |
| Save | Saves the recording to a `.hindsight` file. See [saving and opening recordings](#saving-and-opening-recordings). |
| Open | Opens a saved recording, outside Play Mode. |
| Close | Shown while a saved recording is open: closes it and puts every object back. |
| Record | Turns recording in Play Mode on or off. Off, Hindsight costs nothing at all. |
| Gear | Opens the [preferences](settings.md). |
| Question mark | Opens this documentation. |

## Transport

| Button | Action | Keyboard (timeline focused) |
|---|---|---|
| First | Oldest recorded frame. | `Home` |
| Previous event | Newest event before the displayed frame. Respects the event filters. | `Shift+Left` |
| Step back | One frame back. Pauses the game first if it is running. | `Left` |
| Pause / play | Pauses or resumes the game. Resuming always starts from the live scene. | |
| Step forward | One frame forward, while rewinding. | `Right` |
| Next event | Oldest event after the displayed frame. | `Shift+Right` |
| Live | Puts every object back where it was and leaves the rewind. | `End` |
| Resume here | Shown while rewinding in Play Mode. Carries the game on from the displayed frame (see below). | |
| Fit | Shows the whole recording and follows new frames again. | `F`, or double-click |

**Resume here** keeps the objects where the displayed frame shows them, gives every Rigidbody and Rigidbody2D the
velocity it had then, forgets the recording after that frame and runs the game again. A bookmark marks where it
resumed. Your scripts keep their current state (health, timers, AI), so use it to replay a physics moment or retry a
jump, not to undo game logic.

The readout shows how far back the displayed frame is (`-2.315 s`), its game frame number, and how many frames and
seconds the history holds.

## Find when

The bar under the transport jumps to the frame where a condition becomes true. Press `Ctrl+F` to focus it.

| Write | To find |
|---|---|
| `y < -5` | When the selected object goes below -5 (also `x`, `z`, in local space). |
| `speed > 20` | When its Rigidbody goes faster than 20 m/s (also `vx`, `vy`, `vz`, `angular`). |
| `Health <= 0` | When one of its tracked fields reaches 0, by the name shown in the panel. `true` and `false` work for booleans. |
| `any y < -10` | When any recorded object goes below -10, and selects that object. `any` works with `x`, `y` and `z`. |

Operators: `<`, `<=`, `>`, `>=`, `==`, `!=`. `Enter` finds the next moment after the displayed frame (from the start
of the recording when the scene is live), `Shift+Enter` the previous one. Only transitions count: a value that stays
below -5 for ten frames is found once, where it crosses. Focus the field to see which names the selected object offers.

## Timeline

![Timeline](https://raw.githubusercontent.com/Nekuzaky/Hindsight/main/docs/images/screenshot-recording.png)

- **Ruler**: seconds relative to now.
- **Event lane**: red circles are errors and exceptions, amber triangles warnings, grey dots logs, blue diamonds
  bookmarks, teal and slate triangles objects appearing and being destroyed, white squares scene loads. A message repeated on consecutive frames is one marker with a bar covering its range.
- **Activity lane**: one bar per frame, as tall as the number of values that changed. Spikes show where things happened.
- **Playhead**: green and labelled `LIVE` while the scene is live, purple while rewinding. The tinted area on its right
  is the part of the recording that lies in the future of the displayed frame.

| Do this | To |
|---|---|
| Click or drag | Show that frame. The game pauses if it was running. |
| Click a marker | Jump to that event. |
| Mouse wheel | Zoom around the cursor. |
| `Shift` + wheel, middle-drag or `Alt` + drag | Pan. |
| Double-click | See the whole recording again. |
| Hover | Read the time, or the message of the marker under the cursor. |

## Events

Every recorded event, newest last. The chips filter errors, warnings, bookmarks, logs, and objects and scenes, and show their counts; the
search field filters by text. Selecting an event rewinds to it and shows its full message; each script line of its
stack trace becomes a button that opens the script at that line.

## Selected object

Select any object in the Hierarchy or the Scene view. The panel shows, at the displayed frame:

- its **Transform** (local position, rotation and scale),
- its **values**: linear and angular velocity for Rigidbody and Rigidbody2D, and every [tracked field](tracked-values.md),
- whether it existed yet, and when it appeared.

The **Graph** plots one of these values over the whole recording, on the same time axis as the timeline. Each pixel
draws the minimum and maximum of the frames it covers, so a one-frame spike is never hidden. Click or drag on the graph
to rewind. The header buttons frame the object in the Scene view and copy its Transform as it is at the displayed frame,
so you can paste the values after leaving Play Mode (right-click the Transform > **Paste Component Values**).

If the object is not recorded, the panel says why: static, excluded layer, camera excluded, or the object limit.

## Saving and opening recordings

Click **Save** (or **Tools > Hindsight > Save Recording...**) while a recording is in memory, in Play Mode. The file
holds every kept frame, the events with their stack traces, and the values of every object, bit for bit; it is about
as large as the memory shown in the header.

To look at it later, leave Play Mode, open the scene it was recorded in and click **Open** (or **Tools > Hindsight >
Open Recording...**). The window shows the timeline, the events, the graphs and **Find when** as they were. Dragging
the timeline moves the objects of the open scene to where they were: each recorded object is found again by its place
in the hierarchy (scene, parents, name and sibling index). Objects created while playing, or renamed or moved since,
have no counterpart: their events and values stay in the window, and the header's object count tooltip says how many
were not found.

The scene is put back as it was when you click **Live** or **Close**, enter Play Mode, save the scene, open another
one, or when scripts recompile. Rewinding never changes what is saved in your scene.
