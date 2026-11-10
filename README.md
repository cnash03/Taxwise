# Taxwise
Full Stack LLMs semester long project

TaxWise is a desktop app that helps college students understand their U.S. federal taxes, with answers cited to official IRS publications. The build plan is in [docs/BUILD_PLAN.md](docs/BUILD_PLAN.md).

## Architecture

| Project | Role |
|---|---|
| `src/TaxWise.Shared` | DTOs shared by the API and the App (no dependencies) |
| `src/TaxWise.Core` | Pure domain code: calculator, chunk model, guardrails (no SDKs, no I/O) |
| `src/TaxWise.Rag` | Voyage client, Qdrant store, retriever, Claude answerer, prompts |
| `src/TaxWise.Ingestion` | Console app: download → parse → chunk → embed → upsert |
| `src/TaxWise.Api` | ASP.NET Core minimal API. **The only place API keys live.** |
| `src/TaxWise.App` | .NET MAUI Blazor Hybrid desktop app (Mac Catalyst / Windows). Talks to the API over HTTP. |
| `tests/*` | xUnit tests |
| `eval/TaxWise.Eval` | Gold-set evaluation runner |

Dependencies point one way: `Shared ← Core ← Rag ← {Api, Ingestion, Eval}`. `App` depends only on `Shared`.

## Setup (macOS)

1. **.NET 10 SDK:** `brew install --cask dotnet-sdk` (asks for your password).
2. **MAUI workload** (only needed for the App): `sudo dotnet workload install maui`.
3. **Xcode** (only needed for the App on Mac Catalyst): install it from the App Store, then run `sudo xcode-select -s /Applications/Xcode.app` and open Xcode once to accept the license.
4. **Docker Desktop**, for the local Qdrant vector store.
5. **Secrets** (from Phase 1 on), via user-secrets. Never commit keys:
   ```
   dotnet user-secrets --project src/TaxWise.Api set ANTHROPIC_API_KEY <key>
   dotnet user-secrets --project src/TaxWise.Api set Voyage:ApiKey <key>
   ```
   Turn on GitHub secret scanning and push protection in the repo settings (Settings → Code security).

## Run

```bash
docker compose up -d                                         # Qdrant on :6333 (REST/dashboard) and :6334 (gRPC)
dotnet run --project src/TaxWise.Api --launch-profile http   # API on http://localhost:5080 (GET /health)
dotnet build src/TaxWise.App -t:Run -f net10.0-maccatalyst   # desktop app (needs Xcode); green dot = API is up
```

The App reads `ApiBaseUrl` from `src/TaxWise.App/appsettings.json` (default `http://localhost:5080`).

## Build and test

```bash
dotnet build TaxWise.CI.slnf   # everything except the MAUI App
dotnet test TaxWise.CI.slnf
```

CI (`.github/workflows/ci.yml`) runs the same two commands on `ubuntu-latest`. The App isn't part of CI because the MAUI workload is too heavy.

# 1st presentation link
https://docs.google.com/presentation/d/1-NAtO33yZJG0BExMTAKbcu3BMdrtexmxr1D42kJGFK8/edit?slide=id.p#slide=id.p

# AI usage documentation
How we use AI is documented in [docs/AI_USAGE.md](docs/AI_USAGE.md), with session-by-session detail in [docs/prompt-log.md](docs/prompt-log.md).

- **Automatic logging:** `.claude/settings.json` hooks record every Claude Code session in this repo to `docs/ai-log/raw/<git-user>/`. You need `jq` (`brew install jq`; on Windows, `winget install jqlang.jq` plus Git Bash). Commit the raw logs along with your work.
- **Updating the report:** run `/ai-report` in Claude Code after each phase or PR. It drafts prompt-log entries and asks you multiple-choice questions for anything only you know.
- **Earlier sessions:** import them with `.claude/hooks/ai-log.sh backfill ~/.claude/projects/<this-project>/<session-id>.jsonl`.
- **Other AI tools** (ChatGPT, Copilot, …) aren't captured automatically. Mention them when `/ai-report` asks.
