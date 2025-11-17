using System;
using System.Collections.Concurrent;

namespace stackoverflow_minigame
{
    /// <summary>
    /// Base entity class for all game objects in the world.
    /// </summary>
    internal abstract class Entity
    {
        public float X { get; protected internal set; }
        public float Y { get; protected internal set; }
        public abstract char Symbol { get; }
        public virtual void Update() { }
    }

    /// <summary>
    /// The player character - a stack frame climbing the call stack.
    /// </summary>
    internal class Player : Entity
    {
        public float VelocityY { get; set; }
        public override char Symbol => '@';

        public Player(int x, float y)
        {
            X = x;
            Y = y;
            VelocityY = 0;
        }
    }

    /// <summary>
    /// Platform entity using object pooling to reduce GC pressure during gameplay.
    /// Platforms are recycled via Acquire/Release pattern.
    /// </summary>
    internal class Platform : Entity
    {
        private static readonly ConcurrentStack<Platform> Pool = new();

        public override char Symbol => '=';
        public int Length { get; private set; }

        private Platform() { }

        /// <summary>
        /// Acquires a platform from the pool or creates a new one if pool is empty.
        /// </summary>
        public static Platform Acquire(int x, float y, int length, int interiorWidth)
        {
            if (!Pool.TryPop(out Platform? platform) || platform == null)
            {
                platform = new Platform();
            }
            platform.Initialize(x, y, length, interiorWidth);
            return platform;
        }

        /// <summary>
        /// Returns a platform to the pool for reuse, reducing allocations.
        /// </summary>
        public static void Release(Platform platform)
        {
            if (platform == null)
            {
                return;
            }

            Pool.Push(platform);
        }

        private void Initialize(int x, float y, int length, int interiorWidth)
        {
            int clampedLength = Math.Max(1, Math.Min(length, Math.Max(1, interiorWidth)));
            Length = clampedLength;
            X = Math.Clamp(x, 0, Math.Max(0, interiorWidth - clampedLength));
            Y = y;
        }
    }
}
