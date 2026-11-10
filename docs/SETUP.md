# TaxWise Setup Guide

Works on Windows, macOS, and Linux. Commands are the same on every OS. Run them from the repo root in any terminal (PowerShell, Git Bash, or Terminal).

## 1. Install the tools

| Tool | Why | Get it |
|---|---|---|
| **Git** | clone the repo (Git Bash on Windows also runs the AI-log hooks) | https://git-scm.com/downloads |
| **.NET 10 SDK** | build and run everything | https://dotnet.microsoft.com/download/dotnet/10.0 |
| **Docker Desktop** | runs the Qdrant vector database | https://www.docker.com/products/docker-desktop |
| **jq** | lets the Claude Code hooks log AI usage | Windows: `winget install jqlang.jq` · macOS: `brew install jq` · Linux: `sudo apt install jq` |
| **IDE** (optional) | Rider, Visual Studio 2022+, or VS Code with C# Dev Kit | |

Check the installs:

```
dotnet --version     # should start with 10.
docker --version
jq --version
```

**Only if you'll run the desktop App:** install the MAUI workload. On Windows, run the terminal as Administrator. On macOS, prefix the command with `sudo` and also install Xcode.

```
dotnet workload install maui
```

The App runs on Windows and macOS only. On Linux you can still work on everything else.

## 2. Clone and build

```
git clone <repo-url>
cd Taxwise
dotnet build TaxWise.CI.slnf
dotnet test TaxWise.CI.slnf
```

`TaxWise.CI.slnf` is everything except the App, so it builds without the MAUI workload. If the tests pass, your machine is set up.

## 3. Add the API keys

Get the Anthropic and Voyage keys from your teammate in a private message. Never commit them or paste them into code. They go in .NET user-secrets, which are stored outside the repo:

```
dotnet user-secrets --project src/TaxWise.Api set ANTHROPIC_API_KEY <key>
dotnet user-secrets --project src/TaxWise.Api set Voyage:ApiKey <key>
```

The API and Ingestion share one secrets store, so you set each key only once. Check both are there (this prints the values, so don't share the output):

```
dotnet user-secrets --project src/TaxWise.Api list
```

The Anthropic key is only used by `POST /api/ask`. Local runs use Claude Sonnet 5.5 to keep testing cheap (`appsettings.Development.json`); the deployed API uses Opus 5.5.

## 4. Run it

Start Docker Desktop first, then:

```
docker compose up -d
```

This starts Qdrant. Its dashboard is at http://localhost:6333/dashboard.

**Load the IRS documents into Qdrant** (first time, or after chunking changes):

```
dotnet run --project src/TaxWise.Ingestion -- --download --spot-check
```

Add `--dry-run` to parse and chunk without calling Voyage or Qdrant. Add `--recreate` to wipe the collection and start fresh. Run with an unknown flag (for example `--help`) to see every option.

**Start the API:**

```
dotnet run --project src/TaxWise.Api --launch-profile http
```

Check http://localhost:5080/health, then ask a real question to confirm the Anthropic key works:

```
curl -X POST http://localhost:5080/api/ask -H "content-type: application/json" -d "{\"question\":\"Is my scholarship taxable?\",\"plainLanguage\":false,\"taxYear\":2025}"
```

You should get an answer citing Pub 970. In PowerShell, use `curl.exe` instead of `curl`.

**Start the desktop App** in a second terminal, with the API still running:

```
# Windows
dotnet build src/TaxWise.App -t:Run -f net10.0-windows10.0.19041.0

# macOS
dotnet build src/TaxWise.App -t:Run -f net10.0-maccatalyst
```

A green dot in the App means it's connected to the API.

## 5. Claude Code and AI logging

If you use Claude Code in this repo, the hooks in `.claude/settings.json` automatically log each session to `docs/ai-log/raw/<your-git-username>/`. To make that work:

- Have `jq` installed (step 1). On Windows, also have Git Bash, because the hook is a bash script.
- Set your git name, which becomes your log folder: `git config user.name "<your-github-username>"`
- Commit your log files along with your code.
- After each phase or PR, run `/ai-report` in Claude Code to update `docs/AI_USAGE.md`.

If you use other AI tools (ChatGPT, Copilot, …), mention them when `/ai-report` asks.

## Troubleshooting

| Problem | Fix |
|---|---|
| `Connection refused` on port 6334 | Docker isn't running, or you skipped `docker compose up -d`. |
| `401` or `Unauthorized` from Voyage/Anthropic | Key missing or wrong. Run `dotnet user-secrets --project src/TaxWise.Api list`. |
| `/api/ask` returns `502` | Usually the Anthropic key is missing or wrong. The API log line says which. |
| Ports 5080, 6333, or 6334 already in use | Stop whatever is using them, or run `docker compose down` and try again. |
| App build fails with a workload error | Run `dotnet workload install maui` (as Administrator on Windows). |
| No AI logs appear | Check that `jq --version` works. On Windows, check that Git Bash is installed. |
