using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
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
            if (!SupportsInteractiveInput) {
                Diagnostics.ReportFailure("Input.Start skipped because interactive input is unavailable.");
                return;
            }
            if (listener != null) return;
            listener = new Thread(Listen) {
                IsBackground = true,
                Name = "ConsoleInputListener"
            };
            listener.Start();
        }

        public void Stop() {
            Thread? thread = listener;
            try {
                cancellation.Cancel();
            } catch (ObjectDisposedException) {
                // Already disposed, ignore.
            }
            if (thread != null && thread.IsAlive) {
                if (!thread.Join(LISTENER_SHUTDOWN_TIMEOUT_MS)) {
                    Diagnostics.ReportFailure("Input listener did not shut down within the timeout.");
                }
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
                } catch (InvalidOperationException ex) {
                    Diagnostics.ReportFailure("Console input became unavailable while listening.", ex);
                    break;
                } catch (IOException ex) {
                    Diagnostics.ReportFailure("Console input read failed.", ex);
                    break;
                } catch (SecurityException ex) {
                    Diagnostics.ReportFailure("Console input is not permitted in this environment.", ex);
                    break;
                }
            }
        }

        private static bool ProbeForConsoleInput() {
            if (Console.IsInputRedirected) return false;
            try {
                _ = Console.KeyAvailable;
                return true;
            } catch (InvalidOperationException ex) {
                Diagnostics.ReportFailure("Console input probing failed.", ex);
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
        internal const int BorderThickness = 1;
        private const char BorderCornerChar = '+';
        private const char BorderHorizontalChar = '-';
        private const char BorderVerticalChar = '|';
        private static readonly ConsoleColor BorderColor = ConsoleColor.Cyan;

        private char[] frameBuffer = Array.Empty<char>();
        private char[] paddingBuffer = Array.Empty<char>();
        private int frameWidth;
        private int frameHeight;
        private int worldRenderHeight;
        private bool frameReady;
        private int interiorLeft;
        private int interiorRight;
        private int interiorTopRow;
        private int interiorBottomRow;
        private int interiorWidth;

        public int VisibleWidth => frameWidth;
        public int VisibleHeight => frameHeight;

        public void BeginFrame(World world) {
            frameReady = false;
            int consoleWidth = ConsoleSafe.GetBufferWidth(world.Width + BorderThickness * 2);
            int consoleHeight = ConsoleSafe.GetBufferHeight(world.Height + HudRows + BorderThickness * 2);
            interiorWidth = world.Width;
            frameWidth = Math.Max(0, interiorWidth) + BorderThickness * 2;

            int availableWorldHeight = Math.Max(0, consoleHeight - HudRows - BorderThickness * 2);
            worldRenderHeight = Math.Min(world.Height, availableWorldHeight);
            frameHeight = HudRows + BorderThickness * 2 + worldRenderHeight;

            interiorLeft = BorderThickness;
            interiorRight = interiorLeft + Math.Max(0, interiorWidth - 1);
            interiorTopRow = HudRows + BorderThickness;
            interiorBottomRow = interiorTopRow + Math.Max(0, worldRenderHeight - 1);

            EnsureBufferSize();
            if (frameBuffer.Length > 0) {
                Array.Fill(frameBuffer, ' ');
            }

            DrawBorders();

            frameReady = frameWidth > 0 && frameHeight > 0;
            if (!frameReady) {
                Diagnostics.ReportFailure("Renderer.BeginFrame failed because frame dimensions were non-positive.");
            }
            if (worldRenderHeight <= 0) {
                Diagnostics.ReportFailure("Renderer.BeginFrame computed no visible world rows; only HUD will display.");
            }
        }

        public void Draw(World world) {
            if (!frameReady) {
                Diagnostics.ReportFailure("Renderer.Draw called before a frame was prepared.");
                return;
            }

            if (frameWidth <= 0 || worldRenderHeight <= 0 || interiorWidth <= 0) {
                return;
            }

            foreach (Platform platform in world.Platforms) {
                BlitEntity(platform, world);
            }

            BlitEntity(world.Player, world);
        }

        public void Present() {
            if (!frameReady) {
                Diagnostics.ReportFailure("Renderer.Present called before BeginFrame.");
                return;
            }

            if (frameWidth <= 0 || frameHeight <= 0) {
                return;
            }

            int consoleWidth = ConsoleSafe.GetBufferWidth(frameWidth);
            int consoleHeight = ConsoleSafe.GetBufferHeight(frameHeight);
            if (consoleWidth <= 0 || consoleHeight <= 0) {
                return;
            }

            int rowsToWrite = Math.Min(frameHeight, consoleHeight);
            int columnsToWrite = Math.Min(frameWidth, consoleWidth);
            if (rowsToWrite <= 0 || columnsToWrite <= 0) {
                return;
            }

            int padding = Math.Max(0, consoleWidth - columnsToWrite);
            ConsoleColor originalColor;
            try {
                originalColor = Console.ForegroundColor;
            } catch {
                originalColor = ConsoleColor.Gray;
            }

            for (int row = 0; row < rowsToWrite; row++) {
                if (!ConsoleSafe.TrySetCursorPosition(0, row)) {
                    break;
                }
                WriteRowWithBorderColor(row, columnsToWrite, originalColor);
                if (padding > 0) {
                    EnsurePaddingBuffer(padding);
                    Console.Out.Write(paddingBuffer, 0, padding);
                }
            }
            try {
                Console.ForegroundColor = originalColor;
            } catch {
                // ignore
            }

            frameReady = false;
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
            if (worldRenderHeight <= 0 || interiorWidth <= 0) {
                return;
            }

            int worldX = (int)MathF.Round(entity.X);
            if (worldX < 0 || worldX >= interiorWidth) {
                return;
            }

            int baseX = interiorLeft + worldX;
            float relativeY = entity.Y - world.Offset;
            if (relativeY < 0 || relativeY >= worldRenderHeight) {
                return;
            }

            int projectedRow = interiorTopRow + (worldRenderHeight - 1 - (int)relativeY);
            if (entity is Platform platform) {
                DrawPlatformSpan(projectedRow, baseX, platform.Length, platform.Symbol);
                return;
            }

            if (baseX < 0 || baseX >= frameWidth) {
                return;
            }

            int index = projectedRow * frameWidth + baseX;
            if ((uint)index < (uint)frameBuffer.Length) {
                frameBuffer[index] = entity.Symbol;
            }
        }

        private void DrawPlatformSpan(int row, int startX, int length, char symbol) {
            if (length <= 0) return;
            int absoluteStart = startX;
            int absoluteEnd = absoluteStart + length - 1;
            int clampedStart = Math.Max(interiorLeft, absoluteStart);
            int clampedEnd = Math.Min(interiorRight, absoluteEnd);
            if (clampedStart > clampedEnd) return;

            int rowOffset = row * frameWidth + clampedStart;
            for (int column = clampedStart; column <= clampedEnd; column++) {
                int index = rowOffset + (column - clampedStart);
                if ((uint)index < (uint)frameBuffer.Length) {
                    frameBuffer[index] = symbol;
                }
            }
        }

        private void DrawBorders() {
            if (frameBuffer.Length == 0 || frameWidth <= 0) return;

            int playfieldTop = HudRows;
            int bottomBorderStartRow = frameHeight - BorderThickness;

            for (int row = 0; row < BorderThickness; row++) {
                DrawHorizontalBorderRow(playfieldTop + row);
                DrawHorizontalBorderRow(bottomBorderStartRow + row);
            }

            for (int row = playfieldTop + BorderThickness; row < bottomBorderStartRow; row++) {
                DrawVerticalBorderColumns(row);
            }
        }

        private void DrawHorizontalBorderRow(int row) {
            if (row < 0 || row >= frameHeight) return;
            int rowBase = row * frameWidth;
            if (frameWidth <= 0) return;
            frameBuffer[rowBase] = BorderCornerChar;
            if (frameWidth == 1) return;
            for (int column = 1; column < frameWidth - 1; column++) {
                frameBuffer[rowBase + column] = BorderHorizontalChar;
            }
            frameBuffer[rowBase + frameWidth - 1] = BorderCornerChar;
        }

        private void DrawVerticalBorderColumns(int row) {
            if (row < 0 || row >= frameHeight) return;
            if (frameWidth <= 0) return;
            int leftIndex = row * frameWidth;
            frameBuffer[leftIndex] = BorderVerticalChar;
            frameBuffer[leftIndex + frameWidth - 1] = BorderVerticalChar;
        }

        private void WriteRowWithBorderColor(int row, int visibleColumns, ConsoleColor defaultColor) {
            int rowStart = row * frameWidth;
            int processed = 0;
            while (processed < visibleColumns) {
                int remaining = visibleColumns - processed;
                bool borderSegment = IsBorderChar(frameBuffer[rowStart + processed]);
                int length = 1;
                while (length < remaining && IsBorderChar(frameBuffer[rowStart + processed + length]) == borderSegment) {
                    length++;
                }
                try {
                    Console.ForegroundColor = borderSegment ? BorderColor : defaultColor;
                } catch {
                    // ignore color failures
                }
                Console.Out.Write(frameBuffer, rowStart + processed, length);
                processed += length;
            }
            try {
                Console.ForegroundColor = defaultColor;
            } catch {
                // ignore
            }
        }

        private static bool IsBorderChar(char c) =>
            c == BorderCornerChar || c == BorderHorizontalChar || c == BorderVerticalChar;
    }

    class Spawner {
        private readonly Random rand = new Random();
        private const int EarlyMinGap = 6;
        private const int EarlyMaxGap = 10;
        private const int LateMinGap = 12;
        private const int LateMaxGap = 16;
        private const float ExtraPlatformEarlyChance = 0.5f;
        private const float ExtraPlatformLateChance = 0.1f;
        private const int MaxPlatformsPerBand = 3;
        private const float BandLevelTolerance = 0.2f;

        public void Update(World world) {
            float highestY = world.Offset;
            foreach (Platform platform in world.Platforms) {
                if (platform.Y > highestY) highestY = platform.Y;
            }
            while (highestY < world.Offset + world.Height) {
                int gap = GetGap(world);
                highestY += gap;
                int platformsThisBand = GetPlatformsPerBand(world);
                SpawnPlatformsAt(world, highestY, platformsThisBand);
            }
        }

        private const int MinGapCeiling = 4; // Minimum value used as a floor when calculating the maximum allowed gap
        private const int HeightDivisorForMaxGap = 2; // Maximum gap is constrained to half the world height

        private int GetGap(World world) {
            float progress = GetProgress(world);
            int minGap = LerpInt(EarlyMinGap, LateMinGap, progress);
            int maxGap = LerpInt(EarlyMaxGap, LateMaxGap, progress);
            if (maxGap < minGap) maxGap = minGap;
            int gap = rand.Next(minGap, maxGap + 1);
            int maxAllowedGap = Math.Max(MinGapCeiling, world.Height / HeightDivisorForMaxGap);
            if (gap > maxAllowedGap) gap = maxAllowedGap;
            // The gap between platforms is clamped to avoid excessively large jumps that could make the game unplayable.
            // The maximum allowed gap is tied to the world height to ensure platform spacing scales with the play area,
            // but is never less than MinGapCeiling. The minimum gap of 1 is enforced to guarantee at least one row
            // between platforms, preventing overlap or impossible platform placement.
            return Math.Max(1, gap);
        }

        private int GetPlatformsPerBand(World world) {
            float progress = GetProgress(world);
            float chance = LerpFloat(ExtraPlatformEarlyChance, ExtraPlatformLateChance, progress);
            int count = 1;
            while (count < MaxPlatformsPerBand && rand.NextDouble() < chance) {
                count++;
                chance *= 0.5f;
            }
            return count;
        }

        private void SpawnPlatformsAt(World world, float y, int count) {
            int placed = 0;
            int safety = 0;
            while (placed < count && safety < 40) {
                safety++;
                if (TrySpawnPlatform(world, y)) {
                    placed++;
                }
            }
            if (placed == 0) {
                ForceSpawnPlatform(world, y);
            }
        }

        private bool TrySpawnPlatform(World world, float y) {
            int length = GeneratePlatformLength(world);
            int start = GetPlatformStart(world, length);
            if (BandHasOverlap(world, y, start, length)) {
                return false;
            }
            world.Platforms.Add(new Platform(start, y, length, world.Width));
            return true;
        }

        private void ForceSpawnPlatform(World world, float y) {
            int length = GeneratePlatformLength(world);
            int start = GetPlatformStart(world, length);
            world.Platforms.Add(new Platform(start, y, length, world.Width));
        }

        private int GeneratePlatformLength(World world) {
            int interiorWidth = Math.Max(1, world.Width - Renderer.BorderThickness * 2);
            int maxLength = Math.Max(World.MinPlatformLength, interiorWidth / 3);
            int minLength = World.MinPlatformLength;
            maxLength = Math.Max(minLength, maxLength);
            return rand.Next(minLength, maxLength + 1);
        }

        private bool BandHasOverlap(World world, float y, int start, int length) {
            int end = start + length - 1;
            foreach (Platform platform in world.Platforms) {
                if (Math.Abs(platform.Y - y) > BandLevelTolerance) continue;
                int existingStart = (int)MathF.Round(platform.X);
                int existingEnd = existingStart + platform.Length - 1;
                if (RangesOverlap(start, end, existingStart, existingEnd)) {
                    return true;
                }
            }
            return false;
        }

        private static bool RangesOverlap(int aStart, int aEnd, int bStart, int bEnd) =>
            aStart <= bEnd && bStart <= aEnd;

        private int GetPlatformStart(World world, int length) {
            int interiorMaxStart = Math.Max(0, world.Width - length);
            return rand.Next(interiorMaxStart + 1);
        }


        private static float GetProgress(World world) =>
            Math.Clamp(world.LevelsCompleted / (float)World.GoalPlatforms, 0f, 1f);

        private static int LerpInt(int from, int to, float t) =>
            (int)MathF.Round(from + (to - from) * t);

        private static float LerpFloat(float from, float to, float t) =>
            from + (to - from) * t;
    }

    static class ConsoleSafe {
        public static int GetBufferWidth(int fallback) {
            try {
                int width = Console.BufferWidth;
                if (width > 0) return width;
                Diagnostics.ReportWarning($"Console reported non-positive buffer width ({width}); using fallback {fallback}.");
                return fallback;
            } catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException) {
                Diagnostics.ReportFailure("Failed to read console buffer width.", ex);
                return fallback;
            }
        }

        public static int GetBufferHeight(int fallback) {
            try {
                int height = Console.BufferHeight;
                if (height > 0) return height;
                Diagnostics.ReportWarning($"Console reported non-positive buffer height ({height}); using fallback {fallback}.");
                return fallback;
            } catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException) {
                Diagnostics.ReportFailure("Failed to read console buffer height.", ex);
                return fallback;
            }
        }

        public static bool TrySetCursorPosition(int left, int top) {
            if (left < 0 || top < 0) {
                Diagnostics.ReportFailure($"Rejected cursor move to negative coordinate ({left}, {top}).");
                return false;
            }

            int width = GetBufferWidth(-1);
            if (width >= 0 && left >= width) {
                Diagnostics.ReportFailure($"Rejected cursor move beyond buffer width (left={left}, width={width}).");
                return false;
            }

            int height = GetBufferHeight(-1);
            if (height >= 0 && top >= height) {
                Diagnostics.ReportFailure($"Rejected cursor move beyond buffer height (top={top}, height={height}).");
                return false;
            }

            try {
                Console.SetCursorPosition(left, top);
                return true;
            } catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException) {
                Diagnostics.ReportFailure($"Failed to set cursor position to ({left}, {top}).", ex);
                return false;
            }
        }
    }

    static class Diagnostics {
        public static event Action<string>? FailureReported;
        public static event Action<string>? WarningReported;
        public static event Action<string>? InfoReported;

        public static void ReportFailure(string message, Exception? ex = null, [CallerMemberName] string? caller = null) {
            string prefix = BuildPrefix(message, caller);
            if (ex != null) {
                prefix = $"{prefix} ({ex.GetType().Name}: {ex.Message})";
            }
            FailureReported?.Invoke(prefix);
        }

        public static void ReportWarning(string message, [CallerMemberName] string? caller = null) {
            string prefix = BuildPrefix(message, caller);
            WarningReported?.Invoke(prefix);
        }

        public static void ReportInfo(string message, [CallerMemberName] string? caller = null) {
            string prefix = BuildPrefix(message, caller);
            InfoReported?.Invoke(prefix);
        }

        private static string BuildPrefix(string message, string? caller) =>
            caller != null ? $"{caller}: {message}" : message;
    }
}
