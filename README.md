# stackoverflow-minigame
You’re a stack frame trying to climb higher without overflowing.
## Build and run
Requirements: .NET SDK (9.0 or later) installed. Verify with `dotnet --version`.
From the repository root:
1. Build:
   ```bash
   dotnet build
   ```
2. Run:
   ```bash
   dotnet run
   ```
   
The project uses a single SDK-style project file `stackoverflow-minigame.csproj` in the repository root, and a solution file `stackoverflow-minigame.sln` is already included.
## Gameplay
**Boot-up**  
- Launch from a real terminal. If input/output is redirected, the game warns you and exits safely.  
- Tap any key on the splash screen to start climbing. `Q`/`Esc` quits immediately, `R` restarts after a fall.  
- Before the first jump you’ll enter three-character arcade initials; the blinking 5×5 glyph prompt only accepts A–Z/0–9 and falls back to your last initials if the console can’t read keys.

**Controls & Movement**  
- `A`/`D` or `←`/`→`: drift horizontally. Intent lingers for ~0.12 s, so quick taps still translate into motion.  
- `S`/`↓`: trigger a fast-drop burst to dive through gaps.  
- `H`: cycle HUD visibility (Full → Compact → Hidden) to reclaim vertical space on cramped buffers.  
- `Q`/`Esc`: bail out from anywhere outside blocking prompts.

**World & Objective**  
- The world is a 60×38 shaft wrapped in a cyan frame—touching the border clears your horizontal intent but keeps you on-screen.  
- Platforms spawn in bands: wide/dense near the base, narrow and spaced near the top. Landing on a higher, untouched platform awards a level and auto-launches your next jump.  
- Drop below the camera offset or miss a platform and the run ends. Survive 256 platform awards to “clear the error stack” and win.

**HUD & Feedback**  
- Stats sit under the playfield and show initials, level, landing score, current/max altitude, best run, and a `mm:ss.fff` stopwatch.  
- A progress bar tracks your march to 256 levels and shifts red → yellow → green. The HUD text turns red when you’re within three rows of the fail line.  
- Run with `dotnet run -- trace` to see verbose diagnostics for glyph lookups and initials entry.

**Persistence & Leaderboards**  
- Finished runs append to `scoreboard.jsonl` (newline-delimited JSON shared in the repo).  
- Run `dotnet run -- leaderboard` (or `./launch-leaderboard.sh`) in another terminal to view live top-level and fastest-run boards; press `Q`/`Esc` to close the viewer. You can also tap `L` on the game’s main menu to open the built-in leaderboard overlay and return with `Q`/`Esc`.

## Automated scoreboard sync (GitHub Actions)
The workflow in `.github/workflows/scoreboard-sync.yml` appends new entries to `scoreboard.jsonl` whenever a `repository_dispatch` event with the type `scoreboard-entry` is received (or when you trigger the workflow manually through the Actions UI). Each event must include a base64-encoded payload named `line_b64` that contains the exact JSON line you want appended.

