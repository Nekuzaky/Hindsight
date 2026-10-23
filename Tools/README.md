# Tools

Scripts used to develop Hindsight. They need Windows PowerShell 5.1 or PowerShell 7, and the Unity version written in
`ProjectSettings/ProjectVersion.txt` (set `UNITY_EDITOR` to the path of `Unity.exe` if it is installed elsewhere).
There is no CI: run them locally before a pull request or a release. Results and logs go to `Tools/Output`, which is
not committed.

| Script | What it does | Time |
|---|---|---|
| `Compile.ps1 [-Full]` | Compiles in batch mode and fails on any error or warning. `-Full` rebuilds every assembly instead of the changed ones. | ~1 min |
| `Test.ps1 [-Filter Name]` | Runs the EditMode tests and prints the failures. | ~3 min |
| `Benchmark.ps1` | Plays six scenarios with recording off and on, and writes `Output/benchmark/benchmark.md`. | ~3 min |
| `Check-Features.ps1` | Plays a scene with a Rigidbody and a Rigidbody2D, rewinds, resumes, saves the recording, opens it outside Play Mode and checks every step. Writes `Output/feature-check/report.txt`. | ~1 min |
| `Capture-Media.ps1` | Opens the editor, plays the demo, rewinds to the comet that falls through the floor and saves the README screenshots and GIF to `docs/images`. Needs ffmpeg and a visible screen; do not touch the mouse while it runs. | ~2 min |
| `Render-Brand.ps1 [-NoBrowser]` | Renders `docs/brand/*.html` into the banner, the GitHub social preview and the editor icons: a local server plus `docs/brand/render.html` opened in your browser. | seconds |
| `Build-Check.ps1` | Builds the demo for Windows and proves Hindsight stays out of it: no editor assembly in the player, `Hindsight.Bookmark` calls removed, and the player runs without exceptions. | ~2 min |
| `Check-Rules.ps1` | Checks the code rules: functions of 60 lines at most, no recursion, no `goto`, no `unsafe`, no warning silenced with `#pragma`, warnings as errors for the assemblies under `Assets`, and never in the shipped package. | seconds |
| `Sync-Sample.ps1` | Copies `Assets/Demo` into the package sample `Samples~/Demo`. | seconds |

The scripts that start Unity refuse to run while the project is open in the editor, because Unity cannot open a project twice.
