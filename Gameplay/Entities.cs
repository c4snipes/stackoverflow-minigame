using System;
using System.Collections.Concurrent;

namespace stackoverflow_minigame
{
    internal abstract class Entity
    {
        public float X { get; protected internal set; }
        public float Y { get; protected internal set; }
        public abstract char Symbol { get; }
        public virtual void Update() { }
    }

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
    internal class Platform : Entity
    {
        private static readonly ConcurrentStack<Platform> Pool = new();

        public override char Symbol => '=';
        public int Length { get; private set; }

        private Platform() { }

        public static Platform Acquire(int x, float y, int length, int interiorWidth)
        {
            if (!Pool.TryPop(out Platform? platform) || platform == null)
            {
                platform = new Platform();
            }
            platform.Initialize(x, y, length, interiorWidth);
            return platform;
        }
// Releases a platform back to the pool for reuse.
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
// End of entities.cs
// ----------------------------------------------------------------
