using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Security;

namespace stackoverflow_minigame
{
    internal enum GameState { Menu, Running, Over }

    /// <summary>
    /// Main game controller managing state, input, rendering, and gameplay loop.
    /// Coordinates between World, Renderer, Input, Spawner, and Scoreboard systems.
    /// </summary>
    internal class Game
    {
        static Game()
        {
            // Route diagnostics to stderr by default to keep stdout clean for gameplay
            Diagnostics.FailureReported += DefaultFailureLogger;
        }

        private GameState state = GameState.Menu;
        private bool running = true;
        private World world;
        private readonly Renderer renderer;
        private readonly Input input;
        private readonly Spawner spawner;
        private readonly Scoreboard scoreboard;
        private readonly InitialsPrompt initialsPrompt;
        private int framesClimbed = 0;
        private int bestFrames = 0;
        private bool playerWon = false;
        private bool fastDropQueued = false;
        private int horizontalDirection = 0;
        private float horizontalIntentTimer = 0f;
        private readonly Stopwatch runStopwatch = new();
        private string playerInitials = "AAA";
        private bool initialsConfirmed = false;
        private enum HudMode { Full, Compact, Hidden }
        private HudMode hudMode = HudMode.Full;
        private bool isFirstRun = true;

        // Initials-entry callbacks so callers can monitor ASCII conversion state.
        public event Action? InitialsPromptStarted;
        public event Action<char>? InitialsCharAccepted;
        public event Action<char>? InitialsCharRejected;
        public event Action<string>? InitialsCommitted;
        public event Action? InitialsCanceled;
        public event Action<string>? InitialsFallbackUsed;

        private const int TargetFrameMs = 50;
        private const float MinDeltaSeconds = 0.01f;
        private const float MaxDeltaSeconds = 0.1f;
        internal const float FrameTimeSeconds = TargetFrameMs / 1000f;
        internal const float GravityPerSecond = -2.5f; // Reduced gravity for easier gameplay and more air control
        private const int MIN_PROGRESS_BAR_WIDTH = 10;
        private const int MAX_PROGRESS_BAR_WIDTH = 60;
        private const float HorizontalIntentMemorySeconds = 0.12f;
        private const float DangerZoneThreshold = 3f;

        private static void DefaultFailureLogger(string message)
        {
            try
            {
                Console.Error.WriteLine($"[Diagnostics] {message}");
            }
            catch
            {
                // Ignore console failures while logging diagnostics.
            }
        }

        public Game()
        {
            // Changed world width from 80 to 60 to improve platform density and enhance gameplay pacing.
            // This adjustment makes the game area more compact, increasing challenge and reducing empty space.
            world = new World(60, 38);
            renderer = new Renderer();
            input = new Input();
            spawner = new Spawner();
            scoreboard = new Scoreboard(Scoreboard.ResolveDefaultPath());
            initialsPrompt = new InitialsPrompt(input);
        }

        public void Run()
        {
            bool cursorHidden = false;
            try
            {
                Console.CursorVisible = false;
                cursorHidden = true;
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to hide cursor.", ex);
            }
            catch (SecurityException ex)
            {
                Diagnostics.ReportFailure("Insufficient permissions to hide cursor.", ex);
            }

            input.Start();
            try
            {
                while (running)
                {
                    try
                    {
                        switch (state)
                        {
                            case GameState.Menu:
                                MenuLoop();
                                break;
                            case GameState.Running:
                                GameLoop();
                                break;
                            case GameState.Over:
                                OverLoop();
                                break;
                        }
                    }
                    catch (IOException ex)
                    {
                        Diagnostics.ReportFailure("Console IO error during game loop.", ex);
                        running = false;
                    }
                    catch (SecurityException ex)
                    {
                        Diagnostics.ReportFailure("Console permission error during game loop.", ex);
                        running = false;
                    }
                }
            }
            finally
            {
                input.Dispose();
                if (cursorHidden)
                {
                    try
                    {
                        Console.CursorVisible = true;
                    }
                    catch
                    {
                        // ignore cursor restore failures
                    }
                }
            }
        }

