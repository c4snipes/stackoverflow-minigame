using System;
using System.Collections.Generic;
using System.Linq;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Profanity filter for player initials and short strings.
    /// Keeps the arcade experience family-friendly.
    /// </summary>
    internal static class ProfanityFilter
    {
        // Forbidden word list for arcade-style filtering
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

        /// <summary>
        /// Filters initials for profanity, returning sanitized version if needed.
        /// </summary>
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
