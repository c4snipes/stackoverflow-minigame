using System;

namespace stackoverflow_minigame {
    abstract class Entity {
        public float X { get; protected internal set; }
        public float Y { get; protected internal set; }
        public abstract char Symbol { get; }
        public virtual void Update() { }
    }

    class Player : Entity {
        public float VelocityY { get; set; }
        public override char Symbol => '@';

        public Player(int x, float y) {
            X = x;
            Y = y;
            VelocityY = 0;
        }
    }

    class Platform : Entity {
        private static readonly Stack<Platform> Pool = new();

        public override char Symbol => '=';
        public int Length { get; private set; }

        private Platform() { }

        public static Platform Acquire(int x, float y, int length, int interiorWidth) {
            Platform platform = Pool.Count > 0 ? Pool.Pop() : new Platform();
            platform.Initialize(x, y, length, interiorWidth);
            return platform;
        }

        public static void Release(Platform platform) {
            Pool.Push(platform);
        }

        private void Initialize(int x, float y, int length, int interiorWidth) {
            int clampedLength = Math.Max(1, Math.Min(length, Math.Max(1, interiorWidth)));
            Length = clampedLength;
            X = Math.Clamp(x, 0, Math.Max(0, interiorWidth - clampedLength));
            Y = y;
        }
    }
}
