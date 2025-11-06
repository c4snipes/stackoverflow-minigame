using System;
using System.Collections.Generic;
using System.Threading;

namespace stackoverflow_minigame {
    enum GameState { Menu, Running, Over }

    class Game {
        private GameState state = GameState.Menu;
        private bool running = true;
        private World world;
        private Renderer renderer;
        private Input input;
        private Spawner spawner;
        private int score = 0;
        private const int FrameDelay = 50;
        internal const float GravityPerSecond = -8f; // gravity units per second
        internal const float FrameTimeSeconds = FrameDelay / 1000f; // seconds per frame
        internal const int GravityAcceleration = -1;
        public Game() {
            world = new World(80, 25);
            renderer = new Renderer();
            input = new Input();
            spawner = new Spawner();
        }

        public void Run() {
            Console.CursorVisible = false;
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
            Console.CursorVisible = true;
        }

        private void MenuLoop() {
            Console.Clear();
            Console.SetCursorPosition(0, 0);
            Console.WriteLine("STACKOVERFLOW JUMP GAME");
            Console.WriteLine("Use Left/Right arrows to move");
            Console.WriteLine("Press S to Start or Q to Quit");
            while (state == GameState.Menu) {
                if (Console.KeyAvailable) {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q) {
                        running = false;
                        break;
                    }
                    if (key == ConsoleKey.S || key == ConsoleKey.Enter || key == ConsoleKey.Spacebar) {
                        state = GameState.Running;
                        world.Reset();
                        score = 0;
                        world.Player.VelocityY = World.JumpVelocity;
                        break;
                    }
                }
                Thread.Sleep(10);
            }
        }

        private void GameLoop() {
            while (state == GameState.Running) {
                if (Console.KeyAvailable) {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.LeftArrow || key == ConsoleKey.A) {
                        world.Player.X -= 1;
                    } else if (key == ConsoleKey.RightArrow || key == ConsoleKey.D) {
                        world.Player.X += 1;
                    } else if (key == ConsoleKey.Q) {
                        running = false;
                        return;
                    }
                }
                if (world.Player.X < 0) world.Player.X = 0;
                if (world.Player.X >= world.Width) world.Player.X = world.Width - 1;

                world.Update();
                score++;
                if (world.Player.Y < world.Offset) {
                    state = GameState.Over;
                }
                if (state == GameState.Running) {
                    spawner.Update(world);
                }

                renderer.Clear();
                renderer.Draw(world);
                Console.SetCursorPosition(0, 0);
                Console.Write($"Score: {score}");
                renderer.Present();

                Thread.Sleep(FrameDelay);
            }
        }

        private void OverLoop() {
            Console.Clear();
            Console.WriteLine("GAME OVER!");
            Console.WriteLine($"Final Score: {score}");
            Console.WriteLine("Press R to Restart or Q to Quit");
            while (state == GameState.Over) {
                if (Console.KeyAvailable) {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q) {
                        running = false;
                        break;
                    }
                    if (key == ConsoleKey.R) {
                        state = GameState.Menu;
                        break;
                    }
                }
                Thread.Sleep(10);
            }
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

        public World(int width, int height) {
            Width = width;
            Height = height;
            platforms = new List<Platform>();
            rand = new Random();
            Reset();
        }

        public void Reset() {
            Player = new Player(Width / 2, 0);
            Player.VelocityY = 0f;
            platforms.Clear();
            Offset = 0;
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
