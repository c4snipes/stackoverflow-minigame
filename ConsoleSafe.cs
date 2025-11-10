using System;
using System.IO;
using System.Security;
using System.Threading;

namespace stackoverflow_minigame
{
    // Provides defensive wrappers around console APIs so rendering can degrade gracefully in constrained environments.
    static class ConsoleSafe
    {
        private static long lastWidthWarningTicks = DateTime.MinValue.Ticks;
        private static long lastHeightWarningTicks = DateTime.MinValue.Ticks;
        private const double WarningCooldownSeconds = 2;
        private static readonly long WarningCooldownTicks = TimeSpan.FromSeconds(WarningCooldownSeconds).Ticks;

        public static int GetBufferWidth(int fallback)
        {
            try
            {
                int width = Console.BufferWidth;
                if (width > 0) return width;
                ThrottledWarning(ref lastWidthWarningTicks, $"Console reported non-positive buffer width ({width}); using fallback {fallback}.");
                return fallback;
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException)
            {
                Diagnostics.ReportFailure("Failed to read console buffer width.", ex);
                ThrottledWarning(ref lastWidthWarningTicks, "Failed to read console buffer width. Falling back.");
                return fallback;
            }
        }

        public static int GetBufferHeight(int fallback)
        {
            try
            {
                int height = Console.BufferHeight;
                if (height > 0) return height;
                ThrottledWarning(ref lastHeightWarningTicks, $"Console reported non-positive buffer height ({height}); using fallback {fallback}.");
                return fallback;
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException)
            {
                Diagnostics.ReportFailure("Failed to read console buffer height.", ex);
                ThrottledWarning(ref lastHeightWarningTicks, "Failed to read console buffer height. Falling back.");
                return fallback;
            }
        }

        private static void ThrottledWarning(ref long lastTicks, string message)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            while (true)
            {
                long previous = Interlocked.Read(ref lastTicks);
                if (nowTicks - previous < WarningCooldownTicks) return;
                if (Interlocked.CompareExchange(ref lastTicks, nowTicks, previous) == previous)
                {
                    Diagnostics.ReportWarning(message);
                    return;
                }
            }
        }

        public static bool TrySetCursorPosition(int left, int top)
        {
            if (left < 0 || top < 0)
            {
                Diagnostics.ReportFailure($"Rejected cursor move to negative coordinate ({left}, {top}).");
                return false;
            }

            int width = GetBufferWidth(-1);
            if (width >= 0 && left >= width)
            {
                Diagnostics.ReportFailure($"Rejected cursor move beyond buffer width (left={left}, width={width}).");
                return false;
            }

            int height = GetBufferHeight(-1);
            if (height >= 0 && top >= height)
            {
                Diagnostics.ReportFailure($"Rejected cursor move beyond buffer height (top={top}, height={height}).");
                return false;
            }

            if (width < 0 || height < 0)
            {
                return false;
            }

            try
            {
                Console.SetCursorPosition(left, top);
                return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException)
            {
                Diagnostics.ReportFailure($"Failed to set cursor position to ({left}, {top}).", ex);
                return false;
            }
        }
    }
}
