using System;

namespace stackoverflow_minigame {
    class Program {
        private const string LeaderboardArg = "leaderboard";

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
            game.Run();
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
