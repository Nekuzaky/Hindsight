# Recording your own values

Transforms are always recorded. On top of that, Hindsight records values you can read at any frame and plot in the
graph.

## Rigidbody velocities

Every `Rigidbody` and `Rigidbody2D` gets its linear and angular velocity recorded while the object moves (on the frame
it moves and the frame after it stops). Turn it off with **Rigidbody velocities** in the preferences.

## `[HindsightTrack]`

Mark a field of any MonoBehaviour:

```csharp
using Nekuzaky.Hindsight;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    [HindsightTrack] public float Health;
    [HindsightTrack("AI state")] private EnemyState _state;
    [field: HindsightTrack] public Vector3 Target { get; private set; }
}
```

- Public and private fields both work, and so do inherited ones.
- For an auto-property, put the attribute on its backing field with `[field: HindsightTrack]`.
- The optional argument is the name shown in the window.
- Supported types: `float`, `double`, `int`, `uint`, `long`, `short`, `ushort`, `byte`, `sbyte`, `bool`, enums,
  `Vector2`, `Vector3`, `Vector4`, `Vector2Int`, `Vector3Int`, `Quaternion` (shown as Euler angles) and `Color`.
- The attribute is marked `[Conditional("UNITY_EDITOR")]`, so it does not exist in your builds.

Each field is read through a getter compiled once with expression trees: no reflection and no allocation per frame.

## Pin a field from the Inspector

Right-click any field of one of your scripts in the Inspector and choose **Track in Hindsight**. The pin applies to
that field on every component of that type (and of types deriving from it). Pins are listed, and can be removed, in the
preferences. Nested fields of serializable structs and classes work (for example `stats.health`); array elements do
not.

A field pinned during Play Mode is recorded on objects that appear from then on, and on every object from the next Play
Mode.

## Bookmarks from code

```csharp
using Nekuzaky.Hindsight;

if (rareBranchTaken)
    Hindsight.Bookmark("Rare branch: path recomputed");
```

The bookmark appears as a blue diamond on the timeline at the current frame. Like the attribute, the call is removed by
the compiler outside the editor, so you can leave it in shipping code. Call it from the main thread.

You can also press **F9** (rebind it in **Edit > Shortcuts > Hindsight**) or click the bookmark button of the window.
