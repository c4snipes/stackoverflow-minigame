# stackoverflow-minigame

You’re a stack frame trying to climb higher without overflowing.

## Build and run

Requirements: .NET SDK (9.0 or later) installed. Verify with `dotnet --version`.

From the repository root:

1. Build:

   ```bash
   dotnet build
   ```

2. Run:

   ```bash
   dotnet run
   ```
   
The project uses a single SDK-style project file `stackoverflow-minigame.csproj` in the repository root, and a solution file `stackoverflow-minigame.sln` is already included.

## Gameplay

- Launch the game in a real terminal window so key presses can be captured (GUI IDE consoles that redirect input will exit with a warning).
- At the splash screen press any key to “start recursion”; `Q`/`Esc` exits immediately.
- Use `A`/`D` or the arrow keys to glide horizontally—even mid-flight—for diagonal control; tap `Space`, `W`, or `↑` for a manual boost (longer cooldown) and `S`/`↓` to fast-drop through gaps.
- Keep the player (`@`) on screen—falling below the camera or pressing `Q`/`Esc` triggers a StackOverflowException screen; `R` restarts after a crash.
- A cyan border marks the playable area and also acts as a collision boundary; stay within it or you’ll feel the hit detection clamp movement.
- Before each run you’ll enter classic 3-character arcade initials; every platform you touch adds one level to your score toward the 256-level marathon goal, the HUD shows those initials plus a stopwatch, and the crash screen presents the top-3 scores and fastest runs (with initials and times).
- Global records persist in `scoreboard.jsonl` (tracked in git). Entries are stored as newline-delimited JSON with automatic merge resolution, so multiple players can contribute without hand-editing conflicts—just commit the updated file.
- To view the live leaderboard, open another terminal tab/window (e.g., shortcut `Ctrl+Shift+T` / `Cmd+T`) and run `dotnet run -- leaderboard`. That tab refreshes every second and can be closed with `Q`/`Esc`, while the main game keeps running in the original terminal on every OS.
- Input is captured on a non-blocking listener and movement uses smoothed frame timing so gameplay stays responsive.
