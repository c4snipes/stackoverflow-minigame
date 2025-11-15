# stackoverflow-minigame
You are a stack frame trying to climb without overflowing.

## Table of Contents
1. [Requirements](#requirements)
2. [How to Run](#how-to-run)
3. [Gameplay Reference](#gameplay-reference)
4. [Leaderboards & Sync](#leaderboards--sync)
5. [Fly Relay Deployment](#fly-relay-deployment)
6. [Play From the Cloud](#play-from-the-cloud)
7. [Runtime Flow](#runtime-flow)

## Requirements
- .NET SDK 9.0 or later (`dotnet --version`).
- Real terminal (ANSI + keyboard). The game exits if STDIN/STDOUT are redirected.

## How to Run
1. Build once:
   ```bash
   dotnet build
   ```
2. Play:
   ```bash
   dotnet run
   ```
3. Optional tracing for glyph/initial events:
   ```bash
   dotnet run -- trace
   ```

## Gameplay Reference
### Boot-up & Prompts
- Tap any key on the splash screen to start; `Q`/`Esc` always quits, `R` restarts after a fall.
- Enter three-character arcade initials before your first jump. If the console cannot read keys the previous initials are reused automatically.

### Controls
- `A`/`D` or `←`/`→`: drift with buffered intent (~120 ms stickiness).
- `S`/`↓`: fast-drop burst.
- `H`: cycle HUD (Full → Compact → Hidden).
- `Q`/`Esc`: exit.

### World & Objective
- 60×38 vertical shaft; touching the cyan frame clears horizontal intent but keeps you visible.
- Platforms shrink and spread out the higher you climb. Landing on untouched ground awards a level and auto-launches the next jump.
- Fall below the camera offset or miss a platform and the run ends. Clear 256 levels to “empty the call stack.”

### HUD & Feedback
- The HUD tracks initials, levels cleared, best run, altitude, stopwatch, and a 256-level progress bar that shifts from red → yellow → green.
- Text turns red when you are within three rows of the fail line.

## Leaderboards & Sync
### Viewing standings
- Every finished (or aborted) run appends a JSON line to `scoreboard.jsonl`.
- Press `L` from the title screen: the game detects your OS and launches the appropriate helper (`./launch-leaderboard.sh` on Unix, `pwsh ./launch-leaderboard.ps1` on Windows). The helper just sets `STACKOVERFLOW_MINIGAME_MODE=leaderboard` and runs `dotnet run` to display the standalone viewer; `Q`/`Esc` closes it.
- You can also open the built-in overlay while playing by tapping `L` again.

### Keeping the shared board up-to-date
The GitHub Action in `.github/workflows/scoreboard-sync.yml` listens for `repository_dispatch` events with payload `line_b64` (base64-encoded JSON entry). Configure either:

**1. Direct GitHub dispatch (per-player PAT)**
```bash
export STACKOVERFLOW_SCOREBOARD_REPO="c4snipes/stackoverflow-minigame"
export STACKOVERFLOW_SCOREBOARD_DISPATCH_TOKEN="ghp_xxx"
# optional overrides:
# export STACKOVERFLOW_SCOREBOARD_DISPATCH_EVENT="scoreboard-entry"
# export STACKOVERFLOW_SCOREBOARD_API_BASE="https://api.github.com"
```
Each run calls `https://api.github.com/repos/<owner>/<repo>/dispatches` and the workflow commits the appended line.

**2. Shared webhook (Fly relay, no PAT for players)**
```bash
cp .env .env.local
# ensure STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL=https://stackoverflow-minigame.fly.dev/scoreboard
# keep STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET in the same file
set -a && source .env.local && set +a
```
The game POSTs the JSON line to the relay and the relay dispatches to GitHub using the PAT you stored as Fly secrets.

### Verifying the pipeline
1. Load your environment variables (PAT or webhook).
2. Send a dummy entry:
   ```bash
   curl -X POST "$STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL" \
     -H "Content-Type: application/json" \
     -H "X-Scoreboard-Secret: $STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET" \
     -d '{"line":"{\"id\":\"healthcheck\",\"initials\":\"HCK\",\"level\":1}"}'
   ```
   or call the GitHub API directly if you’re using a PAT.
3. Watch **Actions → Sync Scoreboard** for the triggered workflow; it should finish green and push a commit touching `scoreboard.jsonl`.
4. Play a run (`dotnet run`), quit with `Q` if you like, ensure `tail scoreboard.jsonl` shows the new entry, and confirm another Action run appears.
5. Keep `./launch-leaderboard.sh` open (or hit `L`) to watch the board update live.

### Included Python relay
`tools/scoreboard_webhook.py` is a tiny HTTP service that listens on `/scoreboard`, validates `X-Scoreboard-Secret`, and fires a `repository_dispatch` with the PAT it owns. Put it behind any TLS-terminating proxy (Fly handles this automatically).

```
export SCOREBOARD_REPO="<owner>/<repo>"
export SCOREBOARD_GITHUB_TOKEN="<PAT with Contents: Read/Write>"
export SCOREBOARD_SECRET="shared-secret"
# optional: SCOREBOARD_HOST, SCOREBOARD_PORT (defaults to 0.0.0.0:8080), SCOREBOARD_API_BASE
python tools/scoreboard_webhook.py
```
Give players `https://<your-proxy>/scoreboard` plus the shared secret. They never see the PAT.

## Fly Relay Deployment
1. Install the Fly CLI: `fly auth login`.
2. From the repo root:
   ```bash
   fly launch --copy-config --no-deploy --name <app>
   fly secrets set \
     SCOREBOARD_REPO=c4snipes/stackoverflow-minigame \
     SCOREBOARD_GITHUB_TOKEN=ghp_xxx \
     SCOREBOARD_SECRET=shared-secret
   fly deploy
   ```
3. Fly terminates TLS, so the container just listens on `0.0.0.0:$PORT`. Players point `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL` to `https://<app>.fly.dev/scoreboard`.

## Play From the Cloud
- **GitHub Codespaces:** `Code → Codespaces → Create`. Use the built-in terminal and run `dotnet run`.
- **GitHub CLI:** `gh codespace create -r c4snipes/stackoverflow-minigame`, then `gh codespace ssh -c <name>` and run `dotnet run`.
- **Gitpod (or similar):** `https://gitpod.io/#https://github.com/c4snipes/stackoverflow-minigame` spins up a disposable devcontainer.
All options keep the repo remote—nothing lands on your local disk.

## Runtime Flow
![Flow of Information](flowOfInformationConsoleGame.png)

> Diagram only; underlying PlantUML isn’t included.
