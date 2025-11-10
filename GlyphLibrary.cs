using System;
using System.Collections.Generic;

namespace stackoverflow_minigame {
    static class GlyphLibrary {
        public const int GlyphHeight = 5;

        public static event Action<char>? GlyphLookupStarted;
        public static event Action<char, string[]>? GlyphLookupSucceeded;
        public static event Action<char>? GlyphLookupFallback;

        private static readonly Dictionary<char, string[]> Glyphs = CreateGlyphMap();

        public static string[] GetGlyph(char ch) {
            char key = char.ToUpperInvariant(ch);
            bool trace = HasGlyphInstrumentation;
            if (trace) {
                GlyphLookupStarted?.Invoke(key);
            }

            if (Glyphs.TryGetValue(key, out var glyph)) {
                if (trace) {
                    GlyphLookupSucceeded?.Invoke(key, glyph);
                }
                return glyph;
            }

            if (trace) {
                GlyphLookupFallback?.Invoke(key);
            }
            return Glyphs[' '];
        }

        private static bool HasGlyphInstrumentation =>
            GlyphLookupStarted != null || GlyphLookupSucceeded != null || GlyphLookupFallback != null;

        private static Dictionary<char, string[]> CreateGlyphMap() {
            var glyphs = new Dictionary<char, string[]>();

            void Add(char key, params string[] lines) {
                if (lines.Length != GlyphHeight) {
                    throw new ArgumentException("Glyph height mismatch", nameof(lines));
                }
                glyphs[char.ToUpperInvariant(key)] = lines;
            }

            Add('A',
                " ▄█▄ ",
                "█▀ ▀█",
                "█████",
                "█   █",
                "█   █");

            Add('B',
                "████▄",
                "█▀ ▀█",
                "████▀",
                "█▀ ▀█",
                "████▀");

            Add('C',
                " ▄██",
                "█▀  ",
                "█   ",
                "█▄  ",
                " ▀██");

            Add('D',
                "████▄",
                "█  ▀█",
                "█   █",
                "█  ▄█",
                "████▀");

            Add('E',
                "█████",
                "█▀  ",
                "███ ",
                "█▀  ",
                "█████");

            Add('F',
                "█████",
                "█▀  ",
                "███ ",
                "█▀  ",
                "█   ");

            Add('G',
                " ▄██ ",
                "█▀   ",
                "█  ██",
                "█   █",
                " ▀██ ");

            Add('H',
                "█   █",
                "█   █",
                "█████",
                "█   █",
                "█   █");

            Add('I',
                "███",
                " █ ",
                " █ ",
                " █ ",
                "███");

            Add('J',
                "  ███",
                "   █",
                "   █",
                "█  █",
                " ▀▀ ");

            Add('K',
                "█  █",
                "█ █ ",
                "██  ",
                "█ █ ",
                "█  █");

            Add('L',
                "█   ",
                "█   ",
                "█   ",
                "█   ",
                "█████");

            Add('M',
                "█   █",
                "██ ██",
                "█ █ █",
                "█   █",
                "█   █");

            Add('N',
                "█   █",
                "██  █",
                "█ █ █",
                "█  ██",
                "█   █");

            Add('O',
                " ▄█▄ ",
                "█   █",
                "█   █",
                "█   █",
                " ▀█▀ ");

            Add('P',
                "████▄",
                "█  ▀█",
                "████▀",
                "█    ",
                "█    ");

            Add('Q',
                " ▄█▄ ",
                "█   █",
                "█ █ █",
                "█  █▀",
                " ▀█▄▀");

            Add('R',
                "████▄",
                "█  ▀█",
                "████▀",
                "█ █  ",
                "█  █ ");

            Add('S',
                " ▄██",
                "█▀  ",
                " ▀██",
                "   █",
                "██▀ ");

            Add('T',
                "█████",
                "  █  ",
                "  █  ",
                "  █  ",
                "  █  ");

            Add('U',
                "█   █",
                "█   █",
                "█   █",
                "█   █",
                " ▀█▀ ");

            Add('V',
                "█   █",
                "█   █",
                "█   █",
                " ▀▄▀ ",
                "  █  ");

            Add('W',
                "█   █",
                "█   █",
                "█ █ █",
                "██ ██",
                "█   █");

            Add('X',
                "█   █",
                " ▀▄▀ ",
                "  █  ",
                " ▄▀▄ ",
                "█   █");

            Add('Y',
                "█   █",
                " ▀▄▀ ",
                "  █  ",
                "  █  ",
                "  █  ");

            Add('Z',
                "█████",
                "  ▄▀ ",
                " ▄▀  ",
                "▀▄   ",
                "█████");

            Add('0',
                " ▄█▄ ",
                "█ ▄ █",
                "█ █ █",
                "█▄ ▀█",
                " ▀█▀ ");

            Add('1',
                " ▄ ",
                "  █",
                "  █",
                "  █",
                "████");

            Add('2',
                " ▄█▄",
                "█   █",
                "  ▄▀",
                " ▀▄ ",
                "█████");

            Add('3',
                " ▄█▄",
                "█   █",
                "  ▀█",
                "█   █",
                " ▀█▀");

            Add('4',
                "█  █",
                "█  █",
                "████",
                "   █",
                "   █");

            Add('5',
                "█████",
                "█    ",
                "████ ",
                "   █ ",
                "████ ");

            Add('6',
                " ▄██",
                "█   ",
                "████ ",
                "█  █",
                " ▀█▀");

            Add('7',
                "█████",
                "   ▄▀",
                "  ▄▀ ",
                " ▄▀  ",
                " ▀   ");

            Add('8',
                " ▄█▄ ",
                "█   █",
                " ▀█▀ ",
                "█   █",
                " ▀█▀ ");

            Add('9',
                " ▄█▄",
                "█   █",
                " ▀▀█",
                "   █",
                " ▄█▀");

            Add('_',
                "     ",
                "     ",
                "     ",
                "     ",
                "█████");

            Add(' ',
                "     ",
                "     ",
                "     ",
                "     ",
                "     ");

            return glyphs;
        }
    }
}
