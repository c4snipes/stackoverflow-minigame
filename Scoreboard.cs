using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace stackoverflow_minigame {
    class ScoreEntry {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Initials { get; set; } = "AAA";
        public int Score { get; set; }
        public float MaxAltitude { get; set; }
        public long RunTimeTicks { get; set; }
        public bool Victory { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public TimeSpan RunTime => TimeSpan.FromTicks(RunTimeTicks);
        public ScoreEntry Clone() => (ScoreEntry)MemberwiseClone();
    }

    class Scoreboard {
        public const string DefaultFileName = "scoreboard.jsonl";

        private readonly string filePath;
        private readonly List<ScoreEntry> entries = new();
        private readonly object sync = new();
        private readonly JsonSerializerOptions jsonOptions = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public Scoreboard(string filePath) {
            this.filePath = filePath;
            EnsureFileExists();
            entries.AddRange(ReadEntriesFromDisk());
        }

        public static string ResolveDefaultPath() {
            string? overridePath = Environment.GetEnvironmentVariable("STACKOVERFLOW_SCOREBOARD");
            if (!string.IsNullOrWhiteSpace(overridePath)) {
                return Path.GetFullPath(overridePath);
            }
            string baseDir = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            string currentDir = Directory.GetCurrentDirectory();

            string? located = FindUpwards(new[] { baseDir, currentDir }, DefaultFileName, FindFileUpwards);
            if (!string.IsNullOrEmpty(located)) return located;

            string? gitRoot = FindUpwards(new[] { baseDir, currentDir }, ".git", FindDirectoryUpwards);
            if (!string.IsNullOrEmpty(gitRoot)) {
                return Path.Combine(gitRoot, DefaultFileName);
            }

            return Path.Combine(baseDir, DefaultFileName);
        }

        private static string? FindUpwards(IEnumerable<string> searchPaths, string target, Func<string, string, string?> finder) {
            foreach (var path in searchPaths.Distinct()) {
                var result = finder(path, target);
                if (!string.IsNullOrEmpty(result)) return result;
            }
            return null;
        }

        public void RecordRun(string initials, int score, float maxAltitude, TimeSpan runTime, bool victory) {
            var entry = new ScoreEntry {
                Initials = initials,
                Score = score,
                MaxAltitude = maxAltitude,
                RunTimeTicks = runTime.Ticks,
                Victory = victory,
                TimestampUtc = DateTime.UtcNow
            };

            RecordRun(entry);
        }

        public void RecordRun(ScoreEntry entry) {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            lock (sync) {
                AppendEntry(entry);
                entries.Add(entry.Clone());
            }
        }

        public IReadOnlyList<ScoreEntry> GetTopScores(int count) {
            lock (sync) {
                var snapshot = RefreshEntriesFromDisk();
                return snapshot
                    .OrderByDescending(e => e.Score)
                    .ThenBy(e => e.RunTimeTicks)
                    .Take(count)
                    .Select(e => e.Clone())
                    .ToList();
            }
        }

        public IReadOnlyList<ScoreEntry> GetFastestRuns(int count) {
            lock (sync) {
                var snapshot = RefreshEntriesFromDisk();
                return snapshot
                    .Where(e => e.RunTimeTicks > 0)
                    .OrderBy(e => e.RunTimeTicks)
                    .ThenByDescending(e => e.Score)
                    .Take(count)
                    .Select(e => e.Clone())
                    .ToList();
            }
        }

        private void EnsureFileExists() {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }
            if (!File.Exists(filePath)) {
                using FileStream _ = File.Create(filePath);
            }
        }

        private void AppendEntry(ScoreEntry entry) {
            string payload = JsonSerializer.Serialize(entry, jsonOptions);
            using FileStream stream = new(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using StreamWriter writer = new(stream, Encoding.UTF8);
            writer.WriteLine(payload);
        }

        private List<ScoreEntry> RefreshEntriesFromDisk() {
            var fresh = ReadEntriesFromDisk();
            entries.Clear();
            entries.AddRange(fresh);
            return entries;
        }

        private List<ScoreEntry> ReadEntriesFromDisk() {
            EnsureFileExists();
            string[] rawLines = File.ReadAllLines(filePath);
            Dictionary<string, ScoreEntry> merged = new();

            foreach (string line in rawLines) {
                if (IsConflictMarker(line)) continue;
                ScoreEntry? parsed = ParseLine(line);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.Id)) continue;
                merged[parsed.Id] = parsed;
            }

            return merged.Values.ToList();
        }

        private ScoreEntry? ParseLine(string line) {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) {
                return null;
            }

            try {
                return JsonSerializer.Deserialize<ScoreEntry>(trimmed, jsonOptions);
            } catch (JsonException ex) {
                Diagnostics.ReportFailure("Failed to parse scoreboard entry.", ex);
                LogCorruptLine(line);
                return null;
            }
        }

        private static bool IsConflictMarker(string line) =>
            line.StartsWith("<<<<<<<") || line.StartsWith("=======") || line.StartsWith(">>>>>>>");

        private void LogCorruptLine(string line) {
            try {
                string corruptPath = filePath + ".corrupt";
                using StreamWriter writer = new(corruptPath, append: true, Encoding.UTF8);
                writer.WriteLine(line);
            } catch (IOException ex) {
                Diagnostics.ReportFailure("Failed to log corrupt scoreboard entry.", ex);
            }
        }

        private static string? FindFileUpwards(string start, string fileName) {
            string? current = start;
            while (!string.IsNullOrEmpty(current)) {
                string candidate = Path.Combine(current, fileName);
                if (File.Exists(candidate)) return candidate;
                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == null || parent == current) break;
                current = parent;
            }
            return null;
        }

        private static string? FindDirectoryUpwards(string start, string directoryName) {
            string? current = start;
            while (!string.IsNullOrEmpty(current)) {
                string candidate = Path.Combine(current, directoryName);
                if (Directory.Exists(candidate)) return current;
                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == null || parent == current) break;
                current = parent;
            }
            return null;
        }
    }
}
