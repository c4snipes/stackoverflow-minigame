namespace stackoverflow_minigame.Entities
{
    class Player : Entity
    {
        public override char Symbol => '@';

        public Player(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}