        private void MenuLoop()
        {
            bool redrawMenu = true;
            while (state == GameState.Menu && running)
            {
                if (redrawMenu)
                {
                    RenderMenuHeader();
                    input.ClearBuffer();
                    redrawMenu = false;
                }

                if (!input.SupportsInteractiveInput)
                {
                    try
                    {
                        Console.WriteLine();
                        Console.WriteLine("Interactive console input isn't available in this environment.");
                        Console.WriteLine("Run inside a terminal window to take the jump!");
                    }
                    catch (IOException ex)
                    {
                        Diagnostics.ReportFailure("Failed to print interactive input warning.", ex);
                    }
                    Thread.Sleep(2000);
                    running = false;
                    return;
                }

                if (!input.WaitForKey(100, out var key))
                {
                    continue;
                }
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                {
                    running = false;
                    return;
                }
                if (key.Key == ConsoleKey.L)
                {
                    ShowLeaderboard();
                    redrawMenu = true;
                    continue;
                }
                StartRun();
                return;
            }
        }

        private void RenderMenuHeader()
        {
            try
            {
                Console.Clear();
                ConsoleSafe.TrySetCursorPosition(0, 0);
                ConsoleSafe.WriteLine("╔═══════════════════════════════════════════════════════════════════════╗");
                ConsoleSafe.WriteLine("║               STACKOVERFLOW: THE STACK CLIMBING GAME                  ║");
                ConsoleSafe.WriteLine("╚═══════════════════════════════════════════════════════════════════════╝");
                ConsoleSafe.WriteLine("");
                ConsoleSafe.WriteLine("🎯 THE LEGEND:");
                ConsoleSafe.WriteLine("   You are a stack frame in the call stack, climbing toward the limit.");
                ConsoleSafe.WriteLine("   Each platform represents a function call pushing you higher.");
                ConsoleSafe.WriteLine("   Your mission: reach the legendary 256th level (2^8 - a byte's worth!)");
                ConsoleSafe.WriteLine("   without falling and causing a STACK OVERFLOW exception.");
                ConsoleSafe.WriteLine("");
                ConsoleSafe.WriteLine("   Jump too deep and you'll overflow. Climb too high and you'll succeed!");
                ConsoleSafe.WriteLine("   Can you conquer all 256 levels before the stack unwinds?");
                ConsoleSafe.WriteLine("");
                ConsoleSafe.WriteLine("Press any key to push onto the stack (Q/Esc to return, L for leaderboard)");
                ConsoleSafe.WriteLine("");
                ConsoleSafe.WriteLine(GlyphLibrary.StatusSummary);
                ConsoleSafe.WriteLine(string.Empty);
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to render the menu header.", ex);
            }
        }

        private void StartRun()
        {
            if (!EnsureInitialsSet())
            {
                state = GameState.Menu;
                return;
            }

            // Only show countdown on first run, skip on restarts
            if (isFirstRun)
            {
                ShowCountdown();
                isFirstRun = false;
            }
            else
            {
                // Quick "GO!" message for restarts
                try
                {
                    Console.Clear();
                    int centerRow = Console.WindowHeight / 2;
                    int centerCol = Console.WindowWidth / 2;

                    Console.ForegroundColor = ConsoleColor.Green;
                    ConsoleSafe.TrySetCursorPosition(0, centerRow);
                    ConsoleSafe.WriteLine($"{new string(' ', Math.Max(0, centerCol - 2))}GO!".PadRight(Console.WindowWidth));
                    Console.ResetColor();

                    TryPlayTone(1000, 150);
                    Thread.Sleep(300);
                }
                catch
                {
                    // Silently continue if display fails
                }
            }

            state = GameState.Running;
            framesClimbed = 0;
            playerWon = false;
            fastDropQueued = false;
            horizontalDirection = 0;
            horizontalIntentTimer = 0f;
            world.Reset();
            spawner.Update(world); // seed platforms into the visible window
            world.Player.VelocityY = World.JumpVelocity;
            runStopwatch.Reset();
            runStopwatch.Start();
            input.ClearBuffer();
        }

