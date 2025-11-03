namespace stackoverflow_minigame.Game
{
    class World
    {
        public int Width { get; }
        public int Height { get; }

        public World(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void Reset() { /* TODO: Initialize world entities */ }

        public void Update(double deltaTime)
        {
            /* TODO: Update entities, collisions, and physics */
        }
    }
}
