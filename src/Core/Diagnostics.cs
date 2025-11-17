using System;
using System.Runtime.CompilerServices;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Centralized diagnostics system with event-based routing.
    /// Falls back to Tracing when no subscribers are active.
    /// </summary>
    internal static class Diagnostics
    {
        // Events fire to active subscribers; unsubscribed messages route to Tracing for persistence
        public static event Action<string>? FailureReported;
        public static event Action<string>? WarningReported;
        public static event Action<string>? InfoReported;

        /// <summary>
        /// Reports a critical failure that may affect game functionality.
        /// </summary>
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

        /// <summary>
        /// Reports a non-critical warning that may impact user experience.
        /// </summary>
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

        /// <summary>
        /// Reports informational messages about game state or actions.
        /// </summary>
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
