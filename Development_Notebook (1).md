**CSCI-310 Development Notebook**

-----
**Guideline:** 

- Please document all your development activities, whether you use any AI coding tool or not. You might mix your manual coding or AI tool usage. Just document the entire process. 
  - If this is a team project or assignment, list all team members’ names in the “Name” field. For each iteration, record the name of the person who contributed any part of the work in the “What do you do?” field.
- Any interactions with AI coding tools such as ChatGPT, Gemini, Copilot, and others must capture the full conversation history. 
- Use the format below to record your development activities in a clear and consistent manner. 
  - Adding more iteration sections if needed.
-----
#### Name: Harrison Sternberg, Cole Snipes
#### Project/Assignment: Stackoverflow Game
##### Problem/Task: Create a console game using C#.
##### Development Log
- **Iteration 1:**
  - **Goal/Task/Rationale:**
      Set up Skeleton

  - **What do you do?** 

    Asked ChatGPT to build the first playable shell based on our “StackOverflow Doodle Jump” pitch and scaffold the main classes (player, world, renderer).  
        – Cole S.  
    Initialized the repo, created the .NET project, and wired up the build so we could actually run the placeholder loop.  
        – Harrison S.

- **Response/Result:**

    We ended up with a rough prototype that draws a static board, accepts keyboard input, and proves the concept works in a console window.

- **Your Evaluation:** The skeleton compiles and gave us a baseline to iterate on, so we kept the structure and moved on to real gameplay.


- **Iteration 2:**
  - **Goal/Task/Rationale:**
      Make runs persistent and stand up the online scoreboard.

  - **What do you do?** 

    Pair-programmed the `Scoreboard` class so every death/win writes a JSONL entry and forwards it to the Fly webhook.  
        – Harrison & Cole  
    Built the Python webhook service + SQLite schema, then added the GitHub dispatcher bridge.  
        – Cole S.  
    Wrote a tiny CLI (`forward_payload.py`) to replay payloads into the remote service for testing.  
        – Harrison S.

 

- **Response/Result:**

    Local runs show up immediately in `scoreboard.jsonl`, the webhook echoes them into SQLite on Fly, and the GitHub dispatch fires whenever the server is reachable.

- **Your Evaluation:** Persistent storage works and we can inspect history without leaving the game. Keeping the JSONL file felt redundant once the DB was up, but it’s still handy for offline play, so we left it.

- **Iteration 3:**
  - **Goal/Task/Rationale:**
      Polish the player experience (UI, safety, profanity filtering).

  - **What do you do?** 

    Added `ConsoleSafe` wrappers so HUD rendering and menu input stop crashing on tiny terminals.  
        – Harrison S.  
    Created `Utils/ProfanityFilter` and hooked it into the initials prompt so arcade-style three-letter names stay clean.  
        – Cole S.  
    Reworked the leaderboard viewer so it can fetch the remote scores at any time and handles HTTP failures gracefully.  
        – Harrison & Cole

- **Response/Result:**

    The menu no longer explodes when stdout is redirected, we block the obvious three-letter profanity combos, and players can peek at rankings without finishing a run first.

- **Your Evaluation:** All wins, so we kept the changes. Only note is that the filter list will need periodic updates.

- **Iteration 4:**
  - **Goal/Task/Rationale:**
      Modernize the codebase layout and ops tooling.

  - **What do you do?** 

    Broke the single-folder C# project into `Core`, `Gameplay`, `Rendering`, `Scoreboard`, and `UI` directories, updated the csproj globs, and re-ran builds.  
        – Cole S.  
    Hardened `Tracing` with a bounded queue, retryable log writer, and shutdown hooks so background logging can’t hang the process.  
        – Harrison S.  
    Cleaned up the Fly deployment flow (documented `fly deploy`, refreshed Dockerfile) and validated the webhook template loader.  
        – Harrison & Cole

- **Response/Result:**

    Builds are faster to navigate, tracing survives noisy sessions without eating RAM, and Fly deploys now have a single documented path.

- **Your Evaluation:** This organization feels much easier to maintain. No regressions after the restructure, so we’re locking it in.
