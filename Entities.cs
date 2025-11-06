namespace stackoverflow_minigame {
    abstract class Entity {
        public int X { get; set; }
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

        public Platform(int x, float y) {
            X = x;
            Y = y;
        }
    }
}
