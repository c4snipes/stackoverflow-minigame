using System;
using System.Threading;

namespace stackoverflow_minigame {
    class LeaderboardViewer {
        private readonly Scoreboard scoreboard;
        private const int EntriesToDisplay = 8;
        private const int RefreshIntervalMs = 1000;

        public LeaderboardViewer() {
            scoreboard = new Scoreboard(Scoreboard.ResolveDefaultPath());
        }

        public void Run() {
            Console.Title = "Stackoverflow Skyscraper - Leaderboard";
            bool singlePass = Console.IsOutputRedirected;
            if (!singlePass) {
                Console.CursorVisible = false;
            }
            try {
                do {
                    Draw();
                    if (singlePass) break;
                } while (!WaitOrQuit());
            } finally {
                if (!singlePass) {
                    Console.CursorVisible = true;
                }
            }
        }

        private bool WaitOrQuit() {
            if (Console.IsInputRedirected) {
                Thread.Sleep(RefreshIntervalMs);
                return false;
            }
            const int pollIntervalMs = 100;
            int elapsed = 0;
            while (elapsed < RefreshIntervalMs) {
                if (TryReadQuitKey(out bool quit) && quit) {
                    return true;
                }
                Thread.Sleep(pollIntervalMs);
                elapsed += pollIntervalMs;
            }
            return false;
        }

        private bool TryReadQuitKey(out bool quit) {
            quit = false;
            try {
                if (Console.IsInputRedirected || !Console.KeyAvailable) return false;
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) {
                    quit = true;
                }
            } catch (InvalidOperationException) {
                // Input redirected; ignore.
            }
            return true;
        }

        private void Draw() {
            Console.Clear();
            Console.WriteLine("Stackoverflow Skyscraper - Leaderboard");
            if (!Console.IsInputRedirected) {
                Console.WriteLine("Live Leaderboard (press Q or Esc to close)");
            } else {
                Console.WriteLine("Live Leaderboard");
            }
            Console.WriteLine(new string('=', 50));
            var topScores = scoreboard.GetTopScores(EntriesToDisplay);
            Console.WriteLine("Top Levels");
            if (topScores.Count == 0) {
                Console.WriteLine("  (no runs recorded)");
            } else {
                for (int i = 0; i < topScores.Count; i++) {
                    var entry = topScores[i];
                    Console.WriteLine($"  {i + 1,2}. {entry.Initials,-3}  {entry.Score,4} lvls  {FormatTime(entry.RunTime)}");
                }
            }

            Console.WriteLine();

            var fastest = scoreboard.GetFastestRuns(EntriesToDisplay);
            Console.WriteLine("Fastest Runs");
            if (fastest.Count == 0) {
                Console.WriteLine("  (no completed runs)");
            } else {
                for (int i = 0; i < fastest.Count; i++) {
                    var entry = fastest[i];
                    Console.WriteLine($"  {i + 1,2}. {entry.Initials,-3}  {FormatTime(entry.RunTime),8}  {entry.Score,4} lvls");
                }
            }
        }

        private static string FormatTime(TimeSpan span) {
            if (span < TimeSpan.Zero)
                return span.TotalHours >= 1 ? "--:--:--" : "--:--.--";
            if (span == TimeSpan.Zero)
                return span.TotalHours >= 1 ? "00:00:00" : "00:00.00";
            return span.TotalHours >= 1 ? span.ToString(@"hh\:mm\:ss") : span.ToString(@"mm\:ss\.ff");
        }
    }
}
