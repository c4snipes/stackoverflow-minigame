using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace stackoverflow_minigame {
    enum GameState { Menu, Running, Over }

    class Game {
        static Game() {
            Diagnostics.FailureReported += DefaultFailureLogger;
        }

        private GameState state = GameState.Menu;
        private bool running = true;
        private World world;
        private readonly Renderer renderer;
        private readonly Input input;
        private readonly Spawner spawner;
        private readonly Scoreboard scoreboard;
        private int framesClimbed = 0;
        private int bestFrames = 0;
        private bool playerWon = false;
        private bool manualBoostQueued = false;
        private bool fastDropQueued = false;
        private int horizontalDirection = 0;
        private float horizontalIntentTimer = 0f;
        private readonly Stopwatch runStopwatch = new();
        private string playerInitials = "AAA";

        private const int TargetFrameMs = 50;
        private const float MinDeltaSeconds = 0.01f;
        private const float MaxDeltaSeconds = 0.1f;
        internal const float FrameTimeSeconds = TargetFrameMs / 1000f;
        internal const float GravityPerSecond = -4.5f; // even lighter gravity for longer airtime
        private const int MIN_PROGRESS_BAR_WIDTH = 10;
        private const int MAX_PROGRESS_BAR_WIDTH = 60;
        private const float HorizontalIntentMemorySeconds = 0.12f;
        private const string ScoreboardFileName = "scoreboard.jsonl";

        private static void DefaultFailureLogger(string message) {
            try {
                Console.Error.WriteLine($"[Diagnostics] {message}");
            } catch {
                // Ignore console failures while logging diagnostics.
            }
        }

        public Game() {
            world = new World(80, 32);
            renderer = new Renderer();
            input = new Input();
            spawner = new Spawner();
            scoreboard = new Scoreboard(ResolveScoreboardPath());
        }

        public void Run() {
            Console.CursorVisible = false;
            input.Start();
            try {
                while (running) {
                    switch (state) {
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
            } finally {
                input.Dispose();
                Console.CursorVisible = true;
            }
        }

        private void MenuLoop() {
            Console.Clear();
            ConsoleSafe.TrySetCursorPosition(0, 0);
            Console.WriteLine("STACKOVERFLOW SKY CLIMBER");
            Console.WriteLine("You are a lonely stack frame climbing toward accepted glory.");
            Console.WriteLine("Press any key to start recursion (Q/Esc to bail).");

            if (!input.SupportsInteractiveInput) {
                Console.WriteLine();
                Console.WriteLine("Interactive console input isn't available in this environment.");
                Console.WriteLine("Run inside a terminal window to take the jump!");
                Thread.Sleep(2000);
                running = false;
                return;
            }

            input.ClearBuffer();

            while (state == GameState.Menu && running) {
                if (input.TryReadKey(out var key)) {
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) {
                        running = false;
                        return;
                    }
                    StartRun();
                    return;
                }
                Thread.Sleep(10);
            }
        }

        private void StartRun() {
            if (!TryPromptForInitials(out var initials)) {
                state = GameState.Menu;
                running = true;
                return;
            }
            playerInitials = initials;
            state = GameState.Running;
            framesClimbed = 0;
            playerWon = false;
            manualBoostQueued = false;
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

        private bool TryPromptForInitials(out string initials) {
            const int maxChars = 3;
            initials = "AAA";
            string current = string.Empty;

            Console.Clear();
            Console.WriteLine("ENTER YOUR INITIALS");
            Console.WriteLine("(Letters/numbers, 3 characters. Esc to cancel.)");

            while (true) {
                Console.Write($"\rInitials: {current.PadRight(maxChars, '_')}");
                ConsoleKeyInfo keyInfo;
                try {
                    keyInfo = Console.ReadKey(intercept: true);
                } catch (InvalidOperationException) {
                    // Fallback: cannot read input, keep previous initials.
                    initials = playerInitials;
                    return true;
                }

                if (keyInfo.Key == ConsoleKey.Escape) {
                    return false;
                }

                if (keyInfo.Key == ConsoleKey.Enter) {
                    if (current.Length == 0) continue;
                    while (current.Length < maxChars) current += '_';
                    initials = current.ToUpperInvariant();
                    return true;
                }

                if (keyInfo.Key == ConsoleKey.Backspace && current.Length > 0) {
                    current = current[..^1];
                    continue;
                }

                char ch = char.ToUpperInvariant(keyInfo.KeyChar);
                if (char.IsLetterOrDigit(ch) && current.Length < maxChars) {
                    current += ch;
                }
            }
        }

        private void GameLoop() {
            Stopwatch deltaTimer = Stopwatch.StartNew();
            while (state == GameState.Running && running) {
                float deltaSeconds = Math.Clamp((float)deltaTimer.Elapsed.TotalSeconds, MinDeltaSeconds, MaxDeltaSeconds);
                deltaTimer.Restart();
                Stopwatch workTimer = Stopwatch.StartNew();

                ProcessGameplayInput(deltaSeconds);
                if (manualBoostQueued) {
                    world.TryManualBoost();
                    manualBoostQueued = false;
                }
                if (!running || state != GameState.Running) break;

                world.Update(deltaSeconds, horizontalDirection, fastDropQueued);
                fastDropQueued = false;
                if (world.BorderHitThisFrame) {
                    horizontalDirection = 0;
                    horizontalIntentTimer = 0f;
                }
                ClampPlayerWithinBounds();
                spawner.Update(world);
                if (world.LandedThisFrame) {
                    framesClimbed += 1;
                }

                if (world.Player.Y < world.Offset) {
                    TriggerGameOver(false);
                } else if (world.MaxAltitude >= World.GoalHeight) {
                    TriggerGameOver(true);
                }

                renderer.BeginFrame(world);
                renderer.Draw(world);
                renderer.Present();
                DrawHud();

                int sleepFor = Math.Max(0, TargetFrameMs - (int)workTimer.ElapsedMilliseconds);
                if (sleepFor > 0) {
                    Thread.Sleep(sleepFor);
                }
            }
        }

        private void OverLoop() {
            Console.Clear();
            Console.WriteLine(playerWon ? "YOU CLEARED THE ERROR STACK!" : "*** STACKOVERFLOW EXCEPTION ***");
            Console.WriteLine($"Score (landings): {framesClimbed}");
            Console.WriteLine($"Best run: {bestFrames}");
            Console.WriteLine($"Run time: {FormatTimeSpan(runStopwatch.Elapsed)}");
            Console.WriteLine($"Player: {playerInitials}");
            Console.WriteLine();
            Console.WriteLine("Top Scores:");
            var topScores = scoreboard.GetTopScores(3);
            WriteScoreboard(topScores, entry => $"{entry.Initials} - {entry.Score} pts ({FormatTimeSpan(entry.RunTime)})");
            Console.WriteLine("Fastest Runs:");
            var fastestRuns = scoreboard.GetFastestRuns(3);
            WriteScoreboard(fastestRuns, entry => $"{entry.Initials} - {FormatTimeSpan(entry.RunTime)} ({entry.Score} pts)");
            Console.WriteLine("Press R to restart or Q/Esc to quit.");
            input.ClearBuffer();

            while (state == GameState.Over && running) {
                if (input.TryReadKey(out var key)) {
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) {
                        running = false;
                        return;
                    }
                    if (key.Key == ConsoleKey.R) {
                        state = GameState.Menu;
                        return;
                    }
                }
                Thread.Sleep(10);
            }
        }

        private void ProcessGameplayInput(float deltaSeconds) {
            bool horizontalInputDetected = false;
            while (input.TryReadKey(out var key)) {
                switch (key.Key) {
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
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.W:
                    case ConsoleKey.Spacebar:
                        manualBoostQueued = true;
                        break;
                    case ConsoleKey.Q:
                    case ConsoleKey.Escape:
                        running = false;
                        return;
                }
            }

            if (!horizontalInputDetected && horizontalDirection != 0) {
                horizontalIntentTimer -= deltaSeconds;
                if (horizontalIntentTimer <= 0f) {
                    horizontalDirection = 0;
                    horizontalIntentTimer = 0f;
                }
            }
        }

        private void UpdateHorizontalIntent(int direction) {
            horizontalDirection = Math.Clamp(direction, -1, 1);
            horizontalIntentTimer = HorizontalIntentMemorySeconds;
        }

        private void TriggerGameOver(bool won) {
            if (state == GameState.Over) return;
            playerWon = won;
            state = GameState.Over;
            StopAndRecordRun();
        }

        private void StopAndRecordRun() {
            if (runStopwatch.IsRunning) {
                runStopwatch.Stop();
            }
            scoreboard.RecordRun(playerInitials, framesClimbed, world.MaxAltitude, runStopwatch.Elapsed, playerWon);
            bestFrames = Math.Max(bestFrames, framesClimbed);
        }

        private static string FormatTimeSpan(TimeSpan span) {
            if (span <= TimeSpan.Zero) return "--:--";
            string format = span.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss\.ff";
            return span.ToString(format);
        }

        private static void WriteScoreboard(IReadOnlyList<ScoreEntry> entries, Func<ScoreEntry, string> formatter) {
            if (entries.Count == 0) {
                Console.WriteLine("  (no records yet)");
                return;
            }

            for (int i = 0; i < entries.Count; i++) {
                Console.WriteLine($"  {i + 1}. {formatter(entries[i])}");
            }
        }

        private void ClampPlayerWithinBounds() {
            world.Player.X = Math.Clamp(world.Player.X, 0f, Math.Max(0, world.Width - 1));
        }

        private void DrawHud() {
            int fallbackWidth = renderer.VisibleWidth > 0 ? renderer.VisibleWidth : 1;
            int consoleWidth = ConsoleSafe.GetBufferWidth(fallbackWidth);
            if (consoleWidth <= 0) {
                Diagnostics.ReportFailure("DrawHud aborted because console width could not be resolved.");
                return;
            }

            int hudWidth = renderer.VisibleWidth > 0 ? Math.Min(renderer.VisibleWidth, consoleWidth) : consoleWidth;
            if (hudWidth <= 0) {
                Diagnostics.ReportFailure("DrawHud aborted because the HUD width resolved to zero.");
                return;
            }

            int currentHeight = GetRoundedAltitude(world.Player.Y);
            int maxHeight = GetRoundedAltitude(world.MaxAltitude);
            int displayedBest = Math.Max(bestFrames, framesClimbed);
            string timeText = FormatTimeSpan(runStopwatch.Elapsed);
            WriteHudLine(0, $"Player: {playerInitials} | Score: {framesClimbed,4} | Height: {currentHeight,4} | Max: {maxHeight,4} | Best: {displayedBest,4} | Time: {timeText,8}", hudWidth);

            float progress = Math.Clamp(world.MaxAltitude / World.GoalHeight, 0f, 1f);
            string percentText = $"{progress * 100f:0}%";
            string goalText = $"{World.GoalHeight:0}";
            int reservedWidth = $"Progress:  {percentText} of goal {goalText}".Length;
            int barAvailable = Math.Max(MIN_PROGRESS_BAR_WIDTH, Math.Min(MAX_PROGRESS_BAR_WIDTH, hudWidth - reservedWidth));
            WriteHudLine(1, $"Progress: {BuildProgressBar(progress, barAvailable)} {percentText} of goal {goalText}", hudWidth);

            WriteHudLine(2, "Controls: A/D or <-/-> move, S/↓ dives, Space jumps, Q/Esc quits", hudWidth);
        }

        private static int GetRoundedAltitude(float altitude) => (int)MathF.Round(altitude);

        private static void WriteHudLine(int row, string text, int widthHint) {
            int bufferHeight = ConsoleSafe.GetBufferHeight(-1);
            if (bufferHeight >= 0 && row >= bufferHeight) {
                Diagnostics.ReportFailure($"WriteHudLine skipped row {row} because it exceeds buffer height {bufferHeight}.");
                return;
            }

            int fallbackWidth = widthHint > 0 ? widthHint : 1;
            int consoleWidth = ConsoleSafe.GetBufferWidth(fallbackWidth);
            if (consoleWidth <= 0) {
                Diagnostics.ReportFailure("WriteHudLine aborted because console width could not be read.");
                return;
            }

            string output = text.Length > consoleWidth ? text[..consoleWidth] : text.PadRight(consoleWidth);
            if (!ConsoleSafe.TrySetCursorPosition(0, row)) return;
            Console.Out.Write(output);
        }

        private static string BuildProgressBar(float progress, int width) {
            int clampedWidth = Math.Clamp(width, MIN_PROGRESS_BAR_WIDTH, MAX_PROGRESS_BAR_WIDTH);
            int filled = (int)MathF.Round(progress * clampedWidth);
            if (filled > clampedWidth) filled = clampedWidth;
            string bar = new string('#', filled).PadRight(clampedWidth, '.');
            return $"[{bar}]";
        }

        private static string ResolveScoreboardPath() {
            string? located = FindFileUpwards(Directory.GetCurrentDirectory(), ScoreboardFileName);
            if (!string.IsNullOrEmpty(located)) return located;

            string? gitRoot = FindDirectoryUpwards(Directory.GetCurrentDirectory(), ".git");
            if (!string.IsNullOrEmpty(gitRoot)) {
                return Path.Combine(gitRoot, ScoreboardFileName);
            }

            return Path.Combine(Directory.GetCurrentDirectory(), ScoreboardFileName);
        }

        private static string? FindFileUpwards(string start, string fileName) {
            string? current = start;
            while (!string.IsNullOrEmpty(current)) {
                string candidate = Path.Combine(current, fileName);
                if (File.Exists(candidate)) return candidate;
                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == null || parent == current) break;
                current = parent;
            }
            return null;
        }

        private static string? FindDirectoryUpwards(string start, string directoryName) {
            string? current = start;
            while (!string.IsNullOrEmpty(current)) {
                string candidate = Path.Combine(current, directoryName);
                if (Directory.Exists(candidate)) return current;
                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == null || parent == current) break;
                current = parent;
            }
            return null;
        }
    }

    class World {
        public int Width { get; }
        public int Height { get; }
        public int Offset { get; private set; }
        public Player Player { get; private set; }
        private readonly List<Platform> platforms;
        private readonly Random rand;
        private float manualBoostCooldown = 0f;
        internal List<Platform> Platforms { get { return platforms; } }
        internal const float JumpVelocity = 5f;
        public float MaxAltitude { get; private set; }
        public const float GoalHeight = 250f;
        public bool LandedThisFrame { get; private set; }
        public bool BorderHitThisFrame { get; private set; }
        private const float ManualBoostCooldownSeconds = 0.75f;
        private const float GroundHorizontalUnitsPerSecond = 18f;
        private const float AirHorizontalSpeedMultiplier = 1.4f;
        private const float FastDropImpulse = -6f;
        private const float PlatformCatchTolerance = 1.5f;
        internal const int MinPlatformLength = 4;
        internal const int MaxPlatformLength = 8;

        public World(int width, int height) {
            Width = width;
            Height = height;
            platforms = new List<Platform>();
            rand = new Random();
            Player = new Player(width / 2, 0);
            Reset();
        }

        public void Reset() {
            ResetPlayer();
            platforms.Clear();
            Offset = 0;
            manualBoostCooldown = 0f;
            MaxAltitude = 0f;

            int firstY = 8;
            int firstLength = rand.Next(MinPlatformLength, MaxPlatformLength + 1);
            int firstStart = Math.Clamp((int)MathF.Round(Player.X) - firstLength / 2, 0, Math.Max(0, Width - firstLength));
            platforms.Add(new Platform(firstStart, firstY, firstLength));

            int secondY = firstY + 7;
            int secondLength = rand.Next(MinPlatformLength, MaxPlatformLength + 1);
            int secondMaxStart = Math.Max(0, Width - secondLength);
            int secondX = rand.Next(secondMaxStart + 1);
            platforms.Add(new Platform(secondX, secondY, secondLength));
        }

        public void Update(float deltaSeconds, int horizontalDirection, bool fastDropRequested) {
            LandedThisFrame = false;
            BorderHitThisFrame = false;
            manualBoostCooldown = MathF.Max(0f, manualBoostCooldown - deltaSeconds);
            float stepScale = deltaSeconds / Game.FrameTimeSeconds;

            float horizontalMax = Math.Max(0, Width - 1);
            float newX = Player.X;
            if (horizontalDirection != 0) {
                float horizontalSpeed = GroundHorizontalUnitsPerSecond;
                bool isAirborne = MathF.Abs(Player.VelocityY) > 0.05f;
                if (isAirborne) {
                    horizontalSpeed *= AirHorizontalSpeedMultiplier;
                }
                newX += horizontalDirection * horizontalSpeed * deltaSeconds;
            }
            float clampedX = Math.Clamp(newX, 0f, horizontalMax);
            BorderHitThisFrame = MathF.Abs(clampedX - newX) > float.Epsilon;
            Player.X = clampedX;

            if (fastDropRequested) {
                Player.VelocityY += FastDropImpulse;
            }

            Player.VelocityY += Game.GravityPerSecond * deltaSeconds;
            float oldY = Player.Y;
            float newY = oldY + Player.VelocityY * stepScale;

            if (Player.VelocityY < 0) {
                Platform? collidePlatform = null;
                int playerColumn = Math.Clamp((int)MathF.Round(Player.X), 0, Math.Max(0, Width - 1));
                foreach (Platform platform in platforms) {
                    int platformStart = (int)MathF.Round(platform.X);
                    int platformEnd = platformStart + platform.Length - 1;
                    if (playerColumn >= platformStart && playerColumn <= platformEnd && platform.Y <= oldY && platform.Y >= newY) {
                        collidePlatform = platform;
                        break;
                    }
                }
                if (collidePlatform != null) {
                    newY = collidePlatform.Y;
                    Player.VelocityY = World.JumpVelocity;
                    manualBoostCooldown = ManualBoostCooldownSeconds;
                    LandedThisFrame = true;
                }
            }

            Player.Y = newY;
            if (Player.Y > MaxAltitude) {
                MaxAltitude = Player.Y;
            }

            int threshold = Height / 2;
            if (Player.Y > threshold) {
                Offset = (int)(Player.Y - threshold);
            }
            platforms.RemoveAll(p => p.Y < Offset);
        }

        public bool TryManualBoost() {
            if (manualBoostCooldown > 0f) return false;
            Player.VelocityY = World.JumpVelocity;
            manualBoostCooldown = ManualBoostCooldownSeconds;
            return true;
        }

        private void ResetPlayer() {
            Player ??= new Player(Width / 2, 0);
            Player.X = Width / 2f;
            Player.Y = 0f;
            Player.VelocityY = 0f;
        }

        public IEnumerable<Entity> Entities {
            get {
                yield return Player;
                foreach (Platform p in platforms) yield return p;
            }
        }
    }
}
