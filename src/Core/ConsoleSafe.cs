using System;
using System.IO;
using System.Security;
using System.Threading;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Provides defensive wrappers around console APIs so rendering can degrade gracefully in constrained environments.
    /// </summary>
    internal static class ConsoleSafe
    {
        // Throttle diagnostic warnings to prevent spam when console access repeatedly fails
        private static long lastWidthWarningTicks = DateTime.MinValue.Ticks;
        private static long lastHeightWarningTicks = DateTime.MinValue.Ticks;
        private const double WarningCooldownSeconds = 4;
        private static readonly long WarningCooldownTicks = TimeSpan.FromSeconds(WarningCooldownSeconds).Ticks;

        /// <summary>
        /// Gets the console buffer width, returning a fallback value if unavailable.
        /// Warnings are throttled to avoid spamming when console access fails repeatedly.
        /// </summary>
        public static int GetBufferWidth(int fallback)
        {
            try
            {
                int width = Console.BufferWidth;
                if (width > 0)
                {
                    return width;
                }
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

        /// <summary>
        /// Gets the console buffer height, returning a fallback value if unavailable.
        /// Warnings are throttled to avoid spamming when console access fails repeatedly.
        /// </summary>
        public static int GetBufferHeight(int fallback)
        {
            try
            {
                int height = Console.BufferHeight;
                if (height > 0)
                {
                    return height;
                }
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
                if (nowTicks - previous < WarningCooldownTicks)
                {
                    return;
                }

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
                // Silently reject negative coordinates - this is expected in some corner cases
                return false;
            }

            int width = GetBufferWidth(-1);
            if (width >= 0 && left >= width)
            {
                // Silently reject out-of-bounds moves - terminal is too small
                return false;
            }
            int height = GetBufferHeight(-1);
            if (height >= 0 && top >= height)
            {
                // Silently reject out-of-bounds moves - terminal is too small
                return false;
            }

            if (width < 0 || height < 0)
            {
                return false;
            }

            // Gracefully degrade: never crash the app due to cursor positioning failures in constrained terminals
            try
            {
                Console.SetCursorPosition(left, top);
                return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException)
            {
                // Silently fail for cursor positioning errors - common when terminal is constrained
                return false;
            }
        }

        public static void WriteLine(string text)
        {
            try
            {
                Console.WriteLine(text);
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException)
            {
                Diagnostics.ReportFailure("Failed to write to the console.", ex);
            }
        }

        public static void Write(string text)
        {
            try
            {
                Console.Write(text);
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or SecurityException or PlatformNotSupportedException)
            {
                Diagnostics.ReportFailure("Failed to write to the console.", ex);
            }
        }

        public static bool WaitForKey(TimeSpan timeout, out ConsoleKeyInfo key)
        {
            key = default;
            DateTime end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end)
            {
                if (Console.IsInputRedirected)
                {
                    Thread.Sleep(50);
                    continue;
                }
                try
                {
                    if (Console.KeyAvailable)
                    {
                        key = Console.ReadKey(intercept: true);
                        return true;
                    }
                }
                catch
                {
                    break;
                }
                Thread.Sleep(50);
            }
            return false;
        }
    }
}
