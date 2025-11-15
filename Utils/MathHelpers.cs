using System;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Provides common mathematical utility functions.
    /// </summary>
    internal static class MathHelpers
    {
        /// <summary>
        /// Performs linear interpolation between two integer values.
        /// </summary>
        /// <param name="from">The starting value.</param>
        /// <param name="to">The target value.</param>
        /// <param name="t">The interpolation factor (0.0 to 1.0).</param>
        /// <returns>The interpolated integer value.</returns>
        public static int LerpInt(int from, int to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return (int)MathF.Round(from + (to - from) * t);
        }

        /// <summary>
        /// Performs linear interpolation between two floating-point values.
        /// </summary>
        /// <param name="from">The starting value.</param>
        /// <param name="to">The target value.</param>
        /// <param name="t">The interpolation factor.</param>
        /// <returns>The interpolated float value.</returns>
        public static float LerpFloat(float from, float to, float t)
        {
            return from + (to - from) * t;
        }
    }
}
