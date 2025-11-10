using System;
using System.Collections.Generic;
using System.Security;
using System.Text;
using System.Threading;

namespace stackoverflow_minigame
{
    sealed class InitialsPrompt
    {
        private readonly Input input;
        private readonly int maxChars;
        private static readonly Dictionary<char, string[]> GlyphCache = new();
        [ThreadStatic] private static StringBuilder? GlyphBuilder;

        internal readonly record struct Callbacks(
            Action? PromptStarted,
            Action<char>? CharAccepted,
            Action<char>? CharRejected,
            Action<string>? InitialsCommitted,
            Action? InitialsCanceled,
            Action<string>? FallbackUsed
        );

        public InitialsPrompt(Input input, int maxChars = 3)
        {
            this.input = input;
            this.maxChars = maxChars;
        }

        // Drives the interactive arcade-style prompt, blocking until initials are committed, cancelled, or console input fails.
        public bool TryCapture(out string initials, string fallback, Callbacks callbacks)
        {
            using var pause = input.PauseListening();
            input.ClearBuffer();

            initials = fallback;
            string current = string.Empty;

            Console.Clear();
            callbacks.PromptStarted?.Invoke();
            WriteArcadePrompt(out int artTopRow, out int promptRow);
            bool lastBlink = false;
            string lastRendered = string.Empty;
            bool lastRenderSucceeded = false;

            void InvalidateRender()
            {
                lastRendered = string.Empty;
                lastBlink = false;
                lastRenderSucceeded = false;
            }

            void RefreshPrompt(bool forceBlink)
            {
                bool effectiveBlink = forceBlink || (Environment.TickCount / 500 % 2) == 0;
                if (lastRenderSucceeded && lastBlink == effectiveBlink && lastRendered == current) return;
                WriteInitialsLine(current, maxChars, promptRow, effectiveBlink);
                lastRenderSucceeded = TryRenderInitialsArt(current, maxChars, artTopRow, effectiveBlink);
                lastBlink = effectiveBlink;
                lastRendered = current;
            }

            while (true)
            {
                RefreshPrompt(forceBlink: false);

                ConsoleKeyInfo keyInfo;
                try
                {
                    if (!Console.KeyAvailable)
                    {
                        Thread.Sleep(50);
                        continue;
                    }
                    keyInfo = Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                    callbacks.FallbackUsed?.Invoke(fallback);
                    initials = fallback;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or SecurityException)
                {
                    Diagnostics.ReportFailure("Initials prompt failed to read input; using fallback initials.", ex);
                    callbacks.FallbackUsed?.Invoke(fallback);
                    initials = fallback;
                    return true;
                }

                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    callbacks.InitialsCanceled?.Invoke();
                    return false;
                }

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    if (current.Length == 0) continue;
                    while (current.Length < maxChars) current += '_';
                    initials = current.ToUpperInvariant();
                    callbacks.InitialsCommitted?.Invoke(initials);
                    InvalidateRender();
                    RefreshPrompt(forceBlink: true);
                    return true;
                }

                if (keyInfo.Key == ConsoleKey.Backspace && current.Length > 0)
                {
                    current = current[..^1];
                    continue;
                }

                if (!TryNormalizeInitialChar(keyInfo, out char ch))
                {
                    callbacks.CharRejected?.Invoke(keyInfo.KeyChar);
                    continue;
                }
                if (current.Length >= maxChars)
                {
                    callbacks.CharRejected?.Invoke(ch);
                    continue;
                }

