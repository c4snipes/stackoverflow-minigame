using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace stackoverflow_minigame
{
    internal static class Tracing
    {
        private const int MaxQueueSize = 2048;
        private const int FileRetryCooldownSeconds = 5;

        private static readonly BlockingCollection<string> queue =
            new(new ConcurrentQueue<string>(), MaxQueueSize);
        private static readonly Thread worker;
        private static readonly string logPath =
            Path.Combine(AppContext.BaseDirectory ?? Directory.GetCurrentDirectory(), "trace.log");
        private static DateTime nextFileRetryUtc = DateTime.MinValue;
        private static bool disposed;

        static Tracing()
        {
            worker = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "DiagnosticsTracer"
            };
            worker.Start();

            AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
            Console.CancelKeyPress += (_, _) => Dispose();
        }

        private static void ProcessQueue()
        {
            StreamWriter? fileWriter = null;
            try
            {
                foreach (string message in queue.GetConsumingEnumerable())
                {
                    fileWriter ??= TryOpenWriter();

                    try
                    {
                        Console.Error.WriteLine(message);
                    }
                    catch
                    {
                        // Ignore console errors; diagnostics shouldn't crash the game.
                    }

                    if (fileWriter != null)
                    {
                        try
                        {
                            fileWriter.WriteLine(message);
                        }
                        catch
                        {
                            fileWriter.Dispose();
                            fileWriter = null;
                            nextFileRetryUtc = DateTime.UtcNow.AddSeconds(FileRetryCooldownSeconds);
                        }
                    }
                }
            }
            finally
            {
                fileWriter?.Dispose();
            }
        }

        private static StreamWriter? TryOpenWriter()
        {
            if (DateTime.UtcNow < nextFileRetryUtc)
            {
                return null;
            }

            try
            {
                var writer = new StreamWriter(
                    new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                    Encoding.UTF8)
                {
                    AutoFlush = true
                };
                return writer;
            }
            catch
            {
                nextFileRetryUtc = DateTime.UtcNow.AddSeconds(FileRetryCooldownSeconds);
                return null;
            }
        }

        public static void Enqueue(string message)
        {
            if (disposed)
            {
                return;
            }

            try
            {
                if (!queue.TryAdd(message))
                {
                    // Drop the oldest entry to make room for the latest event.
                    queue.TryTake(out _);
                    queue.TryAdd(message);
                }
            }
            catch (InvalidOperationException)
            {
                // The queue has been marked as complete for adding; ignore.
            }
        }

        public static void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            queue.CompleteAdding();
            if (!worker.Join(TimeSpan.FromSeconds(5)))
            {
                Console.Error.WriteLine("Warning: Tracing worker thread did not terminate within 5 seconds.");
            }
        }
    }
}
