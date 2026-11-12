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

## Learning quizzes

The **Learning quizzes** page turns what you asked about in chat into multiple-choice questions:

- After each chat answer, the App saves the chunk IDs it cited under a **topic**: the section of the first cited passage, e.g. "Tax-Free Scholarships and Fellowship Grants". Topics are grouped by tax year and saved as JSON (`quiz-history.json` in the App's data folder), so they survive restarts. Each topic keeps its 10 most recently cited chunks.
- Open the page from the menu to see your saved topics, or press **Quiz me on …** under a chat answer.
- `POST /api/quiz` (`{ topic, chunkIds[] (≤10), taxYear, plainLanguage, count = 5 }`) looks up the cited chunks. If there are fewer than `count`, it fills up from the reranked passages for the topic text. Then it makes one Claude call per passage, at most 5 at a time.
- Every question is checked: 4 distinct options, a correct index from 0 to 3, and a quote that appears in the passage. A failing question is regenerated once and then dropped, so a quiz can have fewer than 5 questions.
- After you answer, a feedback card shows whether you were right, the explanation and a citation chip that opens the passage with the quoted sentence highlighted. The last screen shows your score.

## Evaluation

The gold set, [`eval/gold.jsonl`](eval/gold.jsonl), has 52 questions:
- 34 in-scope questions across the five personas (W-2 first-timer, scholarship, dependent, gig, international).
- 6 year-specific questions (TY2026 and TY2027).
- 2 year-trap questions, which ask about 2027 while 2025 is selected.
- 10 out-of-scope questions.

Each question names its expected document and PDF pages. Each `goldAnswer` is drafted from the cited pages (`goldStatus: draft`). A person checks it against the publication and sets `goldStatus` to `approved`. Only approved answers are judged.

```bash
dotnet run --project eval/TaxWise.Eval -- --mode taxwise    # the real pipeline: retrieve → Claude → validate
dotnet run --project eval/TaxWise.Eval -- --mode baseline   # same model, no retrieval
dotnet run --project eval/TaxWise.Eval -- --rejudge eval/results/<date>-taxwise.json   # judge newly written gold answers only
```

Options: `--only q01,q08`, `--limit N` (both write a `-smoke` report), `--concurrency N` (default 3), `--judge-model`.

- The runner uses the API's `appsettings.json` and the shared `taxwise-dev` user secrets, and needs Qdrant running.
- Each question runs **once with the rerank threshold off**. The `MinRerankScore` sweep over {0.2, 0.3, 0.35, 0.4, 0.5} is scored afterwards from the saved top scores.
- The chosen threshold refuses the most out-of-scope questions while wrongly refusing at most 10% of in-scope ones.
- **Metrics:**
  - hit@4: a top-4 chunk is from an expected doc and within ±1 page of an expected page.
  - Citation validity: the passage ID is real and the quote is found, counted before the validator drops anything.
  - Answer correctness: judged by `claude-sonnet-5-5`, with 10 verdicts listed for a human spot-check.
  - Refusal recall and precision.
- Reports go to `eval/results/YYYY-MM-DD-{mode}.md` and `.json`.

**Results, 2026-11-12** ([taxwise](eval/results/2026-11-12-taxwise.md), [baseline](eval/results/2026-11-12-baseline.md)):

| | TaxWise | Plain chatbot (same model, no retrieval) |
|---|---|---|
| Retrieval hit@4 | 95% (38/40) | – |
| Citation validity (before the validator) | 100% (262/262) | – |
| Out-of-scope and year-trap questions refused | 100% (12/12) | 8.3% (1/12) |
| In-scope questions wrongly refused | 2.5% (1/40) | 0% |
| Answer correctness (judge: `claude-sonnet-5-5`) | 80% correct (32/40), 6 partial, 1 incorrect, 1 refused | 90% correct (36/40), 1 partial, 3 incorrect |
| TY2027 questions correct | 3/3 | 0/3 (says the 2027 amounts aren't published) |

`MinRerankScore` is **0.50**: 9 of the 12 out-of-scope questions are refused without calling Claude (DECISIONS #14). For "2027 standard deduction" with 2025 selected, the plain chatbot said the 2027 amounts weren't published yet, though Rev. Proc. 2026-35 came out on Nov 5. TaxWise refused.

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