### Choose how to sync
- **Per-player PAT (direct GitHub dispatch):**
  1. [Create a fine-grained PAT](https://github.com/settings/tokens?type=beta) with **Repository → Contents: Read/Write** scope.
  2. Export the variables before running the game:
     ```bash
     export STACKOVERFLOW_SCOREBOARD_REPO="colesnipes/stackoverflow-minigame"
     export STACKOVERFLOW_SCOREBOARD_DISPATCH_TOKEN="ghp_xxx"
     # optional overrides:
     # export STACKOVERFLOW_SCOREBOARD_DISPATCH_EVENT="scoreboard-entry"
     # export STACKOVERFLOW_SCOREBOARD_API_BASE="https://api.github.com"
     ```
  3. Each run automatically POSTs to `https://api.github.com/repos/<owner>/<repo>/dispatches`; the workflow appends the row.
- **Shared webhook (Fly relay, no PAT for players):**
  1. Copy `.env` to `.env.local`, keep `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL=https://stackoverflow-minigame.fly.dev/scoreboard` and the shared secret, then load it with `set -a && source .env.local && set +a`.
  2. When the game finishes, it POSTs the JSON line to the Fly relay. That service calls GitHub with the PAT stored on Fly, so the workflow still commits the entry.

You can still manually trigger the workflow from GitHub Actions → **Sync Scoreboard** → **Run workflow** if you need to re-add a line (base64-encode it first), but day-to-day play is handled by one of the two options above.

### Verify the end-to-end pipeline
1. Load your environment variables (PAT-based or webhook-based) with the `set -a && source … && set +a` pattern so the game can see them.
2. Dry-run the webhook/dispatch flow by posting a dummy entry:
   ```bash
   curl -X POST "${STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL}" \
     -H "Content-Type: application/json" \
     -H "X-Scoreboard-Secret: ${STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET}" \
     -d '{"line":"{\"id\":\"healthcheck\",\"initials\":\"HCK\",\"score\":1}"}'
   ```
   or, if you’re using PAT dispatch, call the GitHub API with the sample command from the previous section. A `queued` response (webhook) or HTTP 204 (GitHub API) confirms the request was accepted.
3. Open GitHub → **Actions → Sync Scoreboard** and wait for the workflow run triggered by the test payload. It should finish green and create a commit touching `scoreboard.jsonl`.
4. Launch the game with `dotnet run`, finish a quick run (quit immediately if you want), and verify two things:
   - `tail scoreboard.jsonl` locally shows the new entry.
   - Another **Sync Scoreboard** workflow run appears and the leaderboard commit includes your entry.
5. Optional: run `dotnet run -- leaderboard` or `./launch-leaderboard.sh` in a second terminal to watch the shared board update in real time.

#### Shared webhook (no personal PAT required by players)
If you’d rather keep a single credential on a server you control, expose a simple HTTPS endpoint that accepts the payload and triggers GitHub on behalf of players. Configure that endpoint with the PAT (or GitHub App token) privately, then give players just the webhook URL/secret:
- `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL`: HTTPS endpoint that accepts POSTs with the body `{"line":"...","line_b64":"..."}`.
- `STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET` *(optional)*: shared secret sent as the `X-Scoreboard-Secret` header so your service can reject anonymous traffic.

Whenever the game writes a score it will call both the direct GitHub dispatch (if configured) *and* the webhook. You can host the webhook on any platform (Azure Function, AWS Lambda, Render, etc.); inside that handler append to `scoreboard.jsonl` or forward the payload to GitHub using the PAT stored on the server. Players never need their own tokens—only the service sees the credential.

##### Included Python relay
The repo ships with `tools/scoreboard_webhook.py`, a tiny HTTPS-capable relay built on the Python standard library. It listens on `/scoreboard`, validates `X-Scoreboard-Secret`, and fires a `repository_dispatch` with the supplied PAT.

1. Generate a certificate (optional but recommended) if you need TLS:
   ```bash
   openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
     -keyout webhook.key -out webhook.crt -subj "/CN=localhost"
   ```
2. Export the required environment variables:
   ```bash
   export SCOREBOARD_REPO="<owner>/<repo>"
   export SCOREBOARD_GITHUB_TOKEN="<PAT with Contents: Read/Write>"
   export SCOREBOARD_SECRET="shared-secret"
   export SCOREBOARD_TLS_CERT="$PWD/webhook.crt"
   export SCOREBOARD_TLS_KEY="$PWD/webhook.key"
   ```
   You can also set `SCOREBOARD_HOST`, `SCOREBOARD_PORT` (defaults to 0.0.0.0:8443), or `SCOREBOARD_API_BASE` if you’re targeting GHES.
3. Run the server:
   ```bash
   python tools/scoreboard_webhook.py
   ```

Give players the URL `https://<host>:<port>/scoreboard` plus the shared secret. Their game clients post to your relay, which owns the PAT and forwards events to GitHub securely.

###### Deploying the relay on Fly.io
You can host the same relay on Fly.io with the included `Dockerfile` and `fly.toml`:
1. Install the Fly CLI and log in:
   ```bash
   fly auth login
   ```
2. From the repo root, launch the app (the default name in `fly.toml` is `stackoverflow-minigame-scoreboard`; change it if needed):
   ```bash
   fly launch --copy-config --no-deploy --name <your-app-name>
   ```
3. Store the required secrets (Fly injects them as environment variables for the container):
   ```bash
   fly secrets set \
     SCOREBOARD_REPO="colesnipes/stackoverflow-minigame" \
     SCOREBOARD_GITHUB_TOKEN="ghp_..." \
     SCOREBOARD_SECRET="shared-secret"
   ```
4. Deploy:
   ```bash
   fly deploy
   ```
   Fly terminates TLS for you, so the container just serves HTTP on `0.0.0.0:$PORT` (handled automatically).

Players now target `https://<your-app-name>.fly.dev/scoreboard` with `STACKOVERFLOW_SCOREBOARD_WEBHOOK_URL`, and they reuse the same `STACKOVERFLOW_SCOREBOARD_WEBHOOK_SECRET` you set above.

## Run in a terminal without downloading locally
You can play straight from a cloud/dev container terminal so nothing lands on your personal machine:
- **GitHub Codespaces:** open the repo on GitHub, click **Code → Codespaces → Create codespace on main**, wait for the VS Code-in-browser session, then run `dotnet run` in the embedded terminal.
- **GitHub CLI:** `gh codespace create -r <owner>/<repo>` spawns a Codespace; `gh codespace ssh -c <name>` gives you a terminal where you can `dotnet run`.
- **Gitpod or similar services:** launch `https://gitpod.io/#https://github.com/<owner>/<repo>` to boot a disposable workspace and run the same CLI commands there.

All of these hosts keep the repository server-side; you only interact through a browser/SSH terminal served over HTTPS, so nothing gets cloned to your local machine.
## Runtime Flow
![Flow of Information](flowOfInformationConsoleGame.png)

> Note: Only the rendered image is provided; the PlantUML source code for this diagram is not included in the repository.
