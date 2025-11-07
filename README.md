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

- Run the game in a real terminal; GUI-integrated consoles that redirect input will show a warning and exit.
- The splash screen explains the controls—press any key to start climbing, `Q`/`Esc` to bail, `R` to retry after a fall.
- Movement: hold `A`/`D` or the arrow keys to drift horizontally, tap `Space`/`W`/`↑` for a manual boost, and use `S`/`↓` to dive through gaps. The cyan left/right rails block you, keeping the player inside the scrollable column.
- Every landing advances one level toward the 256-level “stack overflow” goal. Stats (level, score, timers, progress bar, controls) now sit at the bottom of the console so the playfield stays clear.
- Global results live in `scoreboard.jsonl` (newline-delimited JSON under version control). Open another tab/window (`Ctrl+Shift+T` / `Cmd+T`) and run `dotnet run -- leaderboard` to stream the live standings; close that tab with `Q`/`Esc` whenever you’re done.
- Input is read on a background thread and the main loop uses a fixed 50 ms cadence, so gameplay stays smooth even on slower terminals.
