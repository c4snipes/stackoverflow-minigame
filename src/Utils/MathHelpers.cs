using System;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Common mathematical utility functions for interpolation and calculations.
    /// </summary>
    internal static class MathHelpers
    {
        /// <summary>
        /// Linear interpolation between two integer values.
        /// </summary>
        /// <param name="from">Starting value</param>
        /// <param name="to">Target value</param>
        /// <param name="t">Interpolation factor (0.0 to 1.0, clamped)</param>
        /// <returns>Interpolated integer value</returns>
        public static int LerpInt(int from, int to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return (int)MathF.Round(from + (to - from) * t);
        }

        /// <summary>
        /// Linear interpolation between two floating-point values.
        /// </summary>
        /// <param name="from">Starting value</param>
        /// <param name="to">Target value</param>
        /// <param name="t">Interpolation factor (not clamped - allows extrapolation)</param>
        /// <returns>Interpolated float value</returns>
        public static float LerpFloat(float from, float to, float t)
        {
            return from + (to - from) * t;
        }
    }
}
