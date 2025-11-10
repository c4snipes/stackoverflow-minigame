using System;
using System.Runtime.CompilerServices;

namespace stackoverflow_minigame
{
    static class Diagnostics
    {
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
