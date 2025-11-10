using System;

namespace stackoverflow_minigame {
    class Program {
        private const string LeaderboardArg = "leaderboard";
        private const string DiagnosticsArg = "trace";
        private static bool diagnosticsHooked;

        static void Main(string[] args) {
            var parsedArgs = NormalizeArgs(args, out var unknownArgs, out bool sufferedParseError);
            if (sufferedParseError || unknownArgs.Count > 0) {
                if (unknownArgs.Count > 0) {
                    Console.WriteLine($"Unknown options: {string.Join(", ", unknownArgs)}");
                }
                PrintUsage();
                Environment.ExitCode = 1;
                return;
            }
            bool enableDiagnostics = parsedArgs.Remove(DiagnosticsArg);

            if (parsedArgs.Remove(LeaderboardArg)) {
                if (enableDiagnostics) {
                    Diagnostics.ReportWarning("Trace mode is not supported for the leaderboard viewer; the argument was ignored.");
                }
                LeaderboardViewer viewer = new LeaderboardViewer();
                viewer.Run();
                return;
            }

            Console.WriteLine("Tip: Run 'dotnet run -- leaderboard' or './launch-leaderboard.sh' in another tab to view live standings.\n");
            Game game = new Game();
            if (enableDiagnostics) {
                Console.WriteLine("[trace] Diagnostics tracing enabled.");
                HookDiagnostics(game);
            }
            GlyphLibrary.ReportStatus();

            try {
                game.Run();
            } finally {
                Tracing.Dispose();
                // No cleanup needed for diagnosticsHooked here.
            }
        }

        private static void HookDiagnostics(Game game) {
            if (diagnosticsHooked) return;
            diagnosticsHooked = true;

            game.InitialsPromptStarted += () => Diagnostics.ReportInfo("Initials prompt started.");
            game.InitialsCharAccepted += ch => Diagnostics.ReportInfo($"Initials char accepted: {ch}");
            game.InitialsCharRejected += ch => Diagnostics.ReportInfo($"Initials char rejected: {ch}");
            game.InitialsCommitted += initials => Diagnostics.ReportInfo($"Initials committed: {initials}");
            game.InitialsCanceled += () => Diagnostics.ReportInfo("Initials prompt canceled.");
            game.InitialsFallbackUsed += fallback => Diagnostics.ReportWarning($"Initials fallback used: {fallback}");

            GlyphLibrary.GlyphLookupFallback += ch => Diagnostics.ReportWarning($"Glyph lookup fallback for: {ch}");
        }

        private static HashSet<string> NormalizeArgs(string[] args, out List<string> unknown, out bool parseError) {
            HashSet<string> normalized = new(StringComparer.OrdinalIgnoreCase);
            unknown = new List<string>();
            parseError = false;
            foreach (string raw in args) {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string trimmed = raw.Trim().TrimStart('-');
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.Contains('=')) {
                    parseError = true;
                    Console.WriteLine($"Unsupported option syntax: '{raw}'. Use space-delimited flags (e.g., --trace).");
                    continue;
                }
                if (trimmed.Equals(LeaderboardArg, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals(DiagnosticsArg, StringComparison.OrdinalIgnoreCase)) {
                    normalized.Add(trimmed);
                } else {
                    unknown.Add(trimmed);
                }
            }
            return normalized;
        }

        private static void PrintUsage() {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run              # run the game");
            Console.WriteLine("  dotnet run -- leaderboard | ./launch-leaderboard.sh");
            Console.WriteLine("  dotnet run -- trace     # enable verbose diagnostics for initials/glyphs");
        }
    }
}