                current += ch;
                callbacks.CharAccepted?.Invoke(ch);
                InvalidateRender();
            }
        }

        private static void WriteArcadePrompt(out int artTopRow, out int promptRow)
        {
            ConsoleColor originalColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("========================================");
                Console.WriteLine("          STACKOVERFLOW SKY            ");
                Console.WriteLine("========================================");
                Console.WriteLine();
                Console.WriteLine("ENTER YOUR INITIALS");
                Console.WriteLine("UPPERCASE LETTERS & NUMBERS ONLY (3 CHARACTERS, ESC TO CANCEL)");
                Console.WriteLine();
                artTopRow = Console.CursorTop;
                for (int i = 0; i < GlyphLibrary.GlyphHeight; i++)
                {
                    Console.WriteLine();
                }
                Console.WriteLine();
                promptRow = Console.CursorTop;
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        private static bool TryRenderInitialsArt(string current, int maxChars, int topRow, bool blinkOn)
        {
            if (current.Length > maxChars)
            {
                current = current[..maxChars];
            }

            string padded = current.PadRight(maxChars, blinkOn ? '_' : ' ');
            string[] composedRows = BuildGlyphRows(padded);
            try
            {
                for (int row = 0; row < GlyphLibrary.GlyphHeight; row++)
                {
                    if (!ConsoleSafe.TrySetCursorPosition(0, topRow + row))
                    {
                        continue;
                    }
                    string line = composedRows[row];
                    int width = Console.BufferWidth > 0 ? Console.BufferWidth - 1 : line.Length;
                    if (line.Length < width) line = line.PadRight(width);
                    Console.Write(line.Length > width ? line[..width] : line);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                Diagnostics.ReportFailure("Failed to render initials art.", ex);
                return false;
            }
        }

        private static void WriteInitialsLine(string current, int maxChars, int row, bool blinkOn)
        {
            string display = current.PadRight(maxChars, blinkOn ? '_' : ' ');
            if (!ConsoleSafe.TrySetCursorPosition(0, row)) return;
            Console.Write(new string(' ', Math.Max(0, Console.BufferWidth - 1)));
            if (!ConsoleSafe.TrySetCursorPosition(0, row)) return;
            Console.Write($"INITIALS: {display}");
        }

        private static bool TryNormalizeInitialChar(ConsoleKeyInfo keyInfo, out char normalized)
        {
            char raw = keyInfo.KeyChar;
            if (!char.IsControl(raw) && char.IsLetterOrDigit(raw))
            {
                normalized = char.ToUpperInvariant(raw);
                return true;
            }

            if (keyInfo.Key >= ConsoleKey.A && keyInfo.Key <= ConsoleKey.Z)
            {
                normalized = (char)('A' + (keyInfo.Key - ConsoleKey.A));
                return true;
            }

            if (keyInfo.Key >= ConsoleKey.D0 && keyInfo.Key <= ConsoleKey.D9)
            {
                normalized = (char)('0' + (keyInfo.Key - ConsoleKey.D0));
                return true;
            }

            if (keyInfo.Key >= ConsoleKey.NumPad0 && keyInfo.Key <= ConsoleKey.NumPad9)
            {
                normalized = (char)('0' + (keyInfo.Key - ConsoleKey.NumPad0));
                return true;
            }

            normalized = default;
            return false;
        }

        // Converts the three-character string into pre-concatenated glyph rows so the ASCII banner can be redrawn quickly.
        private static string[] BuildGlyphRows(string padded)
        {
            string[][] glyphs = new string[padded.Length][];
            for (int i = 0; i < padded.Length; i++)
            {
                char key = padded[i];
                if (!GlyphCache.TryGetValue(key, out var glyph))
                {
                    glyph = GlyphLibrary.GetGlyph(key);
                    GlyphCache[key] = glyph;
                }
                glyphs[i] = glyph;
            }

            string[] composed = new string[GlyphLibrary.GlyphHeight];
            for (int row = 0; row < GlyphLibrary.GlyphHeight; row++)
            {
                var builder = GlyphBuilder ??= new StringBuilder(64);
                builder.Clear();
                for (int i = 0; i < glyphs.Length; i++)
                {
                    builder.Append(glyphs[i][row]).Append(' ');
                }
                composed[row] = builder.ToString();
            }
            return composed;
        }
    }
}
