# stackoverflow-minigame
You are a stack frame trying to climb without overflowing.

## Table of Contents
- [stackoverflow-minigame](#stackoverflow-minigame)
  - [Table of Contents](#table-of-contents)
  - [Requirements](#requirements)
  - [How to Run](#how-to-run)
  - [Gameplay Reference](#gameplay-reference)
    - [Boot-up \& Prompts](#boot-up--prompts)
    - [Controls](#controls)
    - [World \& Objective](#world--objective)
    - [HUD \& Feedback](#hud--feedback)
  - [Leaderboards \& Sync](#leaderboards--sync)
    - [Viewing standings](#viewing-standings)
    - [Optional GitHub mirroring](#optional-github-mirroring)
    - [Verifying the pipeline](#verifying-the-pipeline)
    - [Included Python relay](#included-python-relay)
- [optional mirroring:](#optional-mirroring)
- [export SCOREBOARD\_REPO="/"](#export-scoreboard_repo)
- [export SCOREBOARD\_GITHUB\_TOKEN="\<PAT with Contents: Read/Write\>"](#export-scoreboard_github_tokenpat-with-contents-readwrite)
- [optional tuning: SCOREBOARD\_HOST, SCOREBOARD\_PORT, SCOREBOARD\_API\_BASE, SCOREBOARD\_LEADERBOARD\_LIMIT](#optional-tuning-scoreboard_host-scoreboard_port-scoreboard_api_base-scoreboard_leaderboard_limit)
  - [Play From the Cloud](#play-from-the-cloud)
  - [Runtime Flow](#runtime-flow)

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
- Each finished (or aborted) run still appends to `scoreboard.jsonl` locally so the HUD and end-of-run summary have instant data even offline.
- At the same time the Fly relay persists the entry in a SQLite database (mounted at `/data/scoreboard.db`) and exposes the aggregated board at `https://stackoverflow-minigame.fly.dev/` (JSON is available at `/scoreboard`). `LeaderboardViewer` now fetches that feed by default; override it with `STACKOVERFLOW_SCOREBOARD_REMOTE_URL` if you self-host.
- Press `L` from the title screen to launch the standalone viewer, or run `STACKOVERFLOW_MINIGAME_MODE=leaderboard dotnet run` if you only want the reader window. Tap `L` while playing to show the overlay without leaving the session.

### Optional GitHub mirroring
The Fly relay is now the source of truth, but the GitHub Action in `.github/workflows/scoreboard-sync.yml` is kept around so you can requeue entries from the Actions UI (or any `repository_dispatch`). It simply decodes the `line_b64` payload and POSTs it to the same Fly webhook, so make sure the repository has secrets named `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL` and `STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET`. You can trigger the workflow via:

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
3. `curl https://<your-app>.fly.dev/scoreboard | jq '.'` to confirm the remote feed shows the entry (or keep the leaderboard viewer open and watch it update live).
4. Play a run (`dotnet run`), quit with `Q` if you like, and ensure both `tail scoreboard.jsonl` and the remote feed show the new row.
5. If GitHub mirroring is enabled, watch **Actions → Sync Scoreboard** for the triggered workflow; it should finish green (no repository changes are expected now that the Fly DB is canonical).

### Included Python relay
`tools/scoreboard/webhook.py` is a tiny HTTP service that listens on `/scoreboard`, validates `X-Scoreboard-Secret`, persists entries in a SQLite database, and (optionally) fires a `repository_dispatch` if you provide a GitHub token. It also serves the live leaderboard at `/` (HTML) and `/scoreboard` (JSON). Put it behind any TLS-terminating proxy (Fly handles this automatically).

export SCOREBOARD_SECRET="shared-secret"
export SCOREBOARD_DB_PATH="$PWD/scoreboard.db"          # defaults to /data/scoreboard.db
# optional mirroring:
# export SCOREBOARD_REPO="<owner>/<repo>"
# export SCOREBOARD_GITHUB_TOKEN="<PAT with Contents: Read/Write>"
# optional tuning: SCOREBOARD_HOST, SCOREBOARD_PORT, SCOREBOARD_API_BASE, SCOREBOARD_LEADERBOARD_LIMIT
python tools/scoreboard/webhook.py
```
Give players `https://<your-proxy>/scoreboard` plus the shared secret. They never see the PAT, and the service serves `topLevels`/`fastestRuns` JSON to any GET requests.

## Fly Relay Deployment
1. Install the Fly CLI: `fly auth login`.
2. Create a persistent volume so the SQLite database survives restarts:
   ```bash
   fly volumes create scoreboard_data --size 1 --app <app-name>
   ```
3. From the repo root:
   ```bash
   fly launch --copy-config --no-deploy --name <app>
   fly secrets set \
     SCOREBOARD_SECRET=shared-secret
   # optional mirroring if you still want GitHub commits:
   fly secrets set \
     SCOREBOARD_REPO=c4snipes/stackoverflow-minigame \
     SCOREBOARD_GITHUB_TOKEN=ghp_xxx
   fly deploy
   ```
4. Fly terminates TLS, so the container just listens on `0.0.0.0:$PORT`. Players point `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL` (for posting) and `STACKOVERFLOW_SCOREBOARD_REMOTE_URL` (for viewing) to `https://<app>.fly.dev/scoreboard`.

## Play From the Cloud
- **GitHub Codespaces:** `Code → Codespaces → Create`. Use the built-in terminal and run `dotnet run`.
- **GitHub CLI:** `gh codespace create -r c4snipes/stackoverflow-minigame`, then `gh codespace ssh -c <name>` and run `dotnet run`.
Next steps

Create the Fly volume if you haven’t already (fly volumes create scoreboard_data --size 1 --app stackoverflow-minigame), then redeploy (fly deploy --config fly.toml) so the new DB-backed relay runs in production.
Point clients at the JSON feed (STACKOVERFLOW_SCOREBOARD_REMOTE_URL=https://stackoverflow-minigame.fly.dev/scoreboard, already the default) and keep STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL/secret exported so game runs keep posting.

## Runtime Flow
![Flow of Information](flowOfInformationConsoleGame.png)

> Diagram only; underlying PlantUML isn’t included.
