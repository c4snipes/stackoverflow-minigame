using System;

namespace stackoverflow_minigame
{
    internal static class TimeFormatting
    {
        public static string FormatDuration(TimeSpan span)
        {// Clamp negative durations to zero.
            if (span < TimeSpan.Zero)
            {
                Diagnostics.ReportWarning($"FormatDuration received a negative duration ({span}). Clamping to zero.");
                span = TimeSpan.Zero;
            }
            // Handle zero duration explicitly.
            // This avoids issues with formatting and ensures consistent output.
            if (span == TimeSpan.Zero)
            {
                return "00:00.000";
            }
            // Format minutes, seconds, and milliseconds for durations under one hour.
            // Use at least two digits for minutes.
            // For durations of one hour or more, include hours.
            // Use at least two digits for hours, expanding as needed.
            // Calculate total hours to determine formatting.
            // Use total hours to handle durations longer than 99 hours.
            // Extract seconds and milliseconds for formatting.
            long totalHours = span.Ticks / TimeSpan.TicksPerHour;
            int seconds = span.Seconds;
            int milliseconds = span.Milliseconds;

            if (totalHours == 0)
            {
                long totalMinutes = span.Ticks / TimeSpan.TicksPerMinute;
                return $"{totalMinutes:00}:{seconds:00}.{milliseconds:000}";
            }
            // Format hours, minutes, seconds, and milliseconds for durations of one hour or more.
            // Use at least two digits for hours, expanding as needed for large durations.
            string hourFormat = totalHours < 100 ? $"{totalHours:00}" : totalHours.ToString();
            return $"{hourFormat}:{span.Minutes:00}:{seconds:00}.{milliseconds:000}";
        }
    }
}
