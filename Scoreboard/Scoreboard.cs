using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace stackoverflow_minigame
{
    internal class ScoreEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Initials { get; set; } = "AAA";
        [JsonPropertyName("level")]
        public int Level { get; set; }
        public float MaxAltitude { get; set; }
        public long RunTimeTicks { get; set; }
        public bool Victory { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public TimeSpan RunTime => TimeSpan.FromTicks(RunTimeTicks);
        public ScoreEntry Clone() => (ScoreEntry)MemberwiseClone();
    }

    internal class GlobalStats
    {
        public int TotalPlayers { get; set; }
        public int TotalRuns { get; set; }
        public int AverageLevel { get; set; }
        public int HighestLevel { get; set; }
        public long FastestTimeTicks { get; set; }
        public string TopPlayer { get; set; } = "N/A";
        public string FastestPlayer { get; set; } = "N/A";

        [JsonIgnore]
        public TimeSpan FastestTime => TimeSpan.FromTicks(FastestTimeTicks);
    }

    internal class Scoreboard
    {
        public const string DefaultFileName = "scoreboard.db";

        private readonly string dbPath;
        private readonly object sync = new();
        private readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private HttpClient? dispatchClient;
        private Uri? dispatchUri;
        private string dispatchEventType = "scoreboard-entry";
        private HttpClient? webhookClient;
        private Uri? webhookUri;
        private string? webhookSecret;
        private static readonly JsonSerializerOptions dispatchJsonOptions = new()
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public Scoreboard(string dbPath)
        {
            this.dbPath = dbPath;
            InitializeDatabase();
            InitializeDispatchers();
        }

        public static string ResolveDefaultPath()
        {
            string? overridePath = Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }
            string baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            string currentDir = Directory.GetCurrentDirectory();

            string? located = FindUpwards(new[] { baseDir, currentDir }, DefaultFileName, FindFileUpwards);
            if (!string.IsNullOrEmpty(located))
            {
                return located;
            }

            string? gitRoot = FindUpwards(new[] { baseDir, currentDir }, ".git", FindDirectoryUpwards);
            return !string.IsNullOrEmpty(gitRoot) ? Path.Combine(gitRoot, DefaultFileName) : Path.Combine(baseDir, DefaultFileName);
        }

        private static string? FindUpwards(IEnumerable<string> searchPaths, string target, Func<string, string, string?> finder)
        {
            foreach (var path in searchPaths.Distinct())
            {
                var result = finder(path, target);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }
            }
            return null;
        }

        private void InitializeDatabase()
        {
            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS scoreboard (
                    id TEXT PRIMARY KEY,
                    initials TEXT NOT NULL,
                    level INTEGER NOT NULL,
                    max_altitude REAL NOT NULL,
                    run_time_ticks INTEGER NOT NULL,
                    victory INTEGER NOT NULL,
                    timestamp_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_level ON scoreboard(level DESC, run_time_ticks ASC);
                CREATE INDEX IF NOT EXISTS idx_run_time ON scoreboard(run_time_ticks ASC);
                CREATE INDEX IF NOT EXISTS idx_timestamp ON scoreboard(timestamp_utc);
            ";
            command.ExecuteNonQuery();
        }

        public void RecordRun(string initials, int score, float maxAltitude, TimeSpan runTime, bool victory)
        {
            var entry = new ScoreEntry
            {
                Initials = ProfanityFilter.FilterInitials(initials),
                Level = score,
                MaxAltitude = maxAltitude,
                RunTimeTicks = runTime.Ticks,
                Victory = victory,
                TimestampUtc = DateTime.UtcNow
            };

            RecordRun(entry);
        }

        public void RecordRun(ScoreEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (sync)
            {
                entry.Initials = ProfanityFilter.FilterInitials(entry.Initials ?? "AAA");
                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString("N");
                }

                var storedEntry = entry.Clone();
                if (TryInsertEntry(storedEntry))
                {
                    // Dispatch webhook/GitHub event
                    string payload = JsonSerializer.Serialize(storedEntry, jsonOptions);
                    TryDispatchPayload(payload);
                }
            }
        }

        private bool TryInsertEntry(ScoreEntry entry)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT OR REPLACE INTO scoreboard (id, initials, level, max_altitude, run_time_ticks, victory, timestamp_utc)
                    VALUES ($id, $initials, $level, $maxAltitude, $runTimeTicks, $victory, $timestampUtc)
                ";
                command.Parameters.AddWithValue("$id", entry.Id);
                command.Parameters.AddWithValue("$initials", entry.Initials);
                command.Parameters.AddWithValue("$level", entry.Level);
                command.Parameters.AddWithValue("$maxAltitude", entry.MaxAltitude);
                command.Parameters.AddWithValue("$runTimeTicks", entry.RunTimeTicks);
                command.Parameters.AddWithValue("$victory", entry.Victory ? 1 : 0);
                command.Parameters.AddWithValue("$timestampUtc", entry.TimestampUtc.ToString("O"));

                command.ExecuteNonQuery();
                return true;
            }
            catch (SqliteException ex)
            {
                Diagnostics.ReportFailure("Failed to insert scoreboard entry.", ex);
                return false;
            }
        }

        public IReadOnlyList<ScoreEntry> GetTopScores(int count)
        {
            lock (sync)
            {
                return GetTopScores(count, null);
            }
        }

        public IReadOnlyList<ScoreEntry> GetFastestRuns(int count)
        {
            lock (sync)
            {
                return GetFastestRuns(count, null);
            }
        }

        public IReadOnlyList<ScoreEntry> GetTopScores(int count, DateTime? since)
        {
            lock (sync)
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath}");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    if (since.HasValue)
                    {
                        command.CommandText = @"
                            SELECT id, initials, level, max_altitude, run_time_ticks, victory, timestamp_utc
                            FROM scoreboard
                            WHERE timestamp_utc >= $since
                            ORDER BY level DESC, run_time_ticks ASC
                            LIMIT $count
                        ";
                        command.Parameters.AddWithValue("$since", since.Value.ToString("O"));
                    }
                    else
                    {
                        command.CommandText = @"
                            SELECT id, initials, level, max_altitude, run_time_ticks, victory, timestamp_utc
                            FROM scoreboard
                            ORDER BY level DESC, run_time_ticks ASC
                            LIMIT $count
                        ";
                    }
                    command.Parameters.AddWithValue("$count", count);

                    return ReadEntries(command);
                }
                catch (SqliteException ex)
                {
                    Diagnostics.ReportFailure("Failed to get top scores.", ex);
                    return new List<ScoreEntry>();
                }
            }
        }

        public IReadOnlyList<ScoreEntry> GetFastestRuns(int count, DateTime? since)
        {
            lock (sync)
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath}");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    if (since.HasValue)
                    {
                        command.CommandText = @"
                            SELECT id, initials, level, max_altitude, run_time_ticks, victory, timestamp_utc
                            FROM scoreboard
                            WHERE run_time_ticks > 0 AND level > 0 AND timestamp_utc >= $since
                            ORDER BY run_time_ticks ASC, level DESC
                            LIMIT $count
                        ";
                        command.Parameters.AddWithValue("$since", since.Value.ToString("O"));
                    }
                    else
                    {
                        command.CommandText = @"
                            SELECT id, initials, level, max_altitude, run_time_ticks, victory, timestamp_utc
                            FROM scoreboard
                            WHERE run_time_ticks > 0 AND level > 0
                            ORDER BY run_time_ticks ASC, level DESC
                            LIMIT $count
                        ";
                    }
                    command.Parameters.AddWithValue("$count", count);

                    return ReadEntries(command);
                }
                catch (SqliteException ex)
                {
                    Diagnostics.ReportFailure("Failed to get fastest runs.", ex);
                    return new List<ScoreEntry>();
                }
            }
        }

        public GlobalStats GetGlobalStats(DateTime? since = null)
        {
            lock (sync)
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath}");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    if (since.HasValue)
                    {
                        command.CommandText = @"
                            SELECT
                                COUNT(DISTINCT initials) as total_players,
                                COUNT(*) as total_runs,
                                CAST(AVG(level) AS INTEGER) as average_level,
                                MAX(level) as highest_level,
                                MIN(CASE WHEN run_time_ticks > 0 THEN run_time_ticks END) as fastest_time
                            FROM scoreboard
                            WHERE timestamp_utc >= $since
                        ";
                        command.Parameters.AddWithValue("$since", since.Value.ToString("O"));
                    }
                    else
                    {
                        command.CommandText = @"
                            SELECT
                                COUNT(DISTINCT initials) as total_players,
                                COUNT(*) as total_runs,
                                CAST(AVG(level) AS INTEGER) as average_level,
                                MAX(level) as highest_level,
                                MIN(CASE WHEN run_time_ticks > 0 THEN run_time_ticks END) as fastest_time
                            FROM scoreboard
                        ";
                    }

                    using var reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        var stats = new GlobalStats
                        {
                            TotalPlayers = reader.GetInt32(0),
                            TotalRuns = reader.GetInt32(1),
                            AverageLevel = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            HighestLevel = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                            FastestTimeTicks = reader.IsDBNull(4) ? 0 : reader.GetInt64(4)
                        };

                        // Get top player
                        using var topCommand = connection.CreateCommand();
                        if (since.HasValue)
                        {
                            topCommand.CommandText = @"
                                SELECT initials
                                FROM scoreboard
                                WHERE timestamp_utc >= $since
                                GROUP BY initials
                                ORDER BY MAX(level) DESC
                                LIMIT 1
                            ";
                            topCommand.Parameters.AddWithValue("$since", since.Value.ToString("O"));
                        }
                        else
                        {
                            topCommand.CommandText = @"
                                SELECT initials
                                FROM scoreboard
                                GROUP BY initials
                                ORDER BY MAX(level) DESC
                                LIMIT 1
                            ";
                        }
                        var topResult = topCommand.ExecuteScalar();
                        stats.TopPlayer = topResult?.ToString() ?? "N/A";

                        // Get fastest player
                        using var fastestCommand = connection.CreateCommand();
                        if (since.HasValue)
                        {
                            fastestCommand.CommandText = @"
                                SELECT initials
                                FROM scoreboard
                                WHERE run_time_ticks > 0 AND timestamp_utc >= $since
                                ORDER BY run_time_ticks ASC
                                LIMIT 1
                            ";
                            fastestCommand.Parameters.AddWithValue("$since", since.Value.ToString("O"));
                        }
                        else
                        {
                            fastestCommand.CommandText = @"
                                SELECT initials
                                FROM scoreboard
                                WHERE run_time_ticks > 0
                                ORDER BY run_time_ticks ASC
                                LIMIT 1
                            ";
                        }
                        var fastestResult = fastestCommand.ExecuteScalar();
                        stats.FastestPlayer = fastestResult?.ToString() ?? "N/A";

                        return stats;
                    }

                    return new GlobalStats();
                }
                catch (SqliteException ex)
                {
                    Diagnostics.ReportFailure("Failed to get global stats.", ex);
                    return new GlobalStats();
                }
            }
        }

        private List<ScoreEntry> ReadEntries(SqliteCommand command)
        {
            var entries = new List<ScoreEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new ScoreEntry
                {
                    Id = reader.GetString(0),
                    Initials = reader.GetString(1),
                    Level = reader.GetInt32(2),
                    MaxAltitude = reader.GetFloat(3),
                    RunTimeTicks = reader.GetInt64(4),
                    Victory = reader.GetInt32(5) != 0,
                    TimestampUtc = DateTime.Parse(reader.GetString(6)).ToUniversalTime()
                };
                entries.Add(entry);
            }
            return entries;
        }

        private static string? WalkUpwards(string start, Func<string, string?> evaluator)
        {
            string? current = start;
            while (!string.IsNullOrEmpty(current))
            {
                string? result = evaluator(current);
                if (!string.IsNullOrEmpty(result))
                {
                    return result;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == null || parent == current)
                {
                    break;
                }

                current = parent;
            }
            return null;
        }

        private static string? FindFileUpwards(string start, string fileName) =>
            WalkUpwards(start, current =>
            {
                string candidate = Path.Combine(current, fileName);
                return File.Exists(candidate) ? candidate : null;
            });

        private static string? FindDirectoryUpwards(string start, string directoryName) =>
            WalkUpwards(start, current =>
            {
                string candidate = Path.Combine(current, directoryName);
                return Directory.Exists(candidate) ? current : null;
            });

        private static string? NormalizeEnvVariable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"').Trim('\'').Trim();
        }

        private void InitializeDispatchers()
        {
            InitializeGitHubDispatchClient();
            InitializeWebhookClient();
        }

        private void InitializeGitHubDispatchClient()
        {
            string? token = NormalizeEnvVariable(Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_DISPATCH_TOKEN"));
            string? repo = NormalizeEnvVariable(Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_REPO"));
            dispatchEventType = NormalizeEnvVariable(Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_DISPATCH_EVENT")) ?? "scoreboard-entry";
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(repo))
            {
                dispatchClient = null;
                dispatchUri = null;
                return;
            }

            string apiBase = NormalizeEnvVariable(Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_API_BASE")) ?? "https://api.github.com";
            string trimmedRepo = repo.Trim().Trim('/');
            string trimmedBase = apiBase.TrimEnd('/');
            if (!Uri.TryCreate($"{trimmedBase}/repos/{trimmedRepo}/dispatches", UriKind.Absolute, out Uri? uri))
            {
                dispatchClient = null;
                dispatchUri = null;
                Diagnostics.ReportFailure("Invalid GitHub dispatch URI. Check STACKOVERFLOW_SCOREBOARD_API_BASE/REPO.", new UriFormatException());
                return;
            }

            dispatchUri = uri;
            dispatchClient = new HttpClient();
            dispatchClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            dispatchClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("stackoverflow-minigame", "1.0"));
            dispatchClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token.Trim());
        }

        private void InitializeWebhookClient()
        {
            string? url = NormalizeEnvVariable(Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL"));
            webhookSecret = NormalizeEnvVariable(Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET"));
            if (string.IsNullOrWhiteSpace(url))
            {
                webhookClient = null;
                webhookUri = null;
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                Diagnostics.ReportFailure("Invalid STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL.", new UriFormatException());
                return;
            }

            webhookUri = uri;
            webhookClient = new HttpClient();
            webhookClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("stackoverflow-minigame", "1.0"));
        }

        private void TryDispatchPayload(string jsonLine)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonLine));
            if (dispatchClient != null && dispatchUri != null)
            {
                FireAndForget(() => DispatchScoreboardEntryAsync(encoded), "Scoreboard dispatch task failed.");
            }

            if (webhookClient != null && webhookUri != null)
            {
                FireAndForget(() => SendWebhookAsync(encoded, jsonLine), "Scoreboard webhook task failed.");
            }
        }

        private async Task DispatchScoreboardEntryAsync(string encodedLine)
        {
            if (dispatchClient == null || dispatchUri == null)
            {
                return;
            }

            var request = new DispatchRequest
            {
                EventType = dispatchEventType,
                ClientPayload = new DispatchClientPayload { LineBase64 = encodedLine }
            };

            string body = JsonSerializer.Serialize(request, dispatchJsonOptions);
            using StringContent content = new(body, Encoding.UTF8, "application/json");
            try
            {
                HttpResponseMessage response = await dispatchClient.PostAsync(dispatchUri, content).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Diagnostics.ReportFailure(
                        $"Scoreboard dispatch failed with {(int)response.StatusCode} {response.ReasonPhrase}.",
                        new InvalidOperationException(responseBody));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                Diagnostics.ReportFailure("Failed to dispatch scoreboard entry to GitHub.", ex);
            }
        }

        private sealed class DispatchRequest
        {
            [JsonPropertyName("event_type")]
            public string EventType { get; init; } = "scoreboard-entry";

            [JsonPropertyName("client_payload")]
            public DispatchClientPayload ClientPayload { get; init; } = new();
        }

        private sealed class DispatchClientPayload
        {
            [JsonPropertyName("line_b64")]
            public string LineBase64 { get; init; } = string.Empty;
        }

        private async Task SendWebhookAsync(string encodedLine, string rawLine)
        {
            if (webhookClient == null || webhookUri == null)
            {
                return;
            }

            var payload = new WebhookPayload
            {
                LineBase64 = encodedLine,
                Line = rawLine
            };

            string body = JsonSerializer.Serialize(payload, dispatchJsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, webhookUri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(webhookSecret))
            {
                request.Headers.Add("X-Scoreboard-Secret", webhookSecret);
            }

            try
            {
                HttpResponseMessage response = await webhookClient.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Diagnostics.ReportFailure(
                        $"Scoreboard webhook failed with {(int)response.StatusCode} {response.ReasonPhrase}.",
                        new InvalidOperationException(responseBody));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                Diagnostics.ReportFailure("Failed to call scoreboard webhook.", ex);
            }
        }

        private sealed class WebhookPayload
        {
            [JsonPropertyName("line_b64")]
            public string LineBase64 { get; init; } = string.Empty;

            [JsonPropertyName("line")]
            public string Line { get; init; } = string.Empty;
        }

        private static void FireAndForget(Func<Task> action, string description)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Diagnostics.ReportFailure(description, ex);
                }
            });
        }
    }
}
