using System;
using System.Collections.Generic;
using System.Linq;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Centralized profanity filtering so initials (and other short strings) remain arcade friendly.
    /// </summary>
    internal static class ProfanityFilter
    {
        private static readonly HashSet<string> Forbidden =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ASS", "BUM", "CUM", "FUK",
                "PEE", "POO", "SEX",
                "FUC", "WTF", "DIE",
                "SHT", "SUX", "TIT",
                "COC", "CNT", "DCK",
                "JIZ", "CUN",
                "PNS", "VAG", "PEN"
            };

        private const string SanitizedInitials = "PG!";

        public static string FilterInitials(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "AAA";
            }

            string cleaned = value.Trim().ToUpperInvariant();
            if (ContainsProfanity(cleaned))
            {
                Diagnostics.ReportWarning($"Initials '{cleaned}' replaced due to profanity filter.");
                return SanitizedInitials;
            }

            return cleaned;
        }

        public static bool ContainsProfanity(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return Forbidden.Any(word => normalized.Contains(word, StringComparison.OrdinalIgnoreCase));
        }
    }
}
