using System;
using System.IO;

namespace stackoverflow_minigame
{
    internal class Program
    {
        private const string DiagnosticsArg = "trace";
        private const string ModeEnvVar = "STACKOVERFLOW_MINIGAME_MODE";
        private static bool diagnosticsHooked;

        private static void Main(string[] args)
        {
            var parsedArgs = NormalizeArgs(args, out var unknownArgs, out bool sufferedParseError);
            if (sufferedParseError || unknownArgs.Count > 0)
            {
                if (unknownArgs.Count > 0)
                {
                    Console.WriteLine($"Unknown options: {string.Join(", ", unknownArgs)}");
                }
                PrintUsage();
                Environment.ExitCode = 1;
                return;
            }
            bool enableDiagnostics = parsedArgs.Remove(DiagnosticsArg);
            bool leaderboardRequested = ShouldLaunchLeaderboard();
            if (leaderboardRequested)
            {
                if (enableDiagnostics)
                {
                    Diagnostics.ReportWarning("Trace mode is not supported for the leaderboard viewer; the argument was ignored.");
                }
                LeaderboardViewer viewer = new LeaderboardViewer();
                viewer.Run();
                return;
            }
            Console.WriteLine("Tip: Press 'L' anytime for the built-in leaderboard, or visit https://stackoverflow-minigame.fly.dev/ for the live feed.\n");
            Game game = new Game();
            if (enableDiagnostics)
            {
                Console.WriteLine("[trace] Diagnostics tracing enabled.");
                HookDiagnostics(game);
            }
            GlyphLibrary.ReportStatus();

            try
            {
                game.Run();
            }
            finally
            {
                Tracing.Dispose();
                diagnosticsHooked = false;
            }
        }

        private static bool ShouldLaunchLeaderboard()
        {
            string? requestedMode = Environment.GetEnvironmentVariable(ModeEnvVar);
            return !string.IsNullOrWhiteSpace(requestedMode) &&
                   requestedMode.Trim().Equals("leaderboard", StringComparison.OrdinalIgnoreCase);
        }

        // Central place to wire verbose diagnostics so --trace lights up every relevant event without scattering hooks.
        private static void HookDiagnostics(Game game)
        {
            if (diagnosticsHooked)
            {
                return;
            }

            diagnosticsHooked = true;

            game.InitialsPromptStarted += () => Diagnostics.ReportInfo("Initials prompt started.");
            game.InitialsCharAccepted += ch => Diagnostics.ReportInfo($"Initials char accepted: {ch}");
            game.InitialsCharRejected += ch => Diagnostics.ReportInfo($"Initials char rejected: {ch}");
            game.InitialsCommitted += initials => Diagnostics.ReportInfo($"Initials committed: {initials}");
            game.InitialsCanceled += () => Diagnostics.ReportInfo("Initials prompt canceled.");
            game.InitialsFallbackUsed += fallback => Diagnostics.ReportWarning($"Initials fallback used: {fallback}");

            GlyphLibrary.GlyphLookupFallback += ch => Diagnostics.ReportWarning($"Glyph lookup fallback for: {ch}");
        }
        // Parses CLI switches into a normalized set, tracking unknown options and rejecting unsupported syntaxes.
        // <param name="args">The raw command-line arguments.</param>
        // <param name="unknown">Outputs a list of unrecognized options.</param>
        // <param name="parseError">Outputs true if any parsing errors were encountered.</param>
        // <returns>A set of recognized, normalized options.</returns>
        private static HashSet<string> NormalizeArgs(string[] args, out List<string> unknown, out bool parseError)
        {
            HashSet<string> normalized = new(StringComparer.OrdinalIgnoreCase);
            unknown = new List<string>();
            parseError = false;
            foreach (string raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string trimmedRaw = raw.Trim();
                int dashCount = 0;
                while (dashCount < trimmedRaw.Length && trimmedRaw[dashCount] == '-')
                {
                    dashCount++;
                }
                if (dashCount > 2)
                {
                    parseError = true;
                    Console.WriteLine($"Unsupported option syntax: '{raw}'. Use '-' or '--' (e.g., --trace).");
                    continue;
                }
                string trimmed = dashCount > 0 ? trimmedRaw[dashCount..] : trimmedRaw;
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (trimmed.Contains('='))
                {
                    parseError = true;
                    Console.WriteLine($"Unsupported option syntax: '{raw}'. Use space-delimited flags (e.g., --trace).");
                    continue;
                }
                if (trimmed.Equals(DiagnosticsArg, StringComparison.OrdinalIgnoreCase))
                {
                    normalized.Add(trimmed);
                }
                else
                {
                    unknown.Add(trimmed);
                }
            }
            return normalized;
        }
        // Prints usage information to the console.
        private static void PrintUsage()
        {
            try
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  dotnet run                                   # Start game (press 'L' anytime for leaderboard)");
                Console.WriteLine("  dotnet run -- trace                          # Enable verbose diagnostics");
                Console.WriteLine("  STACKOVERFLOW_MINIGAME_MODE=leaderboard dotnet run");
                Console.WriteLine("                                               # Standalone leaderboard viewer");
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to print usage information.", ex);
            }
        }
    }
}
