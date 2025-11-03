using System;

namespace stackoverflow_minigame.Game
{
    enum GameState { Menu, Running, Over }

    class GameCore
    {
        private GameState state = GameState.Menu;
        private bool running = true;

        private World world;
        private Renderer renderer;
        private Input input;
        private Spawner spawner;

        public GameCore()
        {
            world = new World(80, 25);
            renderer = new Renderer();
            input = new Input();
            spawner = new Spawner();
        }

        public void Run()
        {
            Console.CursorVisible = false;
            while (running)
            {
                switch (state)
                {
                    case GameState.Menu:
                        MenuLoop();
                        break;
                    case GameState.Running:
                        GameLoop();
                        break;
                    case GameState.Over:
                        OverLoop();
                        break;
                }
            }
            Console.CursorVisible = true;
        }

        private void MenuLoop() { /* TODO: Add start screen */ }
        private void GameLoop() { /* TODO: Add main game loop */ }
        private void OverLoop() { /* TODO: Add game over handling */ }
    }
}
