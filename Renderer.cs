using System;

namespace stackoverflow_minigame
{
    class Renderer
    {
        // Number of rows reserved for HUD display at the top of the frame.
        public const int HudRows = 3;
        // Thickness of the border around the playfield.
        internal const int BorderThickness = 1;
        private const char BorderCornerChar = '+';
        // Characters used for drawing the borders.
        private const char BorderHorizontalChar = '-';
        // Characters used for drawing the borders.
        private const char BorderVerticalChar = '|';
        // Color used for rendering the borders.
        private static readonly ConsoleColor BorderColor = ConsoleColor.Cyan;
        // Off-screen buffer for the current frame.
        // This buffer holds the characters to be rendered to the console.
        private char[] frameBuffer = Array.Empty<char>();
        private char[] paddingBuffer = Array.Empty<char>();
        private int frameWidth;
        private int frameHeight;
        // Height of the world area being rendered (excluding HUD and borders).
        private int worldRenderHeight;
        // Indicates whether the frame is ready to be presented.
        private bool frameReady;
        // Leftmost column of the interior playfield area.
        private int interiorLeft;
        // Rightmost column of the interior playfield area.
        private int interiorRight;
        // Top row of the interior playfield area.
        private int interiorTopRow;
        // Bottom row of the interior playfield area.
        private int interiorBottomRow;
        // Width of the interior playfield area (excluding borders).
        private int interiorWidth;
        // Gets the visible width of the current frame.

        public int VisibleWidth => frameWidth;
        public int VisibleHeight => frameHeight;

        // Prepares the off-screen buffer for the next frame, deriving console-safe dimensions and drawing the border scaffold.
        public void BeginFrame(World world)
        {
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
            // Allocate or resize the frame buffer as needed.
            // Clear the buffer to spaces.
            // This ensures a clean slate for rendering the new frame.
            EnsureBufferSize();
            if (frameBuffer.Length > 0)
            {
                Array.Fill(frameBuffer, ' ');
            }

            DrawBorders();

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
        // Renders the world entities into the off-screen buffer.
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

        // Flushes the prepared buffer to the console, chunking writes to honor current buffer width/height and color boundaries.
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
                    Console.Out.Write(paddingBuffer, 0, padding);
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
// Updates the world state for the next frame, including player position and platform lifecycle management.
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
// Draws a horizontal span of a platform at the specified row and starting X position.
        private void DrawPlatformSpan(int row, int startX, int length, char symbol)
        {
            if (length <= 0) return;
            int absoluteStart = startX;
            int absoluteEnd = absoluteStart + length - 1;
            int clampedStart = Math.Max(interiorLeft, absoluteStart);
            int clampedEnd = Math.Min(interiorRight, absoluteEnd);
            if (clampedStart > clampedEnd) return;

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
        // Draws the borders around the playfield area.
        private void DrawBorders()
        {
            if (frameBuffer.Length == 0 || frameWidth <= 0) return;

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
            if (row < 0 || row >= frameHeight) return;
            int rowBase = row * frameWidth;
            if (frameWidth <= 0) return;
            frameBuffer[rowBase] = BorderCornerChar;
            if (frameWidth == 1) return;
            for (int column = 1; column < frameWidth - 1; column++)
            {
                frameBuffer[rowBase + column] = BorderHorizontalChar;
            }
            frameBuffer[rowBase + frameWidth - 1] = BorderCornerChar;
        }
// Draws the vertical border columns for a specific row.
        private void DrawVerticalBorderColumns(int row)
        {
            if (row < 0 || row >= frameHeight) return;
            if (frameWidth <= 0) return;
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
                Console.Out.Write(frameBuffer, rowStart + processed, length);
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
