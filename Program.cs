using System;

namespace stackoverflow_minigame {
    class Program {
        private const string LeaderboardArg = "leaderboard";
        private static bool diagnosticsHooked;

        static void Main(string[] args) {
            var parsedArgs = NormalizeArgs(args);
            if (parsedArgs.Contains(LeaderboardArg)) {
                LeaderboardViewer viewer = new LeaderboardViewer();
                viewer.Run();
                return;
            }

            if (parsedArgs.Count > 0) {
                PrintUsage();
                return;
            }

            Console.WriteLine("Tip: Run 'dotnet run -- leaderboard' or './launch-leaderboard.sh' in another tab to view live standings.\n");
            Game game = new Game();
            HookDiagnostics(game);
            game.Run();
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

            GlyphLibrary.GlyphLookupStarted += ch => Diagnostics.ReportInfo($"Glyph lookup started: {ch}");
            GlyphLibrary.GlyphLookupSucceeded += (ch, glyph) => Diagnostics.ReportInfo($"Glyph lookup succeeded: {ch}");
            GlyphLibrary.GlyphLookupFallback += ch => Diagnostics.ReportWarning($"Glyph lookup fallback for: {ch}");
        }

        private static HashSet<string> NormalizeArgs(string[] args) {
            HashSet<string> normalized = new(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in args) {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string trimmed = raw.Trim();
                trimmed = trimmed.TrimStart('-');
                if (!string.IsNullOrEmpty(trimmed)) {
                    normalized.Add(trimmed);
                }
            }
            return normalized;
        }

        private static void PrintUsage() {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run              # run the game");
            Console.WriteLine("  dotnet run -- leaderboard | ./launch-leaderboard.sh");
            Console.WriteLine("                          # view the live leaderboard in this tab");
        }
    }
}
