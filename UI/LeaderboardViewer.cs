using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace stackoverflow_minigame
{
    // Displays the leaderboard for the Stackoverflow Skyscraper game.
    internal class LeaderboardViewer
    {
        private readonly Scoreboard scoreboard;
        private const int EntriesToDisplay = 10;
        private const int RefreshIntervalMs = 1000;
        private bool layoutInitialized;
        private int topSectionRow;
        private int statsSectionRow;
        private readonly HttpClient? remoteClient;
        private readonly Uri? remoteUri;
        private DateTime? timeFilter = null;
        private string timeFilterLabel = "All Time";

        public LeaderboardViewer()
        {
            scoreboard = new Scoreboard(Scoreboard.ResolveDefaultPath());
            remoteUri = ResolveRemoteUri();
            if (remoteUri != null)
            {
                remoteClient = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };
                remoteClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }

        public void Run(bool embedded = false)
        {
            bool singlePass = embedded ? false : Console.IsOutputRedirected;
            bool cursorHidden = false;
            bool shouldRestoreCursor = !singlePass;
            if (shouldRestoreCursor)
            {
                try
                {
#if WINDOWS
                                        cursorHidden = Console.CursorVisible;
                                        Console.CursorVisible = false;
#else
                    cursorHidden = false;
#endif
                }
                catch
                {
                    shouldRestoreCursor = false;
                }
            }

            layoutInitialized = false;
            // Console.Title is only supported on Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Console.Title = "Stackoverflow Skyscraper - Leaderboard";
                }
                catch
                {
                    // Ignore failures when the terminal does not expose a title.
                }
            }

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
#if WINDOWS
                Console.CursorVisible = cursorHidden;
#endif
                ConsoleSafe.WriteLine(string.Empty);
                try
                {
                    Console.CursorVisible = cursorHidden;
                    ConsoleSafe.WriteLine(string.Empty);
                }
                catch
                {
                    // ignore cursor restore failures
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
            int remaining = RefreshIntervalMs;
            while (remaining > 0)
            {
                if (TryReadQuitKey(out bool quit) && quit)
                {
                    return true;
                }
                int slice = Math.Min(remaining, 250);
                Thread.Sleep(slice);
                remaining -= slice;
            }
            return false;
        }
        // Tries to read a quit key from the console input.
        // <param name="quit">Outputs true if a quit key was pressed.</param>
        // <returns>True if a key was read; otherwise, false.</returns>

        private bool TryReadQuitKey(out bool quit)
        {
            quit = false;
            try
            {
                if (Console.IsInputRedirected || !Console.KeyAvailable)
                {
                    return false;
                }

                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
                {
                    quit = true;
                    return true;
                }

                // Handle time filter shortcuts
                switch (key.Key)
                {
                    case ConsoleKey.A:
                        timeFilter = null;
                        timeFilterLabel = "All Time";
                        layoutInitialized = false; // Force redraw
                        break;
                    case ConsoleKey.T:
                        timeFilter = DateTime.UtcNow.Date;
                        timeFilterLabel = "Today";
                        layoutInitialized = false;
                        break;
                    case ConsoleKey.W:
                        timeFilter = DateTime.UtcNow.AddDays(-7);
                        timeFilterLabel = "This Week";
                        layoutInitialized = false;
                        break;
                    case ConsoleKey.M:
                        timeFilter = DateTime.UtcNow.AddDays(-30);
                        timeFilterLabel = "This Month";
                        layoutInitialized = false;
                        break;
                }
            }
            catch (InvalidOperationException)
            {
                // Input redirected; ignore.
            }
            return true;
        }
        // Draws the leaderboard to the console.
        // <returns>True if drawing succeeded; otherwise, false.</returns>


        private bool Draw()
        {
            int consoleWidth = ConsoleSafe.GetBufferWidth(80);
            bool canPosition = !Console.IsOutputRedirected && consoleWidth > 0;
            if (!layoutInitialized || !canPosition)
            {
                try
                {
                    Console.Clear();
                }
                catch (IOException ex)
                {
                    Diagnostics.ReportFailure("Failed to clear the leaderboard console.", ex);
                }
                layoutInitialized = canPosition;
                ConsoleSafe.WriteLine("Stackoverflow Skyscraper - Leaderboard");
                if (remoteUri != null)
                {
                    ConsoleSafe.WriteLine($"Remote feed: {remoteUri}");
                }
                ConsoleSafe.WriteLine($"Time Range: {timeFilterLabel}");
                ConsoleSafe.WriteLine(canPosition ? "Live Leaderboard (Q/Esc=quit, T=today, W=week, M=month, A=all)" : "Live Leaderboard");
                ConsoleSafe.WriteLine(new string('=', 50));
                if (canPosition)
                {
                    statsSectionRow = Console.CursorTop;
                    int statsHeight = 4; // Title + 3 lines of stats
                    topSectionRow = statsSectionRow + statsHeight;
                }
            }

            IReadOnlyList<ScoreEntry> topScores = Array.Empty<ScoreEntry>();
            GlobalStats stats = new GlobalStats();
            string? topError = null;

            bool remoteSucceeded = TryFetchRemoteLeaderboard(out var remoteTop, out var remoteFast, out var remoteError);
            if (remoteSucceeded)
            {
                topScores = remoteTop;
            }
            else
            {
                if (remoteError is not null)
                {
                    ConsoleSafe.WriteLine($"[warning] Remote leaderboard unavailable: {remoteError}");
                }
                if (!TryFetchScores(() => scoreboard.GetTopScores(EntriesToDisplay, timeFilter), "top scores", out topScores, out topError))
                {
                    WriteSection(canPosition, topSectionRow, "Top Scores", Array.Empty<ScoreEntry>(), consoleWidth, topError ?? string.Empty);
                    Environment.ExitCode = 1;
                    return false;
                }

                // Get global stats
                stats = scoreboard.GetGlobalStats(timeFilter);
            }

            if (!layoutInitialized)
            {
                ConsoleSafe.WriteLine("Global Stats:");
                ConsoleSafe.WriteLine($"  Players: {stats.TotalPlayers}  |  Runs: {stats.TotalRuns}  |  Avg: {stats.AverageLevel}  |  Record: {stats.HighestLevel}  |  Top: {stats.TopPlayer}");
                ConsoleSafe.WriteLine(string.Empty);
                ConsoleSafe.WriteLine("Top Scores");
                RenderSectionLines(topScores, consoleWidth, false);
                return true;
            }

            WriteStatsSection(canPosition, statsSectionRow, stats, consoleWidth);
            WriteSection(true, topSectionRow, "Top Scores", topScores, consoleWidth, null);
            return true;
        }
        // Writes a section of the leaderboard at a specified console position.
        /// <summary>
        /// Writes a section of the leaderboard at a specified console position.
        /// </summary>
        /// <param name="usePositioning">Whether to use console positioning.</param>
        /// <param name="startRow">The starting row for the section.</param>
        /// <param name="title">The title of the section.</param>
        /// <param name="scores">The scores to display.</param>
        /// <param name="consoleWidth">The width of the console.</param>
        /// <param name="errorMessage">An optional error message to display.</param>
        /// <returns>Nothing.</returns>
        /// <remarks>If positioning is disabled, this method does nothing.</remarks>
        ///
        private void WriteSection(bool usePositioning, int startRow, string title, IReadOnlyList<ScoreEntry> scores, int consoleWidth, string? errorMessage)
        {
            if (!usePositioning)
            {
                return;
            }

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

        private void WriteStatsSection(bool usePositioning, int startRow, GlobalStats stats, int consoleWidth)
        {
            if (!usePositioning)
            {
                return;
            }

            int row = startRow;
            WriteLineAt(row++, "Global Stats:", consoleWidth);
            WriteLineAt(row++, $"  Players: {stats.TotalPlayers}  |  Runs: {stats.TotalRuns}  |  Avg Lvl: {stats.AverageLevel}  |  Record: {stats.HighestLevel}", consoleWidth);
            WriteLineAt(row++, $"  Fastest: {TimeFormatting.FormatDuration(stats.FastestTime)}  |  Top Player: {stats.TopPlayer}  |  Speed King: {stats.FastestPlayer}", consoleWidth);
            WriteLineAt(row, string.Empty, consoleWidth);
        }
        // Renders the lines of a leaderboard section.

        private void RenderSectionLines(IReadOnlyList<ScoreEntry> scores, int consoleWidth, bool fastest)
        {
            if (scores.Count == 0)
            {
                ConsoleSafe.WriteLine("  (no runs recorded)");
                return;
            }
            for (int i = 0; i < scores.Count; i++)
            {
                string line = FormatLeaderboardLine(scores, i, fastest);
                string display = line.Length > consoleWidth ? line[..consoleWidth] : line;
                ConsoleSafe.WriteLine(display);
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
                ? $"  {index + 1,2}. {entry.Initials,-3}  {TimeFormatting.FormatDuration(entry.RunTime),12}  {entry.Level,4} lvls"
                : $"  {index + 1,2}. {entry.Initials,-3}  {entry.Level,4} lvls  {TimeFormatting.FormatDuration(entry.RunTime)}";
        }

        private static void PrintScoreboardError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                ConsoleSafe.WriteLine("  (unable to load scoreboard)");
            }
            else
            {
                ConsoleSafe.WriteLine($"  (unable to load scoreboard: {error})");
            }
        }

        private bool TryFetchRemoteLeaderboard(out IReadOnlyList<ScoreEntry> topScores, out IReadOnlyList<ScoreEntry> fastestRuns, out string? error)
        {
            topScores = Array.Empty<ScoreEntry>();
            fastestRuns = Array.Empty<ScoreEntry>();
            error = null;
            if (remoteClient == null || remoteUri == null)
            {
                return false;
            }

            try
            {
                using HttpResponseMessage response = remoteClient.GetAsync(remoteUri).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    error = $"Remote feed returned {(int)response.StatusCode}";
                    return false;
                }

                using var stream = response.Content.ReadAsStream();
                var payload = JsonSerializer.Deserialize<RemoteLeaderboardResponse>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (payload == null)
                {
                    error = "Remote feed returned an empty payload.";
                    return false;
                }

                topScores = payload.TopLevels ?? (IReadOnlyList<ScoreEntry>)Array.Empty<ScoreEntry>();
                fastestRuns = payload.FastestRuns ?? (IReadOnlyList<ScoreEntry>)Array.Empty<ScoreEntry>();
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.ReportFailure("Failed to fetch the remote leaderboard.", ex);
                error = ex.Message;
                return false;
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

        private static Uri? ResolveRemoteUri()
        {
            string? raw = Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_REMOTE_URL");
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = "https://stackoverflow-minigame.fly.dev/scoreboard";
            }
            if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            {
                return uri;
            }
            Diagnostics.ReportFailure($"Invalid remote leaderboard URL: {raw}", new UriFormatException());
            return null;
        }

        private sealed class RemoteLeaderboardResponse
        {
            [JsonPropertyName("topLevels")]
            public List<ScoreEntry>? TopLevels { get; set; }

            [JsonPropertyName("fastestRuns")]
            public List<ScoreEntry>? FastestRuns { get; set; }
        }
    }
}
