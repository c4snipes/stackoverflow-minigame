using System;
using System.Collections.Generic;
using System.Threading;

namespace stackoverflow_minigame
{
    // Displays the leaderboard for the Stackoverflow Skyscraper game.
    class LeaderboardViewer
    {
        private readonly Scoreboard scoreboard;
        private const int EntriesToDisplay = 8;
        private const int RefreshIntervalMs = 1000;
        private bool layoutInitialized;
        private int topSectionRow;
        private int fastestSectionRow;

        public LeaderboardViewer()
        {
            scoreboard = new Scoreboard(Scoreboard.ResolveDefaultPath());
        }

        public void Run(bool embedded = false)
        {
            Console.Title = "Stackoverflow Skyscraper - Leaderboard";
            bool singlePass = embedded ? false : Console.IsOutputRedirected;
            bool cursorHidden = false;
            bool shouldRestoreCursor = !singlePass;
            if (shouldRestoreCursor)
            {
                try
                {
                    cursorHidden = Console.CursorVisible;
                    Console.CursorVisible = false;
                }
                catch
                {
                    shouldRestoreCursor = false;
                }
            }

            layoutInitialized = false;
            try
            {
                do
                {
                    bool success = Draw();
                    if (!success || singlePass)
                    {
                        break;
                    }
                } while (!WaitOrQuit());
            }
            finally
            {
                if (shouldRestoreCursor)
                {
                    try
                    {
                        Console.CursorVisible = cursorHidden;
                        Console.WriteLine();
                    }
                    catch
                    {
                        // ignore cursor restore failures
                    }
                }
            }
        }

        // Waits for the refresh interval or a quit key press.
        // Returns true if a quit key was pressed.
        // <returns>True if a quit key was pressed; otherwise, false.</returns>

        private bool WaitOrQuit()
        {
            if (Console.IsInputRedirected)
            {
                Thread.Sleep(RefreshIntervalMs);
                return false;
            }
            const int pollIntervalMs = 100;
            int elapsed = 0;
            while (elapsed < RefreshIntervalMs)
            {
                if (TryReadQuitKey(out bool quit) && quit)
                {
                    return true;
                }
                Thread.Sleep(pollIntervalMs);
                elapsed += pollIntervalMs;
            }
            return false;
        }

        private bool TryReadQuitKey(out bool quit)
        {
            quit = false;
            try
            {
                if (Console.IsInputRedirected || !Console.KeyAvailable) return false;
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                {
                    quit = true;
                }
            }
            catch (InvalidOperationException)
            {
                // Input redirected; ignore.
            }
            return true;
        }

