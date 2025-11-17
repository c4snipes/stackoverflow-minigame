using System;
using System.IO;
using System.Runtime.InteropServices;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Detects and stores terminal capabilities for adaptive rendering.
    /// Helps the game adapt to different terminal environments (Windows Terminal, iTerm2, basic terminals, etc.)
    /// </summary>
    internal sealed class TerminalCapabilities
    {
        private static TerminalCapabilities? instance;
        private static readonly object lockObj = new();

        public bool SupportsColor { get; }
        public bool SupportsExtendedColors { get; }
        public bool SupportsUtf8 { get; }
        public bool SupportsCursorControl { get; }
        public bool SupportsConsoleTitle { get; }
        public bool IsInteractive { get; }
        public int MinWidth { get; }
        public int MinHeight { get; }
        public string TerminalType { get; }
        public bool IsWindowsTerminal { get; }
        public bool IsVSCodeTerminal { get; }

        private TerminalCapabilities()
        {
            // Detect if input/output is redirected
            IsInteractive = !Console.IsInputRedirected && !Console.IsOutputRedirected;

            // Detect terminal type from environment
            string term = Environment.GetEnvironmentVariable("TERM") ?? "";
            string termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? "";
            string wtSession = Environment.GetEnvironmentVariable("WT_SESSION") ?? "";

            IsWindowsTerminal = !string.IsNullOrEmpty(wtSession);
            IsVSCodeTerminal = termProgram.Contains("vscode", StringComparison.OrdinalIgnoreCase);

            TerminalType = DetermineTerminalType(term, termProgram, wtSession);

            // Detect color support
            SupportsColor = DetectColorSupport(term);
            SupportsExtendedColors = DetectExtendedColorSupport(term, termProgram);

            // Detect UTF-8 support
            SupportsUtf8 = DetectUtf8Support();

            // Detect cursor control
            SupportsCursorControl = DetectCursorControl();

            // Detect console title support
            SupportsConsoleTitle = DetectConsoleTitleSupport();

            // Determine minimum dimensions
            (MinWidth, MinHeight) = DetectMinimumDimensions();

            LogCapabilities();
        }

        /// <summary>
        /// Singleton instance providing terminal capability detection.
        /// Thread-safe lazy initialization.
        /// </summary>
        public static TerminalCapabilities Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (lockObj)
                    {
                        instance ??= new TerminalCapabilities();
                    }
                }
                return instance;
            }
        }

        private static string DetermineTerminalType(string term, string termProgram, string wtSession)
        {
            if (!string.IsNullOrEmpty(wtSession))
            {
                return "Windows Terminal";
            }

            if (termProgram.Contains("vscode", StringComparison.OrdinalIgnoreCase))
            {
                return "VS Code";
            }

            if (termProgram.Contains("iTerm", StringComparison.OrdinalIgnoreCase))
            {
                return "iTerm2";
            }

            if (termProgram.Contains("Apple_Terminal", StringComparison.OrdinalIgnoreCase))
            {
                return "macOS Terminal";
            }

            if (term.Contains("xterm", StringComparison.OrdinalIgnoreCase))
            {
                return "xterm";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows Console";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux Terminal";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "macOS Terminal";
            }

            return term != "" ? term : "Unknown";
        }

        private static bool DetectColorSupport(string term)
        {
            // Check for color support indicators
            if (term.Contains("color", StringComparison.OrdinalIgnoreCase) ||
                term.Contains("256", StringComparison.OrdinalIgnoreCase) ||
                term.Contains("xterm", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check for NO_COLOR environment variable (universal color disable)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            {
                return false;
            }

            // Check for COLORTERM
            string colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
            if (!string.IsNullOrEmpty(colorTerm))
            {
                return true;
            }

            // Windows always supports color in modern versions
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return true;
            }

            // Default to true for modern terminals
            return true;
        }

        private static bool DetectExtendedColorSupport(string term, string termProgram)
        {
            // Check for 256-color or truecolor support
            if (term.Contains("256color") || term.Contains("truecolor"))
            {
                return true;
            }

            string colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
            if (colorTerm.Contains("truecolor") || colorTerm.Contains("24bit"))
            {
                return true;
            }

            // Modern terminals typically support extended colors
            if (termProgram.Contains("iTerm") ||
                termProgram.Contains("vscode") ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION")))
            {
                return true;
            }

            return false;
        }

        private static bool DetectUtf8Support()
        {
            try
            {
                // Check encoding
                if (Console.OutputEncoding.CodePage == 65001) // UTF-8
                {
                    return true;
                }

                // Check environment variables
                string lang = Environment.GetEnvironmentVariable("LANG") ?? "";
                if (lang.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) ||
                    lang.Contains("UTF8", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Modern systems typically use UTF-8
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool DetectCursorControl()
        {
            if (Console.IsOutputRedirected)
            {
                return false;
            }

            try
            {
                // Test if we can read and modify cursor position
                int savedLeft = Console.CursorLeft;
                int savedTop = Console.CursorTop;
                Console.SetCursorPosition(savedLeft, savedTop);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool DetectConsoleTitleSupport()
        {
            if (Console.IsOutputRedirected)
            {
                return false;
            }

            // Console.Title is only supported on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            try
            {
                string originalTitle = Console.Title;
                Console.Title = originalTitle; // Try to set it back
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static (int width, int height) DetectMinimumDimensions()
        {
            try
            {
                int width = Console.BufferWidth;
                int height = Console.BufferHeight;

                // Ensure reasonable minimums
                width = Math.Max(40, width);
                height = Math.Max(20, height);

                return (width, height);
            }
            catch
            {
                // Safe fallback values
                return (80, 24);
            }
        }

        private void LogCapabilities()
        {
            Diagnostics.ReportInfo($"Terminal: {TerminalType}");
            Diagnostics.ReportInfo($"  Interactive: {IsInteractive}");
            Diagnostics.ReportInfo($"  Color Support: {SupportsColor}");
            Diagnostics.ReportInfo($"  Extended Colors: {SupportsExtendedColors}");
            Diagnostics.ReportInfo($"  UTF-8: {SupportsUtf8}");
            Diagnostics.ReportInfo($"  Cursor Control: {SupportsCursorControl}");
            Diagnostics.ReportInfo($"  Title Support: {SupportsConsoleTitle}");
            Diagnostics.ReportInfo($"  Min Dimensions: {MinWidth}x{MinHeight}");
        }

        /// <summary>
        /// Gets a color-safe string by falling back to a simpler version if needed
        /// </summary>
        public string GetColorSafeString(string colorful, string simple)
        {
            return SupportsColor ? colorful : simple;
        }

        /// <summary>
        /// Gets a UTF-8 safe string by falling back to ASCII if needed
        /// </summary>
        public string GetUtf8SafeString(string utf8, string ascii)
        {
            return SupportsUtf8 ? utf8 : ascii;
        }

        /// <summary>
        /// Checks if the current terminal dimensions meet minimum requirements
        /// </summary>
        public bool MeetsMinimumDimensions(int requiredWidth, int requiredHeight)
        {
            try
            {
                return Console.BufferWidth >= requiredWidth &&
                       Console.BufferHeight >= requiredHeight;
            }
            catch
            {
                return false;
            }
        }
    }
}
