# Performance and how it works

## Measured cost

Measured with `Tools/Benchmark.ps1`: real Play Mode, the real player loop, each scenario played twice (recording off
and on) for 1,800 frames at a fixed 60 frames per simulated second, after 120 warm-up frames. Moving objects bob up
and down and turn around their vertical axis every frame, like a walking character.

| Scenario | Objects recorded | Hindsight per frame (avg / p95 / max) | Frame time off → on | Memory for 30 s | GC per frame | Seek | Graph data |
|---|---:|---:|---:|---:|---:|---:|---:|
| 10,000 objects, none moving | 10,003 | 0.019 / 0.031 / 0.051 ms | 0.05 → 0.14 ms | 1.2 MB | 0 B | 0.39 ms | 0.32 ms |
| 1,000 objects, all moving | 1,003 | 0.019 / 0.021 / 0.128 ms | 0.20 → 0.30 ms | 36.9 MB | 0 B | 2.41 ms | 0.58 ms |
| 10,000 objects, 1,000 moving | 10,003 | 0.020 / 0.044 / 0.104 ms | 0.21 → 0.29 ms | 39.7 MB | 0 B | 2.73 ms | 0.35 ms |
| 10,000 objects, all moving | 10,003 | 0.072 / 0.184 / 0.420 ms | 1.35 → 1.67 ms | 375.7 MB* | 0 B | 3.77 ms | 0.92 ms |
| 500 Rigidbody2D bodies | 504 | 0.021 / 0.066 / 0.129 ms | 0.13 → 0.24 ms | 5.0 MB | 0 B | 0.94 ms | 0.03 ms |
| Churn: 20 objects created and 20 destroyed per frame | 1,603 | 0.149 / 0.269 / 1.061 ms | 0.35 → 0.62 ms | 15.9 MB | 0 B | 2.79 ms | 0.76 ms |

Intel Core i7-14700KF, Unity 6000.3.13f1, editor in batch mode.

- **Hindsight per frame** is the main-thread time spent in Hindsight's own callbacks.
- **Frame time off → on** is the whole frame. The difference also contains the worker-thread job that the next frame
  waits for: in batch mode nothing is rendered, so the job has nothing to overlap with. In the normal editor it runs
  while the frame renders.
- **Seek** is the time to rebuild and show any recorded frame. **Graph data** is the time to extract one object's
  values over the whole history.
- \* 10,000 objects moving every frame fill the default 512 MB budget, which holds about 29 of the 30 seconds (14
  seconds before version 1.1). Raise the budget if you need more.

## In a real game

Field test in a first-person HDRP horror game in production, Unity 6000.5.9f1, its main scene played for 25 seconds
with recording off, then on:

| Measure | Result |
|---|---|
| Objects recorded | 1,803 |
| Hindsight per frame | 0.018 ms on average, 0.022 ms at the 95th percentile, 0.041 ms at most |
| Managed allocation | 0 bytes over 53,885 frames |
| Rewind to a random frame (200 tries) | 0.10 ms on average, 5.3 ms at most |
| Back to live | every transform and renderer restored exactly |
| New Console messages caused by recording | none |

## Memory

Only what changed is stored, bit for bit: 8 bytes to say which object and which of its ten transform values
changed, then 4 bytes per changed value. An object falling straight down costs 12 bytes per frame, an object sliding
on a plane 16, a character that walks and turns 24, and an object whose position, rotation and scale all change 48.
That is about 1.4 KB per second at 60 fps for a walking character, where every move cost 44 bytes before version 1.1.
An object that does not move costs nothing after its first frame. Velocities and tracked values cost 20 bytes per
change. On top of that, a full copy of the state is kept about once per second, so any frame can be rebuilt
from a nearby copy.

## How it works

```
Frame N                                                Frame N+1
┌───────────┐  ┌──────────────────────────────┐        ┌──────────────────────────────┐
│ Update,   │  │ Start of PostLateUpdate:     │        │ Start of Initialization:     │
│ physics,  │─▶│  find new objects            │─ ─ ─ ▶ │  wait for the job (done)     │
│ LateUpdate│  │  schedule the capture job ───┼──┐     │  record deaths, velocities,  │
└───────────┘  └──────────────────────────────┘  │     │  tracked fields; close frame │
                                                 │     └──────────────────────────────┘
                   worker threads (Burst) ◀──────┘
                   read every transform, keep the ones that changed, write them to the ring
```

- **No GameObject, no MonoBehaviour.** Hindsight adds three systems to Unity's player loop when Play Mode starts and
  removes them when it ends. It works with and without domain reload.
- **Burst jobs.** A read-only `IJobParallelForTransform` reads every recorded transform on worker threads and flags
  the ones that changed; a second job writes only those into a ring buffer, in slot order.
- **Fixed memory.** Every buffer is reserved when Play Mode starts. Recording does not allocate managed memory once
  warmed up; an EditMode test checks it. When the buffers are full, the oldest second of history is dropped.
- **Checkpoints.** About once per second, the whole state is copied. Rebuilding a frame copies the nearest checkpoint
  and replays at most one second of changes, in a Burst job.
- **Finding new objects without walking the scene.** Each root keeps a signature: its transform count and its deepest
  last child. Unity appends new objects at the end of their parent, so when a signature changes only the end of the
  rightmost branch is read, up to the first object already known. A slow round-robin sweep of small roots catches the
  rare object inserted in the middle of its siblings while another is destroyed on the same frame.
- **Slots.** Each recorded transform gets a slot for its whole life; a destroyed object's slot is reused once no kept
  frame mentions it.
- **Rewinding** snapshots the live transforms, rebuilds the chosen frame and writes it with an
  `IJobParallelForTransform`. Going live writes the snapshot back.

## Code rules

The code follows the spirit of NASA's *Power of 10* rules: no recursion, bounded loops, no allocation in the
recording path, short functions, assertions on the invariants, no `unsafe` code, and zero compiler warnings (checked
by `Tools/Compile.ps1`). `Tools/Check-Rules.ps1` verifies the checkable ones.
