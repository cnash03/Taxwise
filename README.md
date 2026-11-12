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

On Windows or Linux, or if you're new to the project, follow [docs/SETUP.md](docs/SETUP.md) instead.

1. **.NET 10 SDK:** `brew install --cask dotnet-sdk` (asks for your password).
2. **MAUI workload** (only needed for the App): `sudo dotnet workload install maui`.
3. **Xcode** (only needed for the App on Mac Catalyst): install it from the App Store, then run `sudo xcode-select -s /Applications/Xcode.app` and open Xcode once to accept the license.
4. **Docker Desktop**, for the local Qdrant vector store.
5. **Secrets** (from Phase 1 on), via user-secrets. The API and Ingestion share one secrets store (`UserSecretsId` `taxwise-dev`), so set each key once. Never commit keys:
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

If the App build fails with *"This version of .NET for MacCatalyst requires Xcode 27.1"* and you have a newer Xcode, add `-p:ValidateXcodeVersion=false` to the App command.

## Ask a question (chat)

The **Ask a question** page sends `POST /api/ask` and shows cited answers:

- Each paragraph has numbered citation chips (`1 · Pub 970 · Ch. 1 · p. 5 · TY 2025`). Click one to see the IRS passage in the right-hand panel with the quoted sentence highlighted, then **Open page N in the PDF**.
- **Plain-language mode** (header toggle, remembered between launches) asks Claude for simpler wording from the same passages. In that mode, tax terms get hover definitions from [`data/glossary.json`](data/glossary.json). Those definitions are written and reviewed by people, not generated. Set `reviewed: true` on each entry after checking it against its `chunkId`.
- The **Tax year** picker defaults to 2025 for chat. Years without their full IRS guides show a banner naming the Rev. Proc. the answers rely on.
- **Quiz me on …** passes the chat's cited chunk IDs to the quizzes page (Phase 6).

## Explain my form

The **Explain my form** page walks through a sample W-2 and 1098-T, one box at a time:

- The sample boxes, their values and where each one goes (`goesTo`) are in [`data/forms/`](data/forms). A person checks each `goesTo` against the TY2025 instructions. The model explains the mapping but never decides it, and the API always returns `goesTo` from the data.
- Clicking a box sends `POST /api/forms/explain` (`{ form, boxId, plainLanguage }`). The API searches only that box's `sourceDocs` with its `retrievalQuery`, refuses below the rerank threshold without calling Claude, and runs the same citation validator as chat. Explanations are cached in memory per form, box and mode.
- `GET /api/forms/{w2|1098t}` returns a sample. Set `Forms:Directory` when the API runs outside the repo.
- The page stays on tax year 2025 until the IRS releases the 2026 form instructions. **Upload mine** is shown but disabled (stretch goal).

## Scenario calculator

The **Scenario calculator** page estimates a federal refund or amount owed from one W-2 (wages, withholding, filing status, dependent or not):

- All the math is in `TaxWise.Core/Calculator` (pure, `decimal` only). Each tax year is one file (`TaxYear2025.cs`, `TaxYear2026.cs`, `TaxYear2027.cs`). Every constant cites its IRS source in a comment. To add a year, add a file.
- Taxable income under $100,000 uses the **Tax Table method**: the brackets are applied to the midpoint of the $50 row and the result is rounded to the dollar, the way the IRS builds the table. At $100,000 and up it uses the Tax Computation Worksheet formula. TY2025 is checked against every row of the official 2025 Tax Table ([`tests/TaxWise.Core.Tests/Calculator/Data/tax-table-2025.csv`](tests/TaxWise.Core.Tests/Calculator/Data/tax-table-2025.csv)). The 2026 and 2027 tables aren't published yet, so those results are labelled **Estimate**.
- `POST /api/calculator` returns the step table plus a short explanation from Claude, cited to the year's IRS passages. Claude only gets the finished numbers. The **dollar guardrail** rejects any explanation with a dollar amount that isn't in the calculation. A rejected explanation is retried once; if the retry fails too, the page shows a canned explanation built from the numbers.
- **Compare years** (header toggle) calls `POST /api/calculator/compare`, which runs the same inputs for 2025, 2026 and 2027 and returns numbers only, with no Claude call.
- Not included: credits, the 2025–2028 tip and overtime deductions, other income and itemized deductions. The page says so under every result.

## Ingest the IRS documents

The sources are listed in [`data/sources/manifest.json`](data/sources/manifest.json). The PDFs download to `data/sources/pdf/` (gitignored).

```bash
dotnet run --project src/TaxWise.Ingestion -- --download --dry-run   # download, parse, chunk, write ingest-report.json (no keys needed)
dotnet run --project src/TaxWise.Ingestion -- --spot-check           # embed with Voyage, upsert into Qdrant, run the acceptance searches
```

Options: `--only <docId>`, `--year <2025|2026|2027>`, `--recreate` (drop the collection first), `--spot-check-only`, `--qdrant-host <h> --qdrant-api-key <k>` (Qdrant Cloud).

- **Edition check.** Each run prints the tax year read from each PDF's cover (or, for a Rev. Proc., from its section heading). A mismatch stops the run before anything is embedded. `irs.gov/pub/irs-pdf/` always serves the newest edition, so when the IRS posts a new year, point the manifest at the `irs-prior/` copy. The W-2/W-3 and 1098-E/T instructions already point there.
- **Changed files.** The manifest stores each PDF's sha256. If a re-download doesn't match, the run stops so a human can check the new edition.
- **Re-runs are idempotent.** Point ids derive from the chunk id, and each (document, year) is replaced as a whole, so re-running never duplicates or leaves stale chunks.
- **Sanity check.** `data/sources/ingest-report.json` has chunk counts per document, average size, and the 10 shortest and longest chunks.
- **Voyage rate limits.** A Voyage account without a payment method is capped at 3 requests and 10K tokens per minute, which can't fit even one embedding batch. Add a payment method in the Voyage dashboard; the free token allowance still applies.

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
