using System;
using System.Collections.Generic;

namespace stackoverflow_minigame {
    class Input {
        // This class can be extended for more complex input management if needed.
        public void Poll() {
            // In this simple game, input is handled directly in GameLoop using Console.
        }
    }

    class Renderer {
        public void Clear() {
            Console.Clear();
        }

        public void Draw(World world) {
            // Draw platforms first
            foreach (Entity entity in world.Entities) {
                if (entity is Player) continue;
                int screenY = (int)(world.Height - 1 - (entity.Y - world.Offset));
                if (screenY >= 0 && screenY < world.Height) {
                    if (entity.X >= 0 && entity.X < world.Width) {
                        Console.SetCursorPosition(entity.X, screenY);
                        Console.Write(entity.Symbol);
                    }
                }
            }
            // Draw player last (on top of platforms)
            Entity player = world.Player;
            int playerScreenY = world.Height - 1 - (int)(player.Y - world.Offset);
            if (playerScreenY >= 0 && playerScreenY < world.Height) {
                if (player.X >= 0 && player.X < world.Width) {
                    Console.SetCursorPosition(player.X, playerScreenY);
                    Console.Write(player.Symbol);
                }
            }
        }

        public void Present() {
            // In console, drawing is immediate, so no additional buffering needed.
            // This method is included for compatibility.
        }
    }

    class Spawner {
        private Random rand = new Random();
        private const int MinPlatformGap = 5;
        private const int MaxPlatformGap = 10;

        public void Update(World world) {
            int highestY = world.Offset;
            foreach (Platform platform in world.Platforms) {
                if (platform.Y > highestY) highestY = (int)platform.Y;
            }
            while (highestY < world.Offset + world.Height) {
                int gap = rand.Next(MinPlatformGap, MaxPlatformGap);
                highestY += gap;
                int newX = rand.Next(world.Width);
                world.Platforms.Add(new Platform(newX, highestY));
            }
        }
    }
}
