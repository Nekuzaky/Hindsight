# Performance and how it works

## Measured cost

Measured with `Tools/Benchmark.ps1`: real Play Mode, the real player loop, each scenario played twice (recording off
and on) for 1,800 frames at a fixed 60 frames per simulated second, after 120 warm-up frames.

| Scenario | Objects recorded | Hindsight per frame (avg / p95 / max) | Frame time off → on | Memory for 30 s | GC per frame | Seek | Graph data |
|---|---:|---:|---:|---:|---:|---:|---:|
| 10,000 objects, none moving | 10,003 | 0.012 / 0.019 / 0.106 ms | 0.06 → 0.16 ms | 1.2 MB | 0 B | 0.47 ms | 0.45 ms |
| 1,000 objects, all moving | 1,003 | 0.019 / 0.026 / 0.169 ms | 0.20 → 0.26 ms | 80.8 MB | 0 B | 2.06 ms | 1.31 ms |
| 10,000 objects, 1,000 moving | 10,003 | 0.016 / 0.022 / 0.271 ms | 0.22 → 0.28 ms | 82.3 MB | 0 B | 2.12 ms | 1.02 ms |
| 10,000 objects, all moving | 10,003 | 0.050 / 0.083 / 0.500 ms | 1.23 → 1.39 ms | 368.0 MB* | 0 B | 2.02 ms | 0.67 ms |
| 500 Rigidbody2D bodies | 504 | 0.020 / 0.062 / 0.181 ms | 0.13 → 0.25 ms | 10.0 MB | 0 B | 0.69 ms | 0.08 ms |
| Churn: 20 objects created and 20 destroyed per frame | 1,603 | 0.130 / 0.200 / 1.032 ms | 0.38 → 0.59 ms | 50.7 MB | 0 B | 2.16 ms | 0.69 ms |

Intel Core i7-14700KF, Unity 6000.3.13f1, editor in batch mode.

- **Hindsight per frame** is the main-thread time spent in Hindsight's own callbacks.
- **Frame time off → on** is the whole frame. The difference also contains the worker-thread job that the next frame
  waits for: in batch mode nothing is rendered, so the job has nothing to overlap with. In the normal editor it runs
  while the frame renders.
- **Seek** is the time to rebuild and show any recorded frame. **Graph data** is the time to extract one object's
  values over the whole history.
- \* With 10,000 objects moving every frame, the default 512 MB budget holds about 14 seconds instead of 30. Raise the
  budget if you need more.

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

A moving object costs 44 bytes per frame it moves (position, rotation, scale and its slot), so about 2.6 KB per second
at 60 fps. An object that does not move costs nothing after its first frame. Velocities and tracked values cost 20
bytes per change. On top of that, a full copy of the state is kept about once per second, so any frame can be rebuilt
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
