using System;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Procedural platform spawner with progressive difficulty scaling.
    /// Gradually increases gaps and reduces platform density as player progresses.
    /// </summary>
    internal class Spawner
    {
        private readonly Random rand;
        private const int EarlyMinGap = 6;
        private const int EarlyMaxGap = 10;
        private const int LateMinGap = 12;
        private const int LateMaxGap = 16;
        private const float ExtraPlatformEarlyChance = 0.5f;
        private const float ExtraPlatformLateChance = 0.1f;
        private const int MaxPlatformsPerBand = 3;
        private const float MinimumBandTolerance = 0.2f;
        private const int MinGapCeiling = 4;
        private const int HeightDivisorForMaxGap = 2;
        private const int MaxPlacementAttempts = 40;

        public Spawner(int? seed = null)
        {
            rand = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public void Update(World world)
        {
            float highestY = world.Offset;
            foreach (Platform platform in world.Platforms)
            {
                if (platform.Y > highestY)
                {
                    highestY = platform.Y;
                }
            }
            while (highestY < world.Offset + world.Height)
            {
                int gap = GetGap(world);
                highestY += gap;
                int platformsThisBand = GetPlatformsPerBand(world);
                SpawnPlatformsAt(world, highestY, platformsThisBand);
            }
        }

        private int GetGap(World world)
        {
            float progress = GetProgress(world);
            int minGap = MathHelpers.LerpInt(EarlyMinGap, LateMinGap, progress);
            int maxGap = MathHelpers.LerpInt(EarlyMaxGap, LateMaxGap, progress);
            if (maxGap < minGap)
            {
                maxGap = minGap;
            }

            int gap = rand.Next(minGap, maxGap + 1);
            int maxAllowedGap = Math.Max(MinGapCeiling, world.Height / HeightDivisorForMaxGap);
            if (gap > maxAllowedGap)
            {
                gap = maxAllowedGap;
            }

            return Math.Max(1, gap);
        }

        private int GetPlatformsPerBand(World world)
        {
            float progress = GetProgress(world);
            float chance = MathHelpers.LerpFloat(ExtraPlatformEarlyChance, ExtraPlatformLateChance, progress);
            int count = 1;
            while (count < MaxPlatformsPerBand && rand.NextDouble() < chance)
            {
                count++;
                chance *= 0.5f;
            }
            return count;
        }

        private void SpawnPlatformsAt(World world, float y, int count)
        {
            int placed = 0;
            int attempts = 0;
            while (placed < count && attempts < MaxPlacementAttempts)
            {
                attempts++;
                if (TrySpawnPlatform(world, y))
                {
                    placed++;
                }
            }

            if (placed == 0)
            {
                ForceSpawnPlatform(world, y);
                Diagnostics.ReportWarning($"Spawner forced platform at y={y:F2} after {attempts} attempts (level={world.LevelsCompleted}).");
            }
        }

        private bool TrySpawnPlatform(World world, float y)
        {
            var (platform, start, length) = CreatePlatform(world, y);
            if (BandHasOverlap(world, y, start, length))
            {
                Platform.Release(platform);
                return false;
            }
            world.AddPlatform(platform);
            return true;
        }

        private void ForceSpawnPlatform(World world, float y)
        {
            var (platform, _, _) = CreatePlatform(world, y);
            world.AddPlatform(platform);
        }

        private (Platform platform, int start, int length) CreatePlatform(World world, float y)
        {
            int length = GeneratePlatformLength(world);
            int start = GetPlatformStart(world, length);
            var platform = Platform.Acquire(start, y, length, world.Width);
            return (platform, start, length);
        }

        private int GeneratePlatformLength(World world)
        {
            int interiorWidth = Math.Max(1, world.Width - Renderer.BorderThickness * 2);
            int minLength = World.MinPlatformLength;
            int baseMax = Math.Max(minLength, interiorWidth / 3);

            float progress = GetProgress(world);
            float shrinkFactor = MathHelpers.LerpFloat(1f, 0.35f, progress);
            int maxLength = Math.Max(minLength + 1, (int)MathF.Round(baseMax * shrinkFactor));

            return rand.Next(minLength, maxLength + 1);
        }

        private bool BandHasOverlap(World world, float y, int start, int length)
        {
            int end = start + length - 1;
            foreach (Platform platform in world.Platforms)
            {
                if (Math.Abs(platform.Y - y) > Math.Max(MinimumBandTolerance, Game.FrameTimeSeconds * 2f))
                {
                    continue;
                }

                int existingStart = (int)MathF.Round(platform.X);
                int existingEnd = existingStart + platform.Length - 1;
                if (RangesOverlap(start, end, existingStart, existingEnd))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RangesOverlap(int aStart, int aEnd, int bStart, int bEnd) =>
            aStart <= bEnd && bStart <= aEnd;

        private int GetPlatformStart(World world, int length)
        {
            int interiorMaxStart = Math.Max(0, world.Width - length);
            return rand.Next(interiorMaxStart + 1);
        }

        private static float GetProgress(World world) =>
            Math.Clamp(world.LevelsCompleted / (float)World.GoalPlatforms, 0f, 1f);
    }
}
