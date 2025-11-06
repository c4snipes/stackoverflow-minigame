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
        public const int HudRows = 3;

        private char[] frameBuffer = Array.Empty<char>();
        private char[] paddingBuffer = Array.Empty<char>();
        private int frameWidth;
        private int frameHeight;
        private int worldRenderHeight;

        public int VisibleWidth => frameWidth;

        public void BeginFrame(World world) {
            int consoleWidth = Console.BufferWidth > 0 ? Console.BufferWidth : world.Width;
            int consoleHeight = Console.BufferHeight > 0 ? Console.BufferHeight : world.Height + HudRows;

            frameWidth = Math.Min(world.Width, consoleWidth);
            int availableWorldHeight = Math.Max(0, consoleHeight - HudRows);
            worldRenderHeight = Math.Min(world.Height, availableWorldHeight);
            frameHeight = HudRows + worldRenderHeight;

            EnsureBufferSize();
            Array.Fill(frameBuffer, ' ');
        }

        public void Draw(World world) {
            if (frameWidth <= 0 || worldRenderHeight <= 0) {
                return;
            }

            foreach (Platform platform in world.Platforms) {
                BlitEntity(platform, world);
            }

            BlitEntity(world.Player, world);
        }

        public void Present() {
            if (frameWidth <= 0 || frameHeight <= 0) {
                return;
            }

            int consoleWidth = Console.BufferWidth > 0 ? Console.BufferWidth : frameWidth;
            int consoleHeight = Console.BufferHeight > 0 ? Console.BufferHeight : frameHeight;
            int rowsToWrite = Math.Min(frameHeight, consoleHeight);
            int columnsToWrite = Math.Min(frameWidth, consoleWidth);
            int padding = Math.Max(0, consoleWidth - columnsToWrite);

            for (int row = 0; row < rowsToWrite; row++) {
                if (row >= Console.BufferHeight) {
                    break;
                }
                Console.SetCursorPosition(0, row);
                Console.Out.Write(frameBuffer, row * frameWidth, columnsToWrite);
                if (padding > 0) {
                    EnsurePaddingBuffer(padding);
                    Console.Out.Write(paddingBuffer, 0, padding);
                }
            }
        }

        private void EnsureBufferSize() {
            int required = frameWidth * frameHeight;
            if (frameBuffer.Length != required) {
                frameBuffer = required > 0 ? new char[required] : Array.Empty<char>();
            }
        }

        private void EnsurePaddingBuffer(int width) {
            if (paddingBuffer.Length < width) {
                paddingBuffer = new char[width];
                Array.Fill(paddingBuffer, ' ');
            }
        }

        private void BlitEntity(Entity entity, World world) {
            int x = entity.X;
            if (x < 0 || x >= frameWidth) {
                return;
            }

            float relativeY = entity.Y - world.Offset;
            if (relativeY < 0 || relativeY >= worldRenderHeight) {
                return;
            }

            int projectedRow = HudRows + (worldRenderHeight - 1 - (int)relativeY);
            int index = projectedRow * frameWidth + x;
            if ((uint)index < (uint)frameBuffer.Length) {
                frameBuffer[index] = entity.Symbol;
            }
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
                int gap = rand.Next(MinPlatformGap, MaxPlatformGap + 1);
                highestY += gap;
                int newX = rand.Next(world.Width);
                world.Platforms.Add(new Platform(newX, highestY));
            }
        }
    }
}