        private bool Draw()
        {
            int consoleWidth = ConsoleSafe.GetBufferWidth(80);
            bool canPosition = !Console.IsOutputRedirected && consoleWidth > 0;
            if (!layoutInitialized || !canPosition)
            {
                Console.Clear();
                layoutInitialized = canPosition;
                Console.WriteLine("Stackoverflow Skyscraper - Leaderboard");
                Console.WriteLine("Remote feed: https://stackoverflow-minigame.fly.dev/scoreboard");
                Console.WriteLine(canPosition ? "Live Leaderboard (press Q or Esc to close)" : "Live Leaderboard");
                Console.WriteLine(new string('=', 50));
                if (canPosition)
                {
                    topSectionRow = Console.CursorTop;
                    int blockHeight = 1 + EntriesToDisplay + 1;
                    fastestSectionRow = topSectionRow + blockHeight;
                }
            }

            if (!TryFetchScores(() => scoreboard.GetTopScores(EntriesToDisplay), "top scores", out var topScores, out var topError))
            {
                WriteSection(canPosition, topSectionRow, "Top Levels", new List<ScoreEntry>(), consoleWidth, topError ?? string.Empty);
                Environment.ExitCode = 1;
                return false;
            }

            if (!TryFetchScores(() => scoreboard.GetFastestRuns(EntriesToDisplay), "fastest runs", out var fastest, out var fastError))
            {
                WriteSection(canPosition, fastestSectionRow, "Fastest Runs", new List<ScoreEntry>(), consoleWidth, fastError ?? string.Empty);
                Environment.ExitCode = 1;
                return false;
            }

            if (!layoutInitialized)
            {
                Console.WriteLine("Top Levels");
                RenderSectionLines(topScores, consoleWidth, false);
                Console.WriteLine();
                Console.WriteLine("Fastest Runs");
                RenderSectionLines(fastest, consoleWidth, true);
                return true;
            }

            WriteSection(true, topSectionRow, "Top Levels", topScores, consoleWidth, null);
            WriteSection(true, fastestSectionRow, "Fastest Runs", fastest, consoleWidth, null);
            return true;
        }
        // Writes a section of the leaderboard at a specified console position.
        /// <summary>
        /// Writes a section of the leaderboard at a specified console position.
        /// </summary>
        private void WriteSection(bool usePositioning, int startRow, string title, IReadOnlyList<ScoreEntry> scores, int consoleWidth, string? errorMessage)
        {
            if (!usePositioning) return;
            int row = startRow;
            bool fastest = title.Contains("Fastest", StringComparison.OrdinalIgnoreCase);
            WriteLineAt(row++, title, consoleWidth);
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                WriteLineAt(row++, $"  (unable to load scoreboard: {errorMessage})", consoleWidth);
                row = PadRemainingRows(row, consoleWidth);
                return;
            }
            for (int i = 0; i < EntriesToDisplay; i++)
            {
                string line = FormatLeaderboardLine(scores, i, fastest);
                WriteLineAt(row++, line, consoleWidth);
            }
            WriteLineAt(row, string.Empty, consoleWidth);
        }
// Pads the remaining rows in a section with empty entries.
        private int PadRemainingRows(int row, int consoleWidth)
        {
            for (int i = 0; i < EntriesToDisplay; i++)
            {
                WriteLineAt(row++, "  --", consoleWidth);
            }
            WriteLineAt(row, string.Empty, consoleWidth);
            return row;
        }

        private void WriteLineAt(int row, string text, int consoleWidth)
        {
            string padded = text.Length > consoleWidth ? text[..consoleWidth] : text.PadRight(consoleWidth);
            if (ConsoleSafe.TrySetCursorPosition(0, row))
            {
                Console.Write(padded);
            }
        }
//
        private void RenderSectionLines(IReadOnlyList<ScoreEntry> scores, int consoleWidth, bool fastest)
        {
            if (scores.Count == 0)
            {
                Console.WriteLine("  (no runs recorded)");
                return;
            }
            for (int i = 0; i < scores.Count; i++)
            {
                string line = FormatLeaderboardLine(scores, i, fastest);
                Console.WriteLine(line.Length > consoleWidth ? line[..consoleWidth] : line);
            }
        }

        private static string FormatLeaderboardLine(IReadOnlyList<ScoreEntry> scores, int index, bool fastest)
        {
            if (index >= scores.Count)
            {
                return "  --";
            }

            var entry = scores[index];
            return fastest
                ? $"  {index + 1,2}. {entry.Initials,-3}  {TimeFormatting.FormatDuration(entry.RunTime),12}  {entry.Score,4} lvls"
                : $"  {index + 1,2}. {entry.Initials,-3}  {entry.Score,4} lvls  {TimeFormatting.FormatDuration(entry.RunTime)}";
        }

        private static void PrintScoreboardError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                Console.WriteLine("  (unable to load scoreboard)");
            }
            else
            {
                Console.WriteLine($"  (unable to load scoreboard: {error})");
            }
        }

        private static bool TryFetchScores(Func<IReadOnlyList<ScoreEntry>> fetch, string label, out IReadOnlyList<ScoreEntry> scores, out string? errorMessage)
        {
            try
            {
                scores = fetch();
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.ReportFailure($"Leaderboard viewer failed to read {label}.", ex);
                scores = Array.Empty<ScoreEntry>();
                errorMessage = ex.Message;
                return false;
            }
            // End of TryFetchScores
        }
    }
}
