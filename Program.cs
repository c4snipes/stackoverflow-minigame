using System;

namespace stackoverflow_minigame {
    class Program {
        private const string LeaderboardArg = "leaderboard";

        static void Main(string[] args) {
            if (args.Length > 0 && IsLeaderboardArg(args[0])) {
                LeaderboardViewer viewer = new LeaderboardViewer();
                viewer.Run();
                return;
            }

            Game game = new Game();
            game.Run();
        }

        private static bool IsLeaderboardArg(string? arg) =>
            !string.IsNullOrWhiteSpace(arg) &&
            string.Equals(arg.Trim(), LeaderboardArg, StringComparison.OrdinalIgnoreCase);
    }
}
