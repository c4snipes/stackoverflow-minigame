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
- Use `A`/`D` or the arrow keys to sprint across platforms; landing on one launches you higher.
- Keep the player (`@`) on screen—falling below the camera or pressing `Q` ends the run.
- Your score comes from the highest altitude reached; the HUD also shows your current height and a progress bar toward the goal (250 world units).
- Near-real-time input is handled on a background thread so movement stays responsive without freezing the frame loop.
