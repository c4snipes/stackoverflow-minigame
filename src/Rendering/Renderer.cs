using System;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Double-buffered ASCII renderer with adaptive sizing for constrained terminals.
    /// </summary>
    internal class Renderer
    {
        public const int HudRows = 3;
        internal const int BorderThickness = 1;
        private const char BorderCornerChar = '+';
        private const char BorderHorizontalChar = '-';
        private const char BorderVerticalChar = '|';
        private static readonly ConsoleColor BorderColor = ConsoleColor.Cyan;

        // Off-screen buffer to avoid flicker during rendering
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

        /// <summary>
        /// Prepares the off-screen buffer for rendering, adapting to current console dimensions.
        /// </summary>
        public void BeginFrame(World world)
        {
            frameReady = false;
            int consoleWidth = ConsoleSafe.GetBufferWidth(world.Width + BorderThickness * 2);
            int consoleHeight = ConsoleSafe.GetBufferHeight(world.Height + HudRows + BorderThickness * 2);
            interiorWidth = world.Width;
            frameWidth = Math.Max(0, interiorWidth) + BorderThickness * 2;

            int availableWorldHeight = Math.Max(0, consoleHeight - HudRows - BorderThickness * 2);
            worldRenderHeight = Math.Max(0, Math.Min(world.Height, availableWorldHeight));
            frameHeight = HudRows + BorderThickness * 2 + worldRenderHeight;

            interiorLeft = BorderThickness;
            interiorRight = interiorLeft + Math.Max(0, interiorWidth - 1);
            interiorTopRow = HudRows + BorderThickness;
            interiorBottomRow = interiorTopRow + Math.Max(0, worldRenderHeight - 1);

            EnsureBufferSize();
            if (frameBuffer.Length > 0)
            {
                Array.Fill(frameBuffer, ' ');
            }

            if (frameWidth > 0 && frameHeight > 0)
            {
                DrawBorders();
            }

            frameReady = frameWidth > 0 && frameHeight > 0;
            if (!frameReady)
            {
                Diagnostics.ReportFailure("Renderer.BeginFrame failed because frame dimensions were non-positive.");
            }
            if (worldRenderHeight <= 0)
            {
                Diagnostics.ReportFailure("Renderer.BeginFrame computed no visible world rows; only HUD will display.");
            }
        }

        /// <summary>
        /// Renders world entities into the off-screen buffer.
        /// </summary>
        public void Draw(World world)
        {
            if (!frameReady)
            {
                Diagnostics.ReportFailure("Renderer.Draw called before a frame was prepared.");
                return;
            }

            if (frameWidth <= 0 || worldRenderHeight <= 0 || interiorWidth <= 0)
            {
                return;
            }

            foreach (Platform platform in world.Platforms)
            {
                BlitEntity(platform, world);
            }

            BlitEntity(world.Player, world);
        }

        /// <summary>
        /// Flushes the off-screen buffer to console, respecting dimensions and color boundaries.
        /// </summary>
        public void Present()
        {
            if (!frameReady)
            {
                Diagnostics.ReportFailure("Renderer.Present called before BeginFrame.");
                return;
            }

            if (frameWidth <= 0 || frameHeight <= 0)
            {
                return;
            }

            int consoleWidth = ConsoleSafe.GetBufferWidth(frameWidth);
            int consoleHeight = ConsoleSafe.GetBufferHeight(frameHeight);
            if (consoleWidth <= 0 || consoleHeight <= 0)
            {
                return;
            }

            int rowsToWrite = Math.Min(frameHeight, consoleHeight);
            int columnsToWrite = Math.Min(frameWidth, consoleWidth);
            if (rowsToWrite <= 0 || columnsToWrite <= 0)
            {
                return;
            }
            // Calculate padding for any extra console width beyond the frame width.
            int padding = Math.Max(0, consoleWidth - columnsToWrite);
            ConsoleColor originalColor;
            try
            {
                originalColor = Console.ForegroundColor;
            }
            catch
            {
                originalColor = ConsoleColor.Gray;
            }

            for (int row = 0; row < rowsToWrite; row++)
            {
                if (!ConsoleSafe.TrySetCursorPosition(0, row))
                {
                    break;
                }
                WriteRowWithBorderColor(row, columnsToWrite, originalColor);
                if (padding > 0)
                {
                    EnsurePaddingBuffer(padding);
                    try
                    {
                        Console.Out.Write(paddingBuffer, 0, padding);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.ReportFailure("Failed to write padding buffer.", ex);
                        break;
                    }
                }
            }
            try
            {
                Console.ForegroundColor = originalColor;
            }
            catch
            {
                // ignore
            }

            frameReady = false;
        }

        private void EnsureBufferSize()
        {
            int required = frameWidth * frameHeight;
            if (frameBuffer.Length != required)
            {
                frameBuffer = required > 0 ? new char[required] : Array.Empty<char>();
            }
        }

        private void EnsurePaddingBuffer(int width)
        {
            if (paddingBuffer.Length < width)
            {
                paddingBuffer = new char[width];
                Array.Fill(paddingBuffer, ' ');
            }
        }

        private void BlitEntity(Entity entity, World world)
        {
            if (worldRenderHeight <= 0 || interiorWidth <= 0)
            {
                return;
            }

            int worldX = (int)MathF.Round(entity.X);
            if (worldX < 0 || worldX >= interiorWidth)
            {
                return;
            }

            int baseX = interiorLeft + worldX;
            float relativeY = entity.Y - world.Offset;
            if (relativeY < 0 || relativeY >= worldRenderHeight)
            {
                return;
            }

            int projectedRow = interiorTopRow + (worldRenderHeight - 1 - (int)relativeY);
            if (entity is Platform platform)
            {
                DrawPlatformSpan(projectedRow, baseX, platform.Length, platform.Symbol);
                return;
            }

            if (baseX < 0 || baseX >= frameWidth)
            {
                return;
            }

            int index = projectedRow * frameWidth + baseX;
            if ((uint)index < (uint)frameBuffer.Length)
            {
                frameBuffer[index] = entity.Symbol;
            }
        }

        private void DrawPlatformSpan(int row, int startX, int length, char symbol)
        {
            if (length <= 0)
            {
                return;
            }

            int absoluteStart = startX;
            int absoluteEnd = absoluteStart + length - 1;
            int clampedStart = Math.Max(interiorLeft, absoluteStart);
            int clampedEnd = Math.Min(interiorRight, absoluteEnd);
            if (clampedStart > clampedEnd)
            {
                return;
            }

            int rowOffset = row * frameWidth + clampedStart;
            for (int column = clampedStart; column <= clampedEnd; column++)
            {
                int index = rowOffset + (column - clampedStart);
                if ((uint)index < (uint)frameBuffer.Length)
                {
                    frameBuffer[index] = symbol;
                }
            }
        }

        private void DrawBorders()
        {
            if (frameBuffer.Length == 0 || frameWidth <= 0 || frameHeight <= 0)
            {
                return;
            }

            int playfieldTop = HudRows;
            int bottomBorderStartRow = frameHeight - BorderThickness;

            for (int row = 0; row < BorderThickness; row++)
            {
                DrawHorizontalBorderRow(playfieldTop + row);
                DrawHorizontalBorderRow(bottomBorderStartRow + row);
            }

            for (int row = playfieldTop + BorderThickness; row < bottomBorderStartRow; row++)
            {
                DrawVerticalBorderColumns(row);
            }
        }

        private void DrawHorizontalBorderRow(int row)
        {
            if (row < 0 || row >= frameHeight)
            {
                return;
            }

            int rowBase = row * frameWidth;
            if (frameWidth <= 0)
            {
                return;
            }

            frameBuffer[rowBase] = BorderCornerChar;
            if (frameWidth == 1)
            {
                return;
            }

            for (int column = 1; column < frameWidth - 1; column++)
            {
                frameBuffer[rowBase + column] = BorderHorizontalChar;
            }
            frameBuffer[rowBase + frameWidth - 1] = BorderCornerChar;
        }

        private void DrawVerticalBorderColumns(int row)
        {
            if (row < 0 || row >= frameHeight)
            {
                return;
            }

            if (frameWidth <= 0)
            {
                return;
            }

            int leftIndex = row * frameWidth;
            frameBuffer[leftIndex] = BorderVerticalChar;
            frameBuffer[leftIndex + frameWidth - 1] = BorderVerticalChar;
        }

        private void WriteRowWithBorderColor(int row, int visibleColumns, ConsoleColor defaultColor)
        {
            int rowStart = row * frameWidth;
            int processed = 0;
            while (processed < visibleColumns)
            {
                int remaining = visibleColumns - processed;
                bool borderSegment = IsBorderChar(frameBuffer[rowStart + processed]);
                int length = 1;
                while (length < remaining && IsBorderChar(frameBuffer[rowStart + processed + length]) == borderSegment)
                {
                    length++;
                }
                try
                {
                    Console.ForegroundColor = borderSegment ? BorderColor : defaultColor;
                }
                catch
                {
                    // ignore color failures
                }
                try
                {
                    Console.Out.Write(frameBuffer, rowStart + processed, length);
                }
                catch (Exception ex)
                {
                    Diagnostics.ReportFailure("Failed to write frame buffer to console.", ex);
                    break;
                }
                processed += length;
            }
            try
            {
                Console.ForegroundColor = defaultColor;
            }
            catch
            {
                // ignore
            }
        }

        private static bool IsBorderChar(char c) =>
            c == BorderCornerChar || c == BorderHorizontalChar || c == BorderVerticalChar;
    }
}
