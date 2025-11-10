using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security;
using System.Threading;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Manages asynchronous console input handling, allowing for non-blocking key reads,
    /// pausing/resuming input listening, and safe operation in constrained environments.
    /// </summary>
    class Input : IDisposable
    {
        private readonly ConcurrentQueue<ConsoleKeyInfo> buffer = new();
        private readonly CancellationTokenSource cancellation = new();
        private readonly ManualResetEventSlim resumeSignal = new(true);
        private Thread? listener;
        private const int LISTENER_SHUTDOWN_TIMEOUT_MS = 1000;

        public bool SupportsInteractiveInput { get; }

        public Input()
        {
            SupportsInteractiveInput = ProbeForConsoleInput();
        }

        public void Start()
        {
            if (!SupportsInteractiveInput)
            {
                Diagnostics.ReportFailure("Input.Start skipped because interactive input is unavailable.");
                return;
            }
            if (listener != null) return;
            listener = new Thread(Listen)
            {
                IsBackground = true,
                Name = "ConsoleInputListener"
            };
            listener.Start();
        }

        public void Stop()
        {
            Thread? thread = listener;
            try
            {
                cancellation.Cancel();
                resumeSignal.Set();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore.
            }
            if (thread != null && thread.IsAlive)
            {
                if (!thread.Join(LISTENER_SHUTDOWN_TIMEOUT_MS))
                {
                    Diagnostics.ReportFailure("Input listener did not shut down within the timeout.");
                }
            }
            listener = null;
        }

        public bool TryReadKey(out ConsoleKeyInfo key) => buffer.TryDequeue(out key);

        public void ClearBuffer()
        {
            while (buffer.TryDequeue(out _)) { }
        }

        public IDisposable PauseListening()
        {
            resumeSignal.Reset();
            return new ResumeHandle(this);
        }

        private void ResumeListening()
        {
            resumeSignal.Set();
        }

        // Background loop that pumps Console input into a queue while respecting pause/cancellation signals.
        private void Listen()
        {
            var token = cancellation.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    resumeSignal.Wait(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                if (token.IsCancellationRequested) break;
                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true);
                        buffer.Enqueue(key);
                    }
                    else
                    {
                        if (token.WaitHandle.WaitOne(2))
                        {
                            break;
                        }
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Diagnostics.ReportFailure("Console input became unavailable while listening.", ex);
                    break;
                }
                catch (IOException ex)
                {
                    Diagnostics.ReportFailure("Console input read failed.", ex);
                    break;
                }
                catch (SecurityException ex)
                {
                    Diagnostics.ReportFailure("Console input is not permitted in this environment.", ex);
                    break;
                }
            }
        }

        private static bool ProbeForConsoleInput()
        {
            if (Console.IsInputRedirected) return false;
            try
            {
                _ = Console.KeyAvailable;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Diagnostics.ReportFailure("Console input probing failed.", ex);
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
            cancellation.Dispose();
            resumeSignal.Dispose();
        }

        private sealed class ResumeHandle : IDisposable
        {
            private readonly Input owner;
            private bool disposed;

            public ResumeHandle(Input owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.ResumeListening();
            }
        }
    }
}
