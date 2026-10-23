# Contributing to Hindsight

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Getting started

1. Install **Unity 6000.3** (6000.3.13f1 is the tested version).
2. Add the repository root in Unity Hub (*Add project from disk*). The tool is the embedded package in
   `Packages/com.nekuzaky.hindsight`; the demo lives in `Assets/Demo` and development-only tools in `Assets/Dev`.
3. Open `Assets/Demo/Hindsight Demo.unity`, press Play and open **Tools > Hindsight > Open Hindsight**.

Every development task is a script in [`Tools/`](Tools/README.md). Those that start Unity need the project to be closed
in the editor.

| Script | What it does |
|---|---|
| `Tools/Compile.ps1` | Compiles every assembly and lists errors and warnings. |
| `Tools/Test.ps1` | Runs the EditMode tests. |
| `Tools/Benchmark.ps1` | Measures the cost of recording in real Play Mode. |
| `Tools/Capture-Media.ps1` | Records the README screenshots and GIF from the real editor. |
| `Tools/Render-Brand.ps1` | Renders the banner, the social preview and the editor icons. |
| `Tools/Build-Check.ps1` | Builds the demo player and checks that Hindsight stays out of it. |
| `Tools/Check-Rules.ps1` | Checks the code rules listed below. |
| `Tools/Sync-Sample.ps1` | Copies `Assets/Demo` into the package sample. |

## Rules the code follows

Hindsight follows the spirit of NASA's *Power of 10* rules, adapted to C#:

- No recursion and no `goto`; every loop has a fixed upper bound.
- No managed allocation while recording once warmed up (a test checks it); memory is reserved when Play Mode starts.
- Functions stay short, under 60 lines.
- `Debug.Assert` guards the invariants of the recording core.
- Return values are checked; settings are clamped when they are set.
- No `unsafe` code. Native memory goes through `NativeArray`.
- Zero compiler warnings. `Tools/Compile.ps1` fails on any warning (`-Full` rebuilds everything), and the development
  assemblies under `Assets` build with `-warnaserror+`. The package itself does not force warnings as errors: a
  deprecation in a future Unity version must never stop someone else's project from compiling.

Style: 4 spaces, `_camelCase` private fields, `#region` blocks like the existing files, English everywhere.
The window styles live in `Editor/UI/Hindsight.uss`.

## Pull requests

- One topic per pull request, with a short description of what changed and how you tested it.
- Run `Tools/Test.ps1`; for changes to recording or rewinding, also run `Tools/Benchmark.ps1` and compare.
- Update `Packages/com.nekuzaky.hindsight/CHANGELOG.md` under an `Unreleased` section.
- UI changes: include a screenshot of the window.
