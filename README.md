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
- **Getting started**
  - Run inside a real terminal session; redirected stdin/out disables input and the game exits after warning you.
  - On launch you’ll see the title crawl. Press any key to begin, `Q`/`Esc` quits, and `R` restarts from the Game Over screen.
  - Before the first climb you’ll be asked for arcade initials. The prompt blinks a 5×5 glyph banner and only accepts A–Z; fallback initials are used if the console can’t read keys.
- **Controls**
  - `A`/`D` or `←`/`→` slide horizontally. Holding the key sustains drift and the input system remembers intent for 0.12 s, giving smoother movement.
  - `S`/`↓` issues a fast-drop impulse so you can punch through gaps instead of waiting for gravity.
  - `H` cycles HUD modes (Full → Compact → Hidden) to reclaim vertical space on cramped buffers.
  - `Q`/`Esc` always quits and is honored anywhere outside of blocking prompts.
- **Objective & world rules**
  - The world is a 60×38 scrolling column framed in cyan. Touching the border clamps your horizontal intent so you never leave the visible window.
  - Platforms are procedurally spawned by bands: early tiers are wide and dense, higher tiers thin out and space apart. Landing on any new, higher platform increments your level and immediately relaunches the jump.
  - Falling below the camera offset or missing a platform ends the run. Reach level 256 to “clear the error stack” and trigger the win outro.
- **HUD & feedback**
  - The HUD renders beneath the playfield and shows player initials, levels cleared, landing score, altitude, max altitude, best run, and a `mm:ss.fff` stopwatch.
  - A progress bar tracks percentage to the 256-level goal and changes color (red → yellow → green) as you climb. When you are within three rows of the fail line the stats row turns red as a warning.
  - Diagnostics tracing (`dotnet run -- trace`) hooks into glyph lookup and initials entry so you can see the same events the game emits internally.
- **Persistence & leaderboards**
  - Every completed run is appended to `scoreboard.jsonl` (newline-delimited JSON). The file lives in the repo so teams can share a common board without extra services.
  - Launch `dotnet run -- leaderboard` (or `./launch-leaderboard.sh` on macOS/Linux) in another terminal to stream the top level counts and fastest runs; press `Q`/`Esc` to close the viewer.
- **Performance notes**
  - Input is read on a background thread into a concurrent queue so gameplay never blocks on `Console.ReadKey`.
  - The simulation step clamps delta time between 10–100 ms and targets a 50 ms frame, which keeps platform motion and HUD timers consistent even on slow shells.
## Runtime Flow
![Flow of Information](flowOfInformationConsoleGame.png)

> Note: Only the rendered image is provided; the PlantUML source code for this diagram is not included in the repository.
