using System;

namespace stackoverflow_minigame {
    abstract class Entity {
        public float X { get; set; }
        public float Y { get; set; }
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
        public override char Symbol => '=';
        public int Length { get; }

        public Platform(int x, float y, int length, int interiorWidth) {
            int clampedLength = Math.Max(1, length);
            clampedLength = Math.Min(clampedLength, Math.Max(1, interiorWidth));
            X = Math.Clamp(x, 0, Math.Max(0, interiorWidth - clampedLength));
            Y = y;
            Length = clampedLength;
        }
    }
}
