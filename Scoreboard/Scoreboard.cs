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

    internal class Scoreboard
    {
        public const string DefaultFileName = "scoreboard.jsonl";

        private readonly string filePath;
        private readonly List<ScoreEntry> entries = new();
        private long lastReadPosition;
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

        public Scoreboard(string filePath)
        {
            this.filePath = filePath;
            EnsureFileExists();
            ReloadAllEntries();
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
                else if (entries.Any(e => e.Id == entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString("N");
                }
                var storedEntry = entry.Clone();
                if (TryAppendEntry(storedEntry))
                {
                    ApplyEntry(storedEntry);
                }
            }
        }

        public IReadOnlyList<ScoreEntry> GetTopScores(int count)
        {
            lock (sync)
            {
                var snapshot = RefreshEntriesFromDisk();
                return snapshot
                    .OrderByDescending(e => e.Level)
                    .ThenBy(e => e.RunTimeTicks)
                    .Take(count)
                    .Select(e => e.Clone())
                    .ToList();
            }
        }

        public IReadOnlyList<ScoreEntry> GetFastestRuns(int count)
        {
            lock (sync)
            {
                var snapshot = RefreshEntriesFromDisk();
                return snapshot
                    .Where(e => e.RunTimeTicks > 0 && e.Level > 0)
                    .OrderBy(e => e.RunTimeTicks)
                    .ThenByDescending(e => e.Level)
                    .Take(count)
                    .Select(e => e.Clone())
                    .ToList();
            }
        }

        private void EnsureFileExists()
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (!File.Exists(filePath))
            {
                using FileStream _ = File.Create(filePath);
            }
        }

        private bool TryAppendEntry(ScoreEntry entry)
        {
            string payload = JsonSerializer.Serialize(entry, jsonOptions);
            try
            {
                using FileStream stream = new(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using StreamWriter writer = new(stream, Encoding.UTF8);
                writer.WriteLine(payload);
                lastReadPosition = stream.Position;
                TryDispatchPayload(payload);
                return true;
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to append scoreboard entry.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Diagnostics.ReportFailure("Scoreboard file is not writable.", ex);
            }
            return false;
        }

        private List<ScoreEntry> RefreshEntriesFromDisk()
        {
            ReadIncrementalEntries();
            return entries;
        }

        private void ReadIncrementalEntries()
        {
            EnsureFileExists();
            FileInfo info = new(filePath);
            long currentLength = info.Exists ? info.Length : 0;
            if (currentLength == 0)
            {
                entries.Clear();
                lastReadPosition = 0;
                return;
            }

            if (currentLength < lastReadPosition)
            {
                ReloadAllEntries();
                return;
            }

            if (currentLength == lastReadPosition)
            {
                return;
            }

            try
            {
                using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(lastReadPosition, SeekOrigin.Begin);
                using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (IsConflictMarker(line))
                    {
                        continue;
                    }

                    ScoreEntry? parsed = ParseLine(line);
                    if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
                    {
                        continue;
                    }

                    ApplyEntry(parsed);
                }
                reader.DiscardBufferedData();
                lastReadPosition = stream.Seek(0, SeekOrigin.Current);
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to read incremental scoreboard entries.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Diagnostics.ReportFailure("Scoreboard file is not readable.", ex);
            }
        }

        private void ReloadAllEntries()
        {
            entries.Clear();
            try
            {
                foreach (string line in File.ReadLines(filePath))
                {
                    if (IsConflictMarker(line))
                    {
                        continue;
                    }

                    ScoreEntry? parsed = ParseLine(line);
                    if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id))
                    {
                        continue;
                    }

                    ApplyEntry(parsed);
                }
                FileInfo info = new(filePath);
                lastReadPosition = info.Exists ? info.Length : 0;
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to reload scoreboard from disk.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Diagnostics.ReportFailure("Scoreboard file is not readable.", ex);
            }
        }

        private void ApplyEntry(ScoreEntry entry)
        {
            int existingIndex = entries.FindIndex(e => e.Id == entry.Id);
            if (existingIndex >= 0)
            {
                entries[existingIndex] = entry;
            }
            else
            {
                entries.Add(entry);
            }
        }

        private ScoreEntry? ParseLine(string line)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ScoreEntry>(trimmed, jsonOptions);
            }
            catch (JsonException ex)
            {
                Diagnostics.ReportFailure("Failed to parse scoreboard entry.", ex);
                LogCorruptLine(line);
                return null;
            }
        }

        private static bool IsConflictMarker(string line) =>
            line.StartsWith("<<<<<<<") || line.StartsWith("=======") || line.StartsWith(">>>>>>>");

        private void LogCorruptLine(string line)
        {
            try
            {
                string corruptPath = filePath + ".corrupt";
                using StreamWriter writer = new(corruptPath, append: true, Encoding.UTF8);
                writer.WriteLine(line);
            }
            catch (IOException ex)
            {
                Diagnostics.ReportFailure("Failed to log corrupt scoreboard entry.", ex);
            }
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