        private void ShowCountdown()
        {
            try
            {
                Console.Clear();
                ConsoleSafe.TrySetCursorPosition(0, 0);

                int centerCol = Console.WindowWidth / 2;

                for (int i = 5; i >= 1; i--)
                {
                    Console.Clear();
                    int centerRow = Console.WindowHeight / 2;

                    // Display "ADJUST YOUR WINDOW SIZE NOW!" message
                    ConsoleSafe.TrySetCursorPosition(0, centerRow - 5);
                    ConsoleSafe.WriteLine($"{new string(' ', Math.Max(0, centerCol - 15))}ADJUST YOUR WINDOW SIZE NOW!".PadRight(Console.WindowWidth));
                    ConsoleSafe.WriteLine("");
                    ConsoleSafe.WriteLine($"{new string(' ', Math.Max(0, centerCol - 10))}GET READY TO CLIMB...".PadRight(Console.WindowWidth));
                    ConsoleSafe.WriteLine("");

                    // Get the glyph for the current number
                    char numberChar = i.ToString()[0];
                    string[] glyphLines = GlyphLibrary.GetGlyph(numberChar);

                    // Set color based on countdown number
                    Console.ForegroundColor = i <= 3 ? ConsoleColor.Yellow : ConsoleColor.Green;
                    if (i == 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }

                    // Display each line of the glyph centered
                    foreach (string glyphLine in glyphLines)
                    {
                        int glyphWidth = glyphLine.Length;
                        int leftPadding = Math.Max(0, centerCol - glyphWidth / 2);
                        ConsoleSafe.WriteLine($"{new string(' ', leftPadding)}{glyphLine}".PadRight(Console.WindowWidth));
                    }

                    Console.ResetColor();

                    // Play ascending tones for countdown
                    int frequency = 400 + (i * 100);
                    TryPlayTone(frequency, 200);

                    Thread.Sleep(1000);
                }

                // Final "GO!" message with block letters
                Console.Clear();
                int finalRow = Console.WindowHeight / 2;

                Console.ForegroundColor = ConsoleColor.Green;

                // Render "GO!" using glyphs side by side
                string[] glyphG = GlyphLibrary.GetGlyph('G');
                string[] glyphO = GlyphLibrary.GetGlyph('O');
                string[] glyphExclaim = GlyphLibrary.GetGlyph('!');

                ConsoleSafe.TrySetCursorPosition(0, finalRow - 2);
                for (int row = 0; row < GlyphLibrary.GlyphHeight; row++)
                {
                    // Combine glyphs horizontally with spacing
                    string combinedLine = $"{glyphG[row]}  {glyphO[row]}  {glyphExclaim[row]}";
                    int leftPadding = Math.Max(0, centerCol - combinedLine.Length / 2);
                    ConsoleSafe.WriteLine($"{new string(' ', leftPadding)}{combinedLine}".PadRight(Console.WindowWidth));
                }

                Console.ResetColor();

                // Play final "go" tone
                TryPlayTone(1000, 200);

                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Diagnostics.ReportWarning($"Countdown display failed: {ex.Message}");
                // Continue anyway - the game should still start
            }
        }

        private void ShowLeaderboard()
        {
            using (input.PauseListening())
            {
                input.ClearBuffer();
                try
                {
                    LeaderboardViewer viewer = new LeaderboardViewer();
                    viewer.Run(embedded: true);
                }
                catch (Exception ex)
                {
                    Diagnostics.ReportFailure("Failed to display the leaderboard viewer.", ex);
                    try
                    {
                        Console.WriteLine("[warning] Unable to load the leaderboard right now.");
                    }
                    catch
                    {
                        // ignore console write failures
                    }
                }
            }
        }

        private bool EnsureInitialsSet()
        {
            if (initialsConfirmed && !string.IsNullOrWhiteSpace(playerInitials))
            {
                return true;
            }

            var callbacks = new InitialsPrompt.Callbacks(
                () => InitialsPromptStarted?.Invoke(),
                ch => InitialsCharAccepted?.Invoke(ch),
                ch => InitialsCharRejected?.Invoke(ch),
                initials => InitialsCommitted?.Invoke(initials),
                () => InitialsCanceled?.Invoke(),
                fallback => InitialsFallbackUsed?.Invoke(fallback)
            );

            if (!initialsPrompt.TryCapture(out var initials, playerInitials, callbacks))
            {
                return false;
            }

            playerInitials = ProfanityFilter.FilterInitials(initials);
            initialsConfirmed = true;
            return true;
        }

