using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace stackoverflow_minigame {
    class Input : IDisposable {
        private readonly ConcurrentQueue<ConsoleKeyInfo> buffer = new();
        private readonly CancellationTokenSource cancellation = new();
        private Thread? listener;
        private const int LISTENER_SHUTDOWN_TIMEOUT_MS = 1000;

        public bool SupportsInteractiveInput { get; }

        public Input() {
            SupportsInteractiveInput = ProbeForConsoleInput();
        }

        public void Start() {
            if (!SupportsInteractiveInput || listener != null) return;
            listener = new Thread(Listen) {
                IsBackground = true,
                Name = "ConsoleInputListener"
            };
            listener.Start();
        }

        public void Stop() {
            try {
                cancellation.Cancel();
            } catch (ObjectDisposedException) {
                // Already disposed, ignore.
            }
            if (listener != null && listener.IsAlive) {
                // Attempt graceful shutdown, but if the thread is blocked on Console.ReadKey(), it may not exit promptly.
                // Since the thread is a background thread, it will be terminated when the process exits.
                // Removed listener.Join() to avoid unnecessary delay; thread will exit when process ends.
                // If still alive, accept that it may not terminate gracefully due to Console.ReadKey() being blocking.
            }
            listener = null;
        }

        public bool TryReadKey(out ConsoleKeyInfo key) => buffer.TryDequeue(out key);

        public void ClearBuffer() {
            while (buffer.TryDequeue(out _)) { }
        }

        private void Listen() {
            while (!cancellation.IsCancellationRequested) {
                try {
                    if (Console.KeyAvailable) {
                        var key = Console.ReadKey(intercept: true);
                        buffer.Enqueue(key);
                    } else {
                        Thread.Sleep(2);
                    }
                } catch (InvalidOperationException) {
                    // Console input became unavailable; exit listener.
                    break;
                }
            }
        }

        private static bool ProbeForConsoleInput() {
            if (Console.IsInputRedirected) return false;
            try {
                _ = Console.KeyAvailable;
                return true;
            } catch (InvalidOperationException) {
                return false;
            }
        }

        public void Dispose() {
            Stop();
            cancellation.Dispose();
        }
    }

    class Renderer {
        public void Clear() {
            Console.Clear();
        }

        public void Draw(World world) {
            // Draw platforms first
            foreach (Entity entity in world.Entities) {
                if (entity is Player) continue;
                int screenY = (int)(world.Height - 1 - (entity.Y - world.Offset));
                if (screenY >= 0 && screenY < world.Height) {
                    if (entity.X >= 0 && entity.X < world.Width) {
                        Console.SetCursorPosition(entity.X, screenY);
                        Console.Write(entity.Symbol);
                    }
                }
            }
            // Draw player last (on top of platforms)
            Entity player = world.Player;
            int playerScreenY = world.Height - 1 - (int)(player.Y - world.Offset);
            if (playerScreenY >= 0 && playerScreenY < world.Height) {
                if (player.X >= 0 && player.X < world.Width) {
                    Console.SetCursorPosition(player.X, playerScreenY);
                    Console.Write(player.Symbol);
                }
            }
        }

        public void Present() {
            // In console, drawing is immediate, so no additional buffering needed.
            // This method is included for compatibility.
        }
    }

    class Spawner {
        private Random rand = new Random();
        private const int MinPlatformGap = 5;
        private const int MaxPlatformGap = 10;

        public void Update(World world) {
            int highestY = world.Offset;
            foreach (Platform platform in world.Platforms) {
                if (platform.Y > highestY) highestY = (int)platform.Y;
            }
            while (highestY < world.Offset + world.Height) {
                int gap = rand.Next(MinPlatformGap, MaxPlatformGap);
                highestY += gap;
                int newX = rand.Next(world.Width);
                world.Platforms.Add(new Platform(newX, highestY));
            }
        }
    }
}
