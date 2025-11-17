using System;
using System.Runtime.CompilerServices;

namespace stackoverflow_minigame
{
    internal static class Diagnostics
    {
        // Events for reporting diagnostics messages.
        // Subscribers can listen to these events to handle diagnostics output.
        // If no subscribers are present, messages are enqueued to the Tracing system.
        // Each event provides a string message with context about the diagnostic.
        // FailureReported is invoked for critical errors that may affect game functionality.
        // WarningReported is invoked for non-critical issues that may impact user experience.
        // InfoReported is invoked for informational messages about game state or actions.
        public static event Action<string>? FailureReported;
        public static event Action<string>? WarningReported;
        public static event Action<string>? InfoReported;

        public static void ReportFailure(string message, Exception? ex = null, [CallerMemberName] string? caller = null)
        {
            string prefix = BuildPrefix(message, caller);
            if (ex != null)
            {
                prefix = $"{prefix} ({ex.GetType().Name}: {ex.Message})";
            }
            if (FailureReported != null)
            {
                FailureReported.Invoke(prefix);
            }
            else
            {
                Tracing.Enqueue(prefix);
            }
        }
        public static void ReportWarning(string message, [CallerMemberName] string? caller = null)
        {
            string prefix = BuildPrefix(message, caller);
            if (WarningReported != null)
            {
                WarningReported.Invoke(prefix);
            }
            else
            {
                Tracing.Enqueue(prefix);
            }
        }

        public static void ReportInfo(string message, [CallerMemberName] string? caller = null)
        {
            string prefix = BuildPrefix(message, caller);
            if (InfoReported != null)
            {
                InfoReported.Invoke(prefix);
            }
            else
            {
                Tracing.Enqueue(prefix);
            }
        }

        private static string BuildPrefix(string message, string? caller) =>
            caller != null ? $"{caller}: {message}" : message;
    }
}