        private void GameLoop()
        {
            Stopwatch deltaTimer = Stopwatch.StartNew();
            while (state == GameState.Running && running)
            {
                float deltaSeconds = Math.Clamp((float)deltaTimer.Elapsed.TotalSeconds, MinDeltaSeconds, MaxDeltaSeconds);
                deltaTimer.Restart();
                Stopwatch workTimer = Stopwatch.StartNew();

                ProcessGameplayInput(deltaSeconds);
                if (!running || state != GameState.Running)
                {
                    break;
                }

                UpdateVisibleRowBudget();
                world.Update(deltaSeconds, horizontalDirection, fastDropQueued);
                fastDropQueued = false;
                if (world.BorderHitThisFrame)
                {
                    horizontalDirection = 0;
                    horizontalIntentTimer = 0f;
                }
                ClampPlayerWithinBounds();
                spawner.Update(world);

                // Audio feedback for landing
                if (world.LandedThisFrame)
                {
                    TryPlayTone(800, 50); // Mid tone for landing
                }

                if (world.LevelAwardedThisFrame)
                {
                    framesClimbed += 1;
                    TryPlayTone(1200, 100); // Higher tone for level up
                }

                if (world.Player.Y < world.Offset)
                {
                    TriggerGameOver(false);
                }
                else if (world.LevelsCompleted >= World.GoalPlatforms)
                {
                    TriggerGameOver(true);
                }

                renderer.BeginFrame(world);
                renderer.Draw(world);
                renderer.Present();
                DrawHud();

                int sleepFor = Math.Max(0, TargetFrameMs - (int)workTimer.ElapsedMilliseconds);
                if (sleepFor > 0)
                {
                    Thread.Sleep(sleepFor);
                }
            }
        }

        private void OverLoop()
        {
            Console.Clear();
            Console.WriteLine(playerWon ? "YOU CLEARED THE ERROR STACK!" : "*** STACKOVERFLOW EXCEPTION ***");
            Console.WriteLine($"Levels cleared: {framesClimbed}");
            Console.WriteLine($"Best levels: {bestFrames}");
            Console.WriteLine($"Run time: {TimeFormatting.FormatDuration(runStopwatch.Elapsed)}");
            Console.WriteLine($"Player: {playerInitials}");
            Console.WriteLine();
            Console.WriteLine("Top Levels:");
            var topScores = scoreboard.GetTopScores(3);
            WriteScoreboard(topScores, entry => $"{entry.Initials} - {entry.Level} lvls ({TimeFormatting.FormatDuration(entry.RunTime)})");
            Console.WriteLine("Fastest Runs:");
            var fastestRuns = scoreboard.GetFastestRuns(3);
            WriteScoreboard(fastestRuns, entry => $"{entry.Initials} - {TimeFormatting.FormatDuration(entry.RunTime)} ({entry.Level} lvls)");
            Console.WriteLine("Press R to restart or Q/Esc to quit.");
            input.ClearBuffer();
            while (state == GameState.Over && running)
            {
                if (input.TryReadKey(out var key))
                {
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                    {
                        running = false;
                        return;
                    }
                    if (key.Key == ConsoleKey.R)
                    {
                        state = GameState.Menu;
                        return;
                    }
                }
                Thread.Sleep(10);
            }
        }

        private void ProcessGameplayInput(float deltaSeconds)
        {
            bool horizontalInputDetected = false;
            while (input.TryReadKey(out var key))
            {
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.A:
                        UpdateHorizontalIntent(-1);
                        horizontalInputDetected = true;
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.D:
                        UpdateHorizontalIntent(1);
                        horizontalInputDetected = true;
                        break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.S:
                        fastDropQueued = true;
                        break;
                    case ConsoleKey.H:
                        CycleHudMode();
                        break;
                    case ConsoleKey.L:
                        // Pause game and show leaderboard
                        runStopwatch.Stop();
                        ShowLeaderboard();
                        runStopwatch.Start();
                        break;
                    case ConsoleKey.Q:
                    case ConsoleKey.Escape:
                        if (state == GameState.Running)
                        {
                            playerWon = false;
                            StopAndRecordRun();
                        }
                        running = false;
                        return;
                }
            }

