# Hindsight

The rewind debugger for Unity. Hindsight records your scene while you play. When something goes wrong, pause, drag the
timeline back, and see every object exactly as it was, with errors, bookmarks, velocities and your own variables on
the same timeline.

- Zero setup: install and press Play.
- Zero cost in builds: editor-only, and the two runtime calls are compiled out.
- A few hundredths of a millisecond per frame in the editor, Burst jobs, no garbage.

## Install

**Window > Package Manager > + > Install package from git URL...**

```
https://github.com/Nekuzaky/Hindsight.git?path=Packages/com.nekuzaky.hindsight
```

Requires Unity 6000.3 or newer.

## Use

1. Press Play.
2. Open **Tools > Hindsight > Open Hindsight** (`Ctrl+Alt+H`).
3. When something goes wrong, pause and drag the timeline back, or click an error marker.

```csharp
using Nekuzaky.Hindsight;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    [HindsightTrack] public float Health;      // recorded every frame, plotted in the window

    void OnPathFailed() => Hindsight.Bookmark("Path failed");   // marker on the timeline
}
```

## Documentation

See [Documentation~/index.md](Documentation~/index.md) or the
[repository](https://github.com/Nekuzaky/Hindsight).

MIT License. Icons: [Bootstrap Icons](https://icons.getbootstrap.com/) (MIT), see `Third Party Notices.md`.
