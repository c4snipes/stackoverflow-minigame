using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Provides access to 5x5 glyph art for characters used in the game, with caching and fallback instrumentation.
    /// </summary>
    internal static class GlyphLibrary
    {
        public const int GlyphHeight = 5;
        public const int GlyphWidth = 5;

        private const char FallbackGlyphKey = ' ';
        private const string GlyphCharacterSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_ ";

        public static event Action<char>? GlyphLookupStarted;
        public static event Action<char, string[]>? GlyphLookupSucceeded;
        public static event Action<char>? GlyphLookupFallback;

        private static readonly char[] RequiredGlyphs = GlyphCharacterSet.ToCharArray();
        private static readonly HashSet<char> AllowedGlyphs = new(GlyphCharacterSet);

        private static readonly Dictionary<char, byte[]> GlyphBitmaps = BuildGlyphBitmaps();
        private static readonly ConcurrentDictionary<char, string[]> GlyphCache = new();
        private static readonly ConcurrentDictionary<char, byte> FallbackWarnedGlyphs = new();
        private static bool loadStatusReported;
        public static string StatusSummary { get; private set; } = "[GlyphLibrary] Glyph inventory pending...";

        static GlyphLibrary()
        {
            PrimeCacheForAllowedGlyphs();
        }
        // Retrieves the glyph representation for the specified character. If the glyph is not found, returns the fallback glyph.
        // Caches allowed glyphs for performance.
        // Triggers instrumentation events for lookup start, success, and fallback.
        // Uses a thread-safe cache to store decoded glyphs.
        public static string[] GetGlyph(char ch)
        {
            char key = char.ToUpperInvariant(ch);
            bool trace = HasGlyphInstrumentation;
            if (trace)
            {
                GlyphLookupStarted?.Invoke(key);
            }

            bool usedFallback = false;
            if (!GlyphCache.TryGetValue(key, out var glyph))
            {
                if (!GlyphBitmaps.TryGetValue(key, out var bitmap))
                {
                    usedFallback = true;
                    bool fallbackInstrumented = GlyphLookupFallback != null;
                    GlyphLookupFallback?.Invoke(key);
                    if (!fallbackInstrumented && FallbackWarnedGlyphs.TryAdd(key, 0))
                    {
                        Diagnostics.ReportWarning($"Glyph lookup fallback for '{key}'.");
                    }
                    bitmap = GetFallbackBitmap();
                }

                glyph = DecodeGlyph(bitmap);
                if (AllowedGlyphs.Contains(key))
                {
                    GlyphCache[key] = glyph;
                }
            }

            if (!usedFallback && trace)
            {
                GlyphLookupSucceeded?.Invoke(key, glyph);
            }

            return glyph;
        }
        // Determines if any glyph lookup instrumentation is subscribed.
        private static bool HasGlyphInstrumentation =>
            GlyphLookupStarted != null || GlyphLookupSucceeded != null || GlyphLookupFallback != null;
        // Reports the status of glyph loading, including any missing required glyphs.
        public static void ReportStatus()
        {
            if (loadStatusReported)
            {
                return;
            }

            loadStatusReported = true;

            int variants = GlyphBitmaps.Count;
            StatusSummary = $"[GlyphLibrary] Glyph cache online ({variants} variants ready).";

            // Verify all required glyphs are loaded at startup.
            // Missing glyphs fall back to space character.
            List<char> missing = new();
            foreach (char required in RequiredGlyphs)
            {
                if (!GlyphBitmaps.ContainsKey(required))
                {
                    missing.Add(required);
                }
            }

            if (missing.Count == 0)
            {
                StatusSummary = "[GlyphLibrary] All required glyphs loaded. Initials prompt fully armed.";
                Diagnostics.ReportInfo(StatusSummary);
            }
            else
            {
                string missingList = string.Join(", ", missing);
                StatusSummary = $"[GlyphLibrary] Missing glyphs: {missingList}";
                Diagnostics.ReportWarning(StatusSummary);
            }
        }
        private static void PrimeCacheForAllowedGlyphs()
        {
            foreach (char allowed in AllowedGlyphs)
            {
                if (!GlyphBitmaps.TryGetValue(allowed, out var bitmap))
                {
                    Diagnostics.ReportWarning($"Glyph '{allowed}' is allowed but no bitmap was defined.");
                    continue;
                }
                try
                {
                    GlyphCache[allowed] = DecodeGlyph(bitmap);
                }
                catch (Exception ex)
                {
                    Diagnostics.ReportFailure($"Failed to prime glyph '{allowed}'.", ex);
                }
            }
        }
        // Retrieves the bitmap for the fallback glyph (space). Throws if the fallback glyph is not defined.
        private static byte[] GetFallbackBitmap()
        {
            if (GlyphBitmaps.TryGetValue(FallbackGlyphKey, out var fallback))
            {
                return fallback;
            }
            throw new InvalidOperationException("GlyphLibrary requires a space glyph (' ') for fallback rendering.");
        }

        // Builds the internal glyph bitmap dictionary from hardcoded definitions. The format is a series of strings
        // representing rows of pixels, where '1' is a filled pixel and '0' is an empty pixel. For reference, please look at GlyphArtReference.txt.

        private static Dictionary<char, byte[]> BuildGlyphBitmaps()
        {
            var map = new Dictionary<char, byte[]>();

            void Add(char key, params string[] rows)
            {
                if (rows.Length != GlyphHeight)
                {
                    throw new ArgumentException("Glyph height mismatch", nameof(rows));
                }

                var bitmap = new byte[GlyphHeight];
                for (int i = 0; i < GlyphHeight; i++)
                {
                    string row = rows[i];
                    if (row.Length != GlyphWidth)
                    {
                        throw new ArgumentException("Glyph width mismatch", nameof(rows));
                    }

                    byte rowBits = 0;
                    for (int col = 0; col < GlyphWidth; col++)
                    {
                        char cell = row[col];
                        if (cell == '1')
                        {
                            rowBits |= (byte)(1 << (GlyphWidth - 1 - col));
                        }
                        else if (cell != '0')
                        {
                            throw new ArgumentException($"Invalid glyph pixel '{cell}' at row {i}, column {col}.", nameof(rows));
                        }
                    }

                    bitmap[i] = rowBits;
                }

                char normalizedKey = char.ToUpperInvariant(key);
                if (map.ContainsKey(normalizedKey))
                {
                    throw new InvalidOperationException($"Duplicate glyph definition for '{normalizedKey}'.");
                }

                map[normalizedKey] = bitmap;
            }

            Add('A',
                "01110",
                "10001",
                "11111",
                "10001",
                "10001");

            Add('B',
                "11110",
                "10001",
                "11110",
                "10001",
                "11110");

            Add('C',
                "01111",
                "10000",
                "10000",
                "10000",
                "01111");

            Add('D',
                "11110",
                "10001",
                "10001",
                "10001",
                "11110");

            Add('E',
                "11111",
                "10000",
                "11110",
                "10000",
                "11111");

            Add('F',
                "11111",
                "10000",
                "11110",
                "10000",
                "10000");

            Add('G',
                "01111",
                "10000",
                "10111",
                "10001",
                "01110");

            Add('H',
                "10001",
                "10001",
                "11111",
                "10001",
                "10001");

            Add('I',
                "11111",
                "00100",
                "00100",
                "00100",
                "11111");

            Add('J',
                "00111",
                "00010",
                "00010",
                "10010",
                "01100");

            Add('K',
                "10001",
                "10010",
                "11100",
                "10010",
                "10001");

            Add('L',
                "10000",
                "10000",
                "10000",
                "10000",
                "11111");

            Add('M',
                "10001",
                "11011",
                "10101",
                "10001",
                "10001");

            Add('N',
                "10001",
                "11001",
                "10101",
                "10011",
                "10001");

            Add('O',
                "01110",
                "10001",
                "10001",
                "10001",
                "01110");

            Add('P',
                "11110",
                "10001",
                "11110",
                "10000",
                "10000");

            Add('Q',
                "01110",
                "10001",
                "10001",
                "10011",
                "01111");

            Add('R',
                "11110",
                "10001",
                "11110",
                "10010",
                "10001");

            Add('S',
                "01111",
                "10000",
                "01110",
                "00001",
                "11110");

            Add('T',
                "11111",
                "00100",
                "00100",
                "00100",
                "00100");

            Add('U',
                "10001",
                "10001",
                "10001",
                "10001",
                "01110");

            Add('V',
                "10001",
                "10001",
                "10001",
                "01010",
                "00100");

            Add('W',
                "10001",
                "10001",
                "10101",
                "11011",
                "10001");

            Add('X',
                "10001",
                "01010",
                "00100",
                "01010",
                "10001");

            Add('Y',
                "10001",
                "01010",
                "00100",
                "00100",
                "00100");

            Add('Z',
                "11111",
                "00010",
                "00100",
                "01000",
                "11111");

            Add('0',
                "01110",
                "10011",
                "10101",
                "11001",
                "01110");

            Add('1',
                "00100",
                "01100",
                "00100",
                "00100",
                "01110");

            Add('2',
                "01110",
                "10001",
                "00010",
                "00100",
                "11111");

            Add('3',
                "11110",
                "00001",
                "00110",
                "00001",
                "11110");

            Add('4',
                "10010",
                "10010",
                "11111",
                "00010",
                "00010");

            Add('5',
                "11111",
                "10000",
                "11110",
                "00001",
                "11110");

            Add('6',
                "01111",
                "10000",
                "11110",
                "10001",
                "01110");

            Add('7',
                "11111",
                "00010",
                "00100",
                "01000",
                "01000");

            Add('8',
                "01110",
                "10001",
                "01110",
                "10001",
                "01110");

            Add('9',
                "01110",
                "10001",
                "01111",
                "00001",
                "11110");

            Add('@',
                "01110",
                "10101",
                "11111",
                "00100",
                "00100");

            Add('#',
                "01010",
                "11111",
                "01010",
                "11111",
                "01010");

            Add('%',
                "11001",
                "11010",
                "00100",
                "01011",
                "10011");

            Add('!',
                "00100",
                "00100",
                "00100",
                "00000",
                "00100");

            Add('?',
                "01110",
                "10001",
                "00110",
                "00000",
                "00100");

            Add('-',
                "00000",
                "00000",
                "11111",
                "00000",
                "00000");

            Add('+',
                "00100",
                "00100",
                "11111",
                "00100",
                "00100");

            Add('_',
                "00000",
                "00000",
                "00000",
                "00000",
                "11111");

            Add(' ',
                "00000",
                "00000",
                "00000",
                "00000",
                "00000");

            if (!map.ContainsKey(FallbackGlyphKey))
            {
                throw new InvalidOperationException("Space glyph (' ') is required for fallback rendering.");
            }

            return map;
        }

        private static string[] DecodeGlyph(byte[] bitmap)
        {
            if (bitmap.Length != GlyphHeight)
            {
                throw new ArgumentException($"Bitmap must be {GlyphHeight} rows.", nameof(bitmap));
            }

            var rows = new string[GlyphHeight];
            for (int i = 0; i < GlyphHeight; i++)
            {
                char[] chars = new char[GlyphWidth];
                byte rowBits = bitmap[i];
                for (int col = 0; col < GlyphWidth; col++)
                {
                    bool filled = (rowBits & (1 << (GlyphWidth - 1 - col))) != 0;
                    chars[col] = filled ? '█' : ' ';
                }

                rows[i] = new string(chars);
            }

            return rows;
        }
    }
}
