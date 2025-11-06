using System;
using System.Collections.Generic;
using System.Threading;

namespace stackoverflow_minigame {
    enum GameState { Menu, Running, Over }

    class Game {
        private GameState state = GameState.Menu;
        private bool running = true;
        private World world;
        private readonly Renderer renderer;
        private readonly Input input;
        private readonly Spawner spawner;
        private int score = 0;
        private bool playerWon = false;
        private const int FrameDelay = 50;
        internal const float GravityPerSecond = -8f; // gravity units per second
        internal const float FrameTimeSeconds = FrameDelay / 1000f; // seconds per frame
        internal const int GravityAcceleration = -1;
        private const int MIN_PROGRESS_BAR_WIDTH = 10;
        private const int MAX_PROGRESS_BAR_WIDTH = 60;

        public Game() {
            world = new World(80, 25);
            renderer = new Renderer();
            input = new Input();
            spawner = new Spawner();
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
            Console.SetCursorPosition(0, 0);
            Console.WriteLine("STACKOVERFLOW SKY CLIMBER");
            Console.WriteLine("Bounce between green checks to climb the question stack.");
            Console.WriteLine("Press S to start or Q to quit.");

            if (!input.SupportsInteractiveInput) {
                Console.WriteLine();
                Console.WriteLine("Interactive console input isn't available in this environment.");
                Console.WriteLine("Run this game in a terminal window to take it for a spin!");
                Thread.Sleep(2000);
                running = false;
                return;
            }

            input.ClearBuffer();

            while (state == GameState.Menu && running) {
                if (input.TryReadKey(out var key)) {
                    if (key.Key == ConsoleKey.Q) {
                        running = false;
                        return;
                    }
                    if (key.Key == ConsoleKey.S || key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar) {
                        StartRun();
                        return;
                    }
                }
                Thread.Sleep(10);
            }
        }

        private void StartRun() {
            state = GameState.Running;
            score = 0;
            playerWon = false;
            world.Reset();
            spawner.Update(world); // seed top half so the first few frames have platforms
            world.Player.VelocityY = World.JumpVelocity;
            input.ClearBuffer();
        }

        private void GameLoop() {
            while (state == GameState.Running && running) {
                ProcessGameplayInput();
                if (!running || state != GameState.Running) break;

                ClampPlayerWithinBounds();

                world.Update();
                spawner.Update(world);
                score = Math.Max(score, (int)MathF.Round(world.MaxAltitude));

                if (world.Player.Y < world.Offset) {
                    state = GameState.Over;
                    playerWon = false;
                } else if (world.MaxAltitude >= World.GoalHeight) {
                    state = GameState.Over;
                    playerWon = true;
                }

                renderer.Clear();
                renderer.Draw(world);
                DrawHud();
                renderer.Present();

                Thread.Sleep(FrameDelay);
            }
        }

        private void OverLoop() {
            Console.Clear();
            Console.WriteLine(playerWon ? "YOU CLEARED THE ERROR STACK!" : "GAME OVER!");
            Console.WriteLine($"Final Height Score: {score}");
            Console.WriteLine("Press R to restart or Q to quit.");
            input.ClearBuffer();

            while (state == GameState.Over && running) {
                if (input.TryReadKey(out var key)) {
                    if (key.Key == ConsoleKey.Q) {
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

        private void ProcessGameplayInput() {
            while (input.TryReadKey(out var key)) {
                switch (key.Key) {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.A:
                        world.Player.X -= 1;
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.D:
                        world.Player.X += 1;
                        break;
                    case ConsoleKey.Q:
                        running = false;
                        return;
                }
            }
        }

        private void ClampPlayerWithinBounds() {
            if (world.Player.X < 0) world.Player.X = 0;
            if (world.Player.X >= world.Width) world.Player.X = world.Width - 1;
        }

            WriteHudLine(0, $"Score: {score,4} | Height: {GetRoundedAltitude(world.Player.Y),4} | Max: {GetRoundedAltitude(world.MaxAltitude),4}", hudWidth);
            int hudWidth = world.Width;
            WriteHudLine(0, $"Score: {score,4} | Height: {(int)MathF.Round(world.Player.Y),4} | Max: {(int)MathF.Round(world.MaxAltitude),4}", hudWidth);
            float progress = Math.Clamp(world.MaxAltitude / World.GoalHeight, 0f, 1f);
            WriteHudLine(1, $"Progress: {BuildProgressBar(progress, 30)} {progress * 100f:0}% of goal {World.GoalHeight:0}", hudWidth);
            WriteHudLine(2, "Controls: A/D or <-/-> move | Q quits", hudWidth);
        }

        private static void WriteHudLine(int row, string text, int width) {
            Console.SetCursorPosition(0, row);
            string output = text.Length > width ? text[..width] : text.PadRight(width);
            Console.Write(output);
        }

        /// <summary>
        /// Builds a progress bar string with the given progress and width.
        /// Width is clamped between MIN_PROGRESS_BAR_WIDTH and MAX_PROGRESS_BAR_WIDTH.
        /// </summary>
        private static string BuildProgressBar(float progress, int width) {
            int clampedWidth = Math.Clamp(width, MIN_PROGRESS_BAR_WIDTH, MAX_PROGRESS_BAR_WIDTH);
            int filled = (int)MathF.Round(progress * clampedWidth);
            if (filled > clampedWidth) filled = clampedWidth;
            string bar = new string('#', filled).PadRight(clampedWidth, '.');
            return $"[{bar}]";
        }
    }

    class World {
        public int Width { get; }
        public int Height { get; }
        public int Offset { get; private set; }
        public Player Player { get; private set; }
        private List<Platform> platforms;
        private Random rand;
        internal List<Platform> Platforms { get { return platforms; } }
        internal const int JumpVelocity = 4;
        public float MaxAltitude { get; private set; }
        public const float GoalHeight = 250f;

        public World(int width, int height) {
            Width = width;
            Height = height;
            platforms = new List<Platform>();
            rand = new Random();
            Player = new Player(width / 2, 0);
            Reset();
        }

        public void Reset() {
            Player = new Player(Width / 2, 0);
            Player.VelocityY = 0f;
            platforms.Clear();
            Offset = 0;
            MaxAltitude = 0f;
            // initial platforms
            int firstY = 8;
            platforms.Add(new Platform(rand.Next(Width), firstY));
            int secondY = firstY + 7;
            platforms.Add(new Platform(rand.Next(Width), secondY));
        }

        public void Update() {
            Player.VelocityY += Game.GravityPerSecond * Game.FrameTimeSeconds;
            float oldY = Player.Y;
            float newY = oldY + Player.VelocityY;
            if (Player.VelocityY < 0) {
                // check for platform collisions while falling
                Platform? collidePlatform = null;
                foreach (Platform platform in platforms) {
                    if (platform.X == Player.X && platform.Y <= oldY && platform.Y >= newY) {
                        collidePlatform = platform;
                        break;
                    }
                }
                if (collidePlatform != null) {
                    newY = collidePlatform.Y;
                    Player.VelocityY = World.JumpVelocity;
                }
            }
            Player.Y = newY;
            if (Player.Y > MaxAltitude) {
                MaxAltitude = Player.Y;
            }

            // Update camera offset if player has climbed above threshold
            int threshold = Height / 2;
            if (Player.Y > threshold) {
                Offset = (int)(Player.Y - threshold);
            }
            // Remove platforms that fell below view
            platforms.RemoveAll(p => p.Y < Offset);
        }

        public IEnumerable<Entity> Entities {
            get {
                yield return Player;
                foreach (Platform p in platforms) yield return p;
            }
        }
    }
}
