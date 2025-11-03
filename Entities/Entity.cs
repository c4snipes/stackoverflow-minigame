namespace stackoverflow_minigame.Entities
{
    abstract class Entity
    {
        public int X { get; set; }
        public int Y { get; set; }

        public abstract char Symbol { get; }

        public virtual void Update(double deltaTime) { }
    }
}