            if (!horizontalInputDetected && horizontalDirection != 0)
            {
                horizontalIntentTimer -= deltaSeconds;
                if (horizontalIntentTimer <= 0f)
                {
                    horizontalDirection = 0;
                    horizontalIntentTimer = 0f;
                }
            }
        }

        private void UpdateHorizontalIntent(int direction)
        {
            horizontalDirection = Math.Clamp(direction, -1, 1);
            horizontalIntentTimer = HorizontalIntentMemorySeconds;
        }

        private void CycleHudMode()
        {
            hudMode = hudMode switch
            {
                HudMode.Full => HudMode.Compact,
                HudMode.Compact => HudMode.Hidden,
                _ => HudMode.Full
            };
        }

        private void TriggerGameOver(bool won)
        {
            if (state == GameState.Over)
            {
                return;
            }

            playerWon = won;
            state = GameState.Over;
            StopAndRecordRun();
        }

        private void StopAndRecordRun()
        {
            if (runStopwatch.IsRunning)
            {
                runStopwatch.Stop();
            }
            scoreboard.RecordRun(playerInitials, framesClimbed, world.MaxAltitude, runStopwatch.Elapsed, playerWon);
            bestFrames = Math.Max(bestFrames, framesClimbed);
        }

        private static void WriteScoreboard(IReadOnlyList<ScoreEntry> entries, Func<ScoreEntry, string> formatter)
        {
            if (entries.Count == 0)
            {
                Console.WriteLine("  (no records yet)");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {formatter(entries[i])}");
            }
        }

        private void ClampPlayerWithinBounds()
        {
            world.Player.X = Math.Clamp(world.Player.X, 0f, Math.Max(0, world.Width - 1));
        }

        private void DrawHud()
        {
            if (hudMode == HudMode.Hidden)
            {
                return;
            }

            int fallbackWidth = renderer.VisibleWidth > 0 ? renderer.VisibleWidth : 1;
            int consoleWidth = ConsoleSafe.GetBufferWidth(fallbackWidth);
            if (consoleWidth <= 0)
            {
                Diagnostics.ReportFailure("DrawHud aborted because console width could not be resolved.");
                return;
            }

            int hudWidth = renderer.VisibleWidth > 0 ? Math.Min(renderer.VisibleWidth, consoleWidth) : consoleWidth;
            if (hudWidth <= 0)
            {
                Diagnostics.ReportFailure("DrawHud aborted because the HUD width resolved to zero.");
                return;
            }

            // Place the HUD immediately below the rendered playfield but never
            // beyond the console's buffer height. When the buffer is tiny we
            // clamp the HUD into the available rows.
            int consoleHeight = ConsoleSafe.GetBufferHeight(renderer.VisibleHeight + Renderer.HudRows);
            if (consoleHeight <= 0)
            {
                consoleHeight = renderer.VisibleHeight + Renderer.HudRows;
            }
            int hudStartRow = Math.Max(renderer.VisibleHeight, consoleHeight - Renderer.HudRows);
            if (hudStartRow < 0 || hudStartRow + Renderer.HudRows > consoleHeight)
            {
                hudStartRow = Math.Max(0, consoleHeight - Renderer.HudRows);
            }

            int hudRowsAvailable = Math.Max(0, consoleHeight - hudStartRow);
            bool showProgress = hudMode == HudMode.Full && hudRowsAvailable >= 2;
            bool showControls = hudMode == HudMode.Full && hudRowsAvailable >= 3;
            if (hudMode == HudMode.Compact)
            {
                showProgress = false;
                showControls = false;
            }

            int currentHeight = GetRoundedAltitude(world.Player.Y);
            int maxHeight = GetRoundedAltitude(world.MaxAltitude);
            int displayedBest = Math.Max(bestFrames, framesClimbed);
            string timeText = TimeFormatting.FormatDuration(runStopwatch.Elapsed);
            float distanceFromBottom = world.Player.Y - world.Offset;
            ConsoleColor statsColor = distanceFromBottom < DangerZoneThreshold ? ConsoleColor.Red : ConsoleColor.Gray;

            WriteHudLine(hudStartRow + 0, $"Player: {playerInitials} | Level: {world.LevelsCompleted,4} | Score: {framesClimbed,4} | Height: {currentHeight,4} | Max: {maxHeight,4} | Best: {displayedBest,4} | Time: {timeText,8}", hudWidth, statsColor);

            if (showProgress)
            {
                float progress = Math.Clamp(world.LevelsCompleted / (float)World.GoalPlatforms, 0f, 1f);
                string percentText = $"{progress * 100f:0}%";
                string goalText = $"{World.GoalPlatforms} levels";
                int reservedWidth = $"Progress:  {percentText} of goal {goalText}".Length;
                int barAvailable = Math.Max(MIN_PROGRESS_BAR_WIDTH, Math.Min(MAX_PROGRESS_BAR_WIDTH, hudWidth - reservedWidth));
                ConsoleColor progressColor = progress switch
                {
                    >= 0.66f => ConsoleColor.Green,
                    >= 0.33f => ConsoleColor.Yellow,
                    _ => ConsoleColor.Red
                };
                WriteHudLine(hudStartRow + 1, $"Progress: {BuildProgressBar(progress, barAvailable)} {percentText} of goal {goalText}", hudWidth, progressColor);
            }

            if (showControls)
            {
                WriteHudLine(hudStartRow + 2, "Controls: A/D or <-/-> move, S/↓ dives, Q/Esc quits", hudWidth, ConsoleColor.Cyan);
            }
        }

