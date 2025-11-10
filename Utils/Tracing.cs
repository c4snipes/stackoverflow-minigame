using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace stackoverflow_minigame {
    static class Tracing {
        private static readonly BlockingCollection<string> queue = new(new ConcurrentQueue<string>());
        private static readonly Thread worker;
        private static readonly string logPath = Path.Combine(AppContext.BaseDirectory ?? Directory.GetCurrentDirectory(), "trace.log");
        private static bool disposed;

        static Tracing() {
            worker = new Thread(ProcessQueue) {
                IsBackground = true,
                Name = "DiagnosticsTracer"
            };
            worker.Start();
        }

        private static void ProcessQueue() {
            StreamWriter? fileWriter = null;
            try {
                try {
                    fileWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8) {
                        AutoFlush = true
                    };
                } catch {
                    // If we can't open the log file, continue with stderr only.
                }

                foreach (string message in queue.GetConsumingEnumerable()) {
                    try {
                        Console.Error.WriteLine(message);
                    } catch {
                        // Ignore console errors; diagnostics shouldn't crash the game.
                    }

                    if (fileWriter != null) {
                        try {
                            fileWriter.WriteLine(message);
                        } catch {
                            fileWriter.Dispose();
                            fileWriter = null;
                        }
                    }
                }
            } finally {
                fileWriter?.Dispose();
            }
        }

        public static void Enqueue(string message) {
            if (disposed) return;
            try {
                queue.Add(message);
            } catch (InvalidOperationException) {
                // The queue has been marked as complete for adding; ignore.
            }
        }

        public static void Dispose() {
            if (disposed) return;
            disposed = true;
            queue.CompleteAdding();
            if (!worker.Join(TimeSpan.FromSeconds(5))) {
                Console.Error.WriteLine("Warning: Tracing worker thread did not terminate within 5 seconds.");
                // Optionally, you could call worker.Join() without a timeout here to wait indefinitely:
                // worker.Join();
            }
        }
    }
}
