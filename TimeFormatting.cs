using System;

namespace stackoverflow_minigame
{
    internal static class TimeFormatting
    {
        public static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
            {
                Diagnostics.ReportWarning($"FormatDuration received a negative duration ({span}). Clamping to zero.");
                span = TimeSpan.Zero;
            }

            if (span == TimeSpan.Zero)
            {
                return "00:00.000";
            }

            long totalHours = span.Ticks / TimeSpan.TicksPerHour;
            int seconds = span.Seconds;
            int milliseconds = span.Milliseconds;

            if (totalHours == 0)
            {
                long totalMinutes = span.Ticks / TimeSpan.TicksPerMinute;
                return $"{totalMinutes:00}:{seconds:00}.{milliseconds:000}";
            }

            string hourFormat = totalHours < 100 ? $"{totalHours:00}" : totalHours.ToString();
            return $"{hourFormat}:{span.Minutes:00}:{seconds:00}.{milliseconds:000}";
        }
    }
}
