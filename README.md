# Stack Overflow - The Game 🎮

You're a stack frame trying to climb higher and higher without overflowing. How far can you go?

## 🎯 What is this?

A retro arcade-style game where you jump from platform to platform, climbing as high as you can. Think of it like an endless vertical platformer - but with a nerdy computer science twist.

## 🚀 Quick Start

### Play Locally

**Requirements:**
- .NET SDK 9.0 or later (check with `dotnet --version`)
- A real terminal (the game needs keyboard input and won't work if you redirect things)

**To Play:**
```bash
# First time only - build the game
dotnet build

# Start playing!
dotnet run
```

That's it! The game will launch in your terminal.

### Play Online (No Installation!)

Don't want to install anything? Play directly in your browser:

1. **Via GitHub Codespaces:** Click the green "Code" button → "Codespaces" → "Create codespace on main"
2. Once it loads, open the terminal and type `dotnet run`
3. That's it - you're playing!

## 🎮 How to Play

### Starting the Game
- Tap any key at the splash screen to begin
- Enter your 3-character arcade initials (like "ACE" or "MVP")
- Start jumping!

### Controls
- **A/D** or **←/→**: Move left and right
- **S** or **↓**: Drop down faster
- **H**: Toggle the HUD display (Full → Compact → Hidden)
- **L**: View the leaderboard
- **Q** or **Esc**: Quit the game
- **R**: Restart after falling

### The Goal
- Jump from platform to platform, climbing higher each time
- Land on fresh platforms to level up
- Don't fall off the screen!
- Survive all 256 levels to win (good luck!)

### The Challenge
- Platforms get smaller and farther apart as you climb
- Stay within the cyan frame at the edges
- Don't fall below the danger line (it turns red when you're close!)
- The progress bar at the top changes from red → yellow → green as you get closer to winning

## 🏆 Leaderboards

Every run you complete gets saved! See how you stack up against others:

- **In-game:** Press **L** during gameplay or from the main menu
- **Online:** Visit [stackoverflow-minigame.fly.dev](https://stackoverflow-minigame.fly.dev/)

Your scores are saved both locally (in `scoreboard.jsonl`) and online.

## 🎨 Tips & Tricks

- Watch the altitude meter - it shows how close you are to the danger zone
- The stopwatch tracks how fast you're climbing
- You can see your best run in the HUD
- Platforms shrink progressively - adjust your jumps accordingly!

## 🔧 Advanced Options

### Debug Mode
Want to see what's happening under the hood?
```bash
dotnet run -- trace
```

### Leaderboard Only Mode
Just want to check the scores?
```bash
STACKOVERFLOW_MINIGAME_MODE=leaderboard dotnet run
```

## 📊 For Developers

### How Scores Work
- Scores are stored locally in `scoreboard.jsonl`
- They're also synced to a remote leaderboard at `https://stackoverflow-minigame.fly.dev/`
- The online leaderboard tracks top scores and fastest runs

### Environment Variables
Want to customize things? Here are some options:

- `STACKOVERFLOW_SCOREBOARD_REMOTE_URL`: Custom leaderboard URL
- `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL`: Where to send scores
- `STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET`: Auth for score submissions
- `STACKOVERFLOW_MINIGAME_MODE`: Set to "leaderboard" for viewer-only mode

### Hosting Your Own Leaderboard
The game includes a Python webhook server in `tools/scoreboard/webhook.py` that:
- Receives score submissions
- Stores them in a SQLite database
- Serves a live web leaderboard

To deploy it on Fly.io:
```bash
fly launch --copy-config --no-deploy --name <your-app-name>
fly volumes create scoreboard_data --size 1
fly secrets set SCOREBOARD_SECRET=your-secret-here
fly deploy
```

Then players can point to your server:
```bash
export STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL="https://your-app.fly.dev/scoreboard"
export STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET="your-secret-here"
```

### GitHub Integration (Optional)
Want scores automatically committed to GitHub? Set up the workflow in `.github/workflows/scoreboard-sync.yml` with:
- Repository secret: `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL`
- Repository secret: `STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET`

## 🤔 Troubleshooting

**Game won't start?**
- Make sure you have .NET 9.0+ installed
- Run from a real terminal (not redirected input/output)

**Scores not syncing?**
- Check your internet connection
- Verify the webhook URL is accessible
- Look for error messages in the console

**Graphics look weird?**
- Your terminal needs ANSI color support
- Try a different terminal emulator if issues persist

## 📝 Project Structure

```
Core/          - Main game loop and initialization
Gameplay/      - Game mechanics, entities, and world
Rendering/     - Display and graphics
UI/            - Menus, prompts, and HUD
Scoreboard/    - Score tracking and submission
Utils/         - Helper functions
tools/         - External tools (leaderboard server)
```

## 🎓 About

This is a fun project that combines arcade gaming with computer science concepts. Built with C# and playable in your terminal!

Have fun climbing, and watch out for that stack overflow! 🚀

---

**Current Deployment:** The live leaderboard is running at [stackoverflow-minigame.fly.dev](https://stackoverflow-minigame.fly.dev/)
