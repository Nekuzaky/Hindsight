# Preferences

**Edit > Preferences > Hindsight** (or the gear in the window). Settings are saved per user in
`UserSettings/HindsightSettings.asset`, which Unity keeps out of version control, so each person on a team can tune
Hindsight without affecting the others.

![Preferences](https://raw.githubusercontent.com/Nekuzaky/Hindsight/main/docs/images/screenshot-preferences.png)

## Recording

| Option | Default | What it does |
|---|---|---|
| Record in Play Mode | On | Starts a recording every time you enter Play Mode. Off, Hindsight does nothing and costs nothing. Can be toggled during Play Mode. |
| History length | 30 s | How far back you can rewind (5 to 600 s). Changes apply immediately. |
| Memory budget | 512 MB | Memory reserved when Play Mode starts (32 MB to 8 GB). When it is full, the oldest frames are dropped first, so a very busy scene keeps less than the history length. Applies at the next Play Mode. |
| Capture rate | Every frame | Every frame gives exact rewinds. 120, 60, 30 or 15 Hz record fewer frames in very fast scenes. |
| Object limit | 100,000 | Objects beyond this count are not recorded. The memory budget also caps it: a frame where everything moves must fit in a quarter of the transform memory. |

## What to record

| Option | Default | What it does |
|---|---|---|
| Rigidbody velocities | On | Linear and angular velocity of `Rigidbody` and `Rigidbody2D`, while they move. |
| Tracked fields | On | Fields marked `[HindsightTrack]` and fields pinned from the Inspector. |
| Ignore static objects | On | Static objects cannot move, so recording them only uses memory. |
| Ignore cameras | Off | When on, cameras stay where they are while you rewind, so the Game view keeps its current point of view. |
| Layers | Everything | Only objects on these layers are recorded. |

## Events

| Option | Default | What it does |
|---|---|---|
| Errors and exceptions | On | Red marker for every error, exception and failed assertion logged during a frame. |
| Warnings | On | Amber marker for every warning. |
| Logs | Off | Grey marker for every `Debug.Log`. Can be noisy. |
| Editor messages | Off | Also records messages logged by editor code between two frames (inspectors, importers, the editor itself). Off keeps the timeline about your game. |
| Collapse repeated messages | On | A message repeated within two frames extends the previous marker (with a count) instead of adding a new one. |

## Rewinding and Scene view

| Option | Default | What it does |
|---|---|---|
| Hide objects not spawned yet | On | While rewinding, hides the renderers of objects that did not exist at the displayed frame. |
| Motion trail | On | Draws the path of the selected objects. |
| Ghosts of destroyed objects | On | Marks where destroyed objects were at the displayed frame. |
| Scene view overlay | On | Shows the Hindsight overlay in Scene views. |

## Pinned fields

Fields pinned with **Track in Hindsight**. Remove one with its bin button. **Reset to defaults** restores every option
above and keeps your pins. **Edit shortcuts** opens the Shortcut Manager, where every Hindsight command can be bound:

| Command | Default |
|---|---|
| Hindsight/Add Bookmark | `F9` |
| Hindsight/Step Back, Step Forward, Previous Event, Next Event, Back to Live | Unbound |
| Tools > Hindsight > Open Hindsight | `Ctrl+Alt+H` |
