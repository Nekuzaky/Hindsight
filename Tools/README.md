# Tools

Scripts used to develop Hindsight. They need Windows PowerShell 5.1 or PowerShell 7, and the Unity version written in
`ProjectSettings/ProjectVersion.txt` (set `UNITY_EDITOR` to the path of `Unity.exe` if it is installed elsewhere).
There is no CI: run them locally before a pull request or a release. Results and logs go to `Tools/Output`, which is
not committed.

| Script | What it does | Time |
|---|---|---|
| `Compile.ps1` | Compiles every assembly in batch mode. Hindsight builds with warnings as errors, so any line it prints is a failure. | ~1 min |
| `Test.ps1 [-Filter Name]` | Runs the EditMode tests and prints the failures. | ~3 min |
| `Benchmark.ps1` | Plays six scenarios with recording off and on, and writes `Output/benchmark/benchmark.md`. | ~3 min |
| `Capture-Media.ps1` | Opens the editor, plays the demo, rewinds to the comet that falls through the floor and saves the README screenshots and GIF to `docs/images`. Needs ffmpeg and a visible screen; do not touch the mouse while it runs. | ~2 min |
| `Render-Brand.ps1 [-NoBrowser]` | Renders `docs/brand/*.html` into the banner, the GitHub social preview and the editor icons: a local server plus `docs/brand/render.html` opened in your browser. | seconds |
| `Check-Rules.ps1` | Checks the code rules: functions of 60 lines at most, no recursion, no `goto`, no `unsafe`, and a `csc.rsp` with `-warnaserror+` next to every assembly definition. | seconds |
| `Sync-Sample.ps1` | Copies `Assets/Demo` into the package sample `Samples~/Demo`. | seconds |

The scripts that start Unity refuse to run while the project is open in the editor, because Unity cannot open a project twice.