        private static int GetRoundedAltitude(float altitude) => (int)MathF.Round(altitude);

        private static void WriteHudLine(int row, string text, int widthHint, ConsoleColor? color = null)
        {
            int bufferHeight = ConsoleSafe.GetBufferHeight(-1);
            if (bufferHeight >= 0 && row >= bufferHeight)
            {
                Diagnostics.ReportFailure($"WriteHudLine skipped row {row} because it exceeds buffer height {bufferHeight}.");
                return;
            }

            int fallbackWidth = widthHint > 0 ? widthHint : 1;
            int consoleWidth = ConsoleSafe.GetBufferWidth(fallbackWidth);
            if (consoleWidth <= 0)
            {
                Diagnostics.ReportFailure("WriteHudLine aborted because console width could not be read.");
                return;
            }

            string output = text.Length > consoleWidth
                ? text[..consoleWidth]
                : text.PadRight(consoleWidth);
            if (!ConsoleSafe.TrySetCursorPosition(0, row))
            {
                return;
            }

            ConsoleColor originalColor = Console.ForegroundColor;
            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
            }
            Console.Out.Write(output);
            if (color.HasValue)
            {
                Console.ForegroundColor = originalColor;
            }
        }

        private static string BuildProgressBar(float progress, int width)
        {
            int clampedWidth = Math.Clamp(width, MIN_PROGRESS_BAR_WIDTH, MAX_PROGRESS_BAR_WIDTH);
            int filled = (int)MathF.Round(progress * clampedWidth);
            if (filled > clampedWidth)
            {
                filled = clampedWidth;
            }

            string bar = new string('#', filled).PadRight(clampedWidth, '.');
            return $"[{bar}]";
        }

        private void UpdateVisibleRowBudget()
        {
            int totalPadding = Renderer.HudRows + Renderer.BorderThickness * 2;
            int consoleHeight = ConsoleSafe.GetBufferHeight(world.Height + totalPadding);
            if (consoleHeight <= 0)
            {
                consoleHeight = world.Height + totalPadding;
            }
            int playableRows = Math.Max(1, consoleHeight - totalPadding);
            world.SetVisibleRowBudget(playableRows);
        }

        /// <summary>
        /// Plays an audio tone. On Windows uses Console.Beep with specified frequency.
        /// On other platforms, uses terminal bell (BEL character) for cross-platform support.
        /// </summary>
        private static void TryPlayTone(int frequency, int duration)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windows: Use Console.Beep with specific frequency
                    Console.Beep(frequency, duration);
                }
                else
                {
                    // Unix/macOS/Linux: Use terminal bell (ASCII BEL character)
                    // Most terminals will play a system beep sound
                    Console.Write('\a');
                    Console.Out.Flush();
                }
            }
            catch
            {
                // Audio not supported, silently ignore
            }
        }

    }
}
