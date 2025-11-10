using System;
using System.Collections.Generic;

namespace stackoverflow_minigame {
    class World {
        public int Width { get; }
        public int Height { get; }
        public int Offset { get; private set; }
        public Player Player { get; private set; }
        private readonly List<Platform> platforms;
        private readonly Random rand;
        internal List<Platform> Platforms { get { return platforms; } }
        internal const float JumpVelocity = 3.2f;
        public float MaxAltitude { get; private set; }
        public int LevelsCompleted { get; private set; }
        public const int GoalPlatforms = 256;
        public bool LandedThisFrame { get; private set; }
        public bool LevelAwardedThisFrame { get; private set; }
        public bool BorderHitThisFrame { get; private set; }
        private const float GroundHorizontalUnitsPerSecond = 15f;
        private const float AirHorizontalSpeedMultiplier = 1.3f;
        private const float FastDropImpulse = -6f;
        private const int MaxPlatformWidthDivisor = 3;
        internal const int MinPlatformLength = 4;
        internal int MaxPlatformLength => Math.Min(Width, Math.Max(MinPlatformLength, Width / MaxPlatformWidthDivisor));
        private const int InitialPlatformY = 8;
        private const int InitialPlatformGap = 7;
        private float highestPlatformTouched;
        private int visibleRowBudget = int.MaxValue;

        public World(int width, int height) {
            Width = width;
            Height = height;
            platforms = new List<Platform>();
            rand = new Random();
            Player = new Player(width / 2, 0);
            Reset();
        }

        public void Reset() {
            ResetPlayer();
            ReleaseAllPlatforms();
            Offset = 0;
            MaxAltitude = 0f;
            LevelsCompleted = 0;
            highestPlatformTouched = float.NegativeInfinity;
            LevelAwardedThisFrame = false;

            AddSeedPlatform(InitialPlatformY, centerOnPlayer: true);
            AddSeedPlatform(InitialPlatformY + InitialPlatformGap, centerOnPlayer: false);
        }

        private int GetRandomPlatformLength() {
            int maxLength = Math.Min(Width, MaxPlatformLength);
            return rand.Next(MinPlatformLength, Math.Max(MinPlatformLength, maxLength) + 1);
        }

        private void AddSeedPlatform(int y, bool centerOnPlayer) {
            int length = GetRandomPlatformLength();
            int interiorWidth = Math.Max(1, Width);
            int start = centerOnPlayer
                ? Math.Clamp((int)MathF.Round(Player.X) - length / 2, 0, Math.Max(0, interiorWidth - length))
                : rand.Next(Math.Max(0, interiorWidth - length) + 1);
            platforms.Add(Platform.Acquire(start, y, length, interiorWidth));
        }

        public void Update(float deltaSeconds, int horizontalDirection, bool fastDropRequested) {
            LandedThisFrame = false;
            LevelAwardedThisFrame = false;
            BorderHitThisFrame = false;
            float stepScale = deltaSeconds / Game.FrameTimeSeconds;

            float horizontalMax = Math.Max(0, Width - 1);
            float newX = Player.X;
            if (horizontalDirection != 0) {
                float horizontalSpeed = GroundHorizontalUnitsPerSecond;
                bool isAirborne = MathF.Abs(Player.VelocityY) > 0.05f;
                if (isAirborne) {
                    horizontalSpeed *= AirHorizontalSpeedMultiplier;
                }
                newX += horizontalDirection * horizontalSpeed * deltaSeconds;
            }
            float clampedX = Math.Clamp(newX, 0f, horizontalMax);
            BorderHitThisFrame = MathF.Abs(clampedX - newX) > float.Epsilon;
            Player.X = clampedX;

            if (fastDropRequested) {
                Player.VelocityY += FastDropImpulse;
            }

            Player.VelocityY += Game.GravityPerSecond * deltaSeconds;
            float oldY = Player.Y;
            float newY = oldY + Player.VelocityY * stepScale;

            if (Player.VelocityY < 0) {
                Platform? collidePlatform = null;
                int playerColumn = Math.Clamp((int)MathF.Round(Player.X), 0, Math.Max(0, Width - 1));
                foreach (Platform platform in platforms) {
                    int platformStart = (int)MathF.Round(platform.X);
                    int platformEnd = platformStart + platform.Length - 1;
                    if (playerColumn >= platformStart && playerColumn <= platformEnd && platform.Y <= oldY && platform.Y >= newY) {
                        collidePlatform = platform;
                        break;
                    }
                }
                if (collidePlatform != null) {
                    newY = collidePlatform.Y;
                    Player.VelocityY = World.JumpVelocity;
                    LandedThisFrame = true;
                    if (collidePlatform.Y > highestPlatformTouched + float.Epsilon) {
                        bool hasTouchedBefore = !float.IsNegativeInfinity(highestPlatformTouched);
                        highestPlatformTouched = collidePlatform.Y;
                        if (hasTouchedBefore) {
                            LevelsCompleted++;
                            LevelAwardedThisFrame = true;
                        }
                    }
                }
            }

            Player.Y = newY;
            if (Player.Y > MaxAltitude) {
                MaxAltitude = Player.Y;
            }

            int thresholdSource = visibleRowBudget <= 0 ? Height : visibleRowBudget;
            int threshold = Math.Max(1, Math.Min(Height / 2, thresholdSource / 2));
            if (Player.Y > threshold) {
                Offset = (int)(Player.Y - threshold);
            }
            for (int i = platforms.Count - 1; i >= 0; i--) {
                if (platforms[i].Y < Offset) {
                    Platform.Release(platforms[i]);
                    platforms.RemoveAt(i);
                }
            }
        }

        private void ResetPlayer() {
            Player ??= new Player(Width / 2, 0);
            Player.X = Width / 2f;
            Player.Y = 0f;
            Player.VelocityY = 0f;
        }

        public IEnumerable<Entity> Entities {
            get {
                yield return Player;
                foreach (Platform p in platforms) yield return p;
            }
        }

        public void SetVisibleRowBudget(int rows) {
            visibleRowBudget = Math.Max(1, rows);
        }
        private void ReleaseAllPlatforms() {
            foreach (Platform platform in platforms) {
                Platform.Release(platform);
            }
            platforms.Clear();
        }
    }
}
