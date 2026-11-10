# Prompt log

One entry per AI working session, oldest first, in the format from [BUILD_PLAN §9](BUILD_PLAN.md#9-prompt-log-format-docsprompt-logmd).
Claude Code sessions are captured automatically by the project hooks (`docs/ai-log/raw/`). The `/ai-report` skill drafts the entries from those logs and asks the author for the fields only a human knows: outcome, human changes, and other tools used.
Entries are append-only. The overall summary is in [AI_USAGE.md](AI_USAGE.md).

---

### 2026-11-10 · Planning · Claude Code (claude-opus-5-5) · @cnash03 · session `be960576`
- **Role:** planning agent. It wrote the spec that the execution agent follows; it wrote no app code.
- **Prompt:** "you have access to 2 pdfs your a planning agent on how to build out this application- give me mark down that another agent can follow after you plan out how they will execute - if you have any questions give them to me in a section option i do not want to type anything"
  - Follow-up: "irs 2027 just came out can you add 2026 and 2026 information before i kick off the exectution agent"
- **Generated:** [`docs/BUILD_PLAN.md`](BUILD_PLAN.md) (564 lines). It covers locked tech decisions, phases 0–8 with acceptance criteria, the prompt-log format, cross-cutting rules, and a list of items where the execution agent must stop and ask a human (§12). After the follow-up it added §1a, which makes tax year a parameter everywhere, with TY2025, TY2026 and TY2027 calculator constants.
- **Human decisions:** 4 multiple-choice checkpoints.
  - UI host: the AI recommended a Blazor Web App. **We chose .NET MAUI Blazor Hybrid** because the team knows MAUI/.NET. The AI had pointed out that the design doc says "Blazor Web App" while the slides say "MAUI Blazor UI".
  - Vector store: Qdrant in Docker (accepted the recommendation).
  - Rerank: Voyage rerank API (accepted the recommendation).
  - Scope: all 4 features, in phases (accepted the recommendation).
- **Verified by:** it read both source PDFs in full. It loaded the `claude-api` skill to confirm the C# SDK package and model ids instead of relying on memory. For the follow-up it ran 2 web searches, then went past the IRS press releases (they leave out the head-of-household and married-filing-separately brackets) to the primary sources, [Rev. Proc. 2025-32](https://www.irs.gov/pub/irs-drop/rp-25-32.pdf) and [Rev. Proc. 2026-35](https://www.irs.gov/pub/irs-drop/rp-26-35.pdf).
- **Worked?** yes. The plan is ready for the execution agent.
- **Human change:** none yet. The plan is unedited.
- **AI limits seen:**
  - PDF tooling was missing on this Mac (`pdftotext`, `pypdf`, `poppler`). The first text extraction failed, and so did the page-range reads of the Rev. Procs. It read the source PDFs natively and installed `pypdf` in a scratch venv for the Rev. Procs. The fallbacks worked without help, but this is worth knowing before Phase 1 ingestion.
  - All tax constants are AI-drafted. The plan marks them "must be hand-verified" and stops the build for a human check before the calculator merges (§12).
  - It read the typo "2026 and 2026" as "2026 and 2027" and said so, rather than asking.
- **AI caught in our materials:**
  - Our slide 11 mockup shows $1,231.50 tax on $12,250. That number applies the brackets directly. The IRS Tax Table uses the midpoint of each $50 row, which gives about $1,235. The plan tells the build agent to follow the Tax Table, so we need to update the demo numbers.
  - The 2027 head-of-household top bracket ($661,350) is $25 below the single filer's. It looks like a typo, but the plan copies exactly what the IRS printed.
- **Tag:** success

### 2026-11-10 · Tooling · Claude Code (claude-opus-5-5) · @cnash03 · session `83171a0b`
- **Role:** ad-hoc (documentation tooling). It built the automatic AI-usage logging that this log is generated from.
- **Prompt:** "how should i go about documenting how ai was used in the project? i want it to be uatomated - should it be a cluade skill? claude agent? i need it to track everything my partner does and i do"
  - Follow-up: "Yes do that please - i have already done a planning agent so can you add that as well - here is the criteria that the professor give …" (pasted the grading rubric)
  - Follow-ups: commit to a branch → push and open a PR → "i dont see your commit in my github desktop" → switch to main and commit the logs.
- **Generated:** [`.claude/hooks/ai-log.sh`](../.claude/hooks/ai-log.sh) (hook + backfill mode with key redaction), [`.claude/settings.json`](../.claude/settings.json) (6 hook events), [`.claude/skills/ai-report/`](../.claude/skills/ai-report/SKILL.md) (`SKILL.md` + `stats.sh`), the first `prompt-log.md` entry and `AI_USAGE.md`, an AI-usage section in `README.md`, and a BUILD_PLAN §0 step 5 change so the execution agent runs `/ai-report` instead of writing log entries by hand. Committed as `2964456`, merged as PR #1 (`63d8991`).
- **Human decisions:** 1 multiple-choice checkpoint (tag and rationale for the planning session, recorded in the `be960576` entry). The AI recommended hooks over a skill or agent alone, because a skill only runs when someone remembers to call it. We accepted.
- **Verified by:** tested the hook with simulated events and by backfilling `be960576`. Confirmed redaction masks fake keys. Grepped the staged raw logs for secrets before committing. Read the course's P4 project README to align the report with the rubric.
- **Worked?** partly. The logging and the skill work, but finishing the job needed several manual steps.
- **Human change:** pushed the branch and merged PR #1 ourselves. Committed `Finance AI App.pdf` and `.idea/` by hand (`b7ddb43`).
- **AI limits seen:**
  - The push was blocked by Claude Code's auto-mode safety check, because it would send session transcripts to GitHub. The AI explained what's in the logs and handed the push to us instead of working around the block.
  - Switching to `main` with an uncommitted, still-growing log file caused a merge conflict. The AI backed up the logs, unstaged everything and fast-forwarded `main` (`fcaa8e0`). It also committed a stray `docs/ai-log/raw/.DS_Store`.
  - We couldn't find the commit in GitHub Desktop. It was on a separate branch, and the AI had to walk us through the branch switch.
  - It flagged a false "already logged" check, which turned out to be its own `head -12` truncating the output.
- **Tag:** partial. We overrode the AI's suggested "success": it worked, but we had to step in for the push, PR, merge and branch cleanup.

### 2026-11-10 · Phase 0 · Claude Code (claude-opus-5-5) · @cnash03 · session `866029d7`
- **Role:** execution agent, Phase 0 (scaffold), branch `phase-0-scaffold`. This is the first session that follows BUILD_PLAN.md as an agent-to-agent handoff.
- **Prompt:** "you are an execution agent - your plan to execute is in build_plan.md - do it - stop with questions"
  - Follow-ups: "i need to test this first before i answer any question so lets run this and see", then a paste of a failed `dotnet build` command.
- **Generated:** `TaxWise.sln` with 10 projects (Shared, Core, Rag, Ingestion, Api, App, 3 xUnit test projects, Eval), `Directory.Build.props`, `TaxWise.CI.slnf`, `.github/workflows/ci.yml`, `docker-compose.yml` (Qdrant), `.gitignore`, `GET /health` + `HealthEndpointTests`, `DependencyRuleTests` (Core may only reference Shared), and a MAUI Blazor Hybrid App with the sidebar nav, Knowledge base footer and a health dot that polls `/health`. Also a README setup/run section. 61 files, commit `50d76fb`.
- **Human decisions:** 1 checkpoint with 4 questions, all accepted as recommended. We approved the Phase 0 plan, chose a Homebrew cask for the .NET SDK, chose to scaffold the App and defer its launch check until Xcode, and agreed to start Docker. **When the AI then asked for outcome tags, we refused to answer until we had run the app ourselves**, and only tagged it after seeing the desktop App's green dot.
- **Verified by:** `dotnet build`/`dotnet test` on `TaxWise.CI.slnf` in Debug and Release (0 warnings with warnings-as-errors; 2 tests pass), `docker compose up -d` + `/healthz`, `curl localhost:5080/health`. Before Xcode was installed it compiled the App's components in a scratch Razor library and served them in a scratch browser preview. After Xcode was installed, the real Mac Catalyst App built with 0 warnings, launched, and we confirmed the green dot.
- **Worked?** yes in the end, but with a lot of human setup.
- **Human change:** we ran the two `sudo` installs (the .NET SDK cask and `dotnet workload install maui`) that the AI couldn't run, installed Xcode and ran `xcode-select`, started Docker Desktop, and tested the nav and health dot by hand. No code edits.
- **AI limits seen:**
  - Looked for `build_plan.md` at the repo root. The plan was at `docs/BUILD_PLAN.md`, and it searched until it found it.
  - Tried `brew install --cask dotnet-sdk` itself, which failed at the sudo password prompt. As a side effect, Homebrew's auto-cleanup deleted old cached versions of openssl, xz and ca-certificates. `dotnet workload install maui` also needed sudo.
  - `dotnet add package` picked `Microsoft.AspNetCore.Mvc.Testing` 11.0.0, which only supports net11.0. The AI pinned 10.0.15.
  - Wrote the first test with an xUnit v3 API (`TestContext.Current`) against the v2 template. It caught this itself before compiling.
  - **Its instructions caused a failed build.** A command in its message ended with a sentence period, `-f net10.0-maccatalyst.`. We copied the period, and the build failed with `MSB3073 … exited with code 127`. Runnable commands belong in code blocks.
  - The installed Mac Catalyst pack (27.0.10742) requires Xcode 27.1, and we have 27.2. The AI worked around it with a one-off `-p:ValidateXcodeVersion=false` and told us the real fix is `sudo dotnet workload update` (pack 27.2.10000).
  - Deviated from BUILD_PLAN §2 on purpose: `TargetFramework` stays in each csproj rather than in `Directory.Build.props`, because the multi-targeting App would break otherwise.
  - The logging hook filed 9 events under the macOS user `callennash` with no branch while Xcode was being set up, most likely because `/usr/bin/git` fails until the Xcode license is accepted. Found during `/ai-report` and merged back into `cnash03`.
- **Tag:** partial. We overrode the AI's suggested "success": every acceptance check passes, but it needed too much from us (two sudo installs, Xcode, and a broken copy-paste command).

### 2026-11-10 · Phase 1 · Claude Code (claude-opus-5-5) · @cnash03 · session `cc8e27f2`
- **Role:** execution agent, Phase 1 (ingestion), branch `phase-1-ingestion`. We asked for Phase 2. The agent found that Phase 1 hadn't been built yet and stopped to ask.
- **Prompt:** "start phase 2 of the build_plan.md"
  - Follow-ups: "approved, go ahead" (plan approval), "added a payment method, run the spot check", "yes, commit and open the PR".
- **Generated:** the ingestion pipeline, commit `262d044` (46 files, +2,640 lines), PR #3:
  - `data/sources/manifest.json` (9 IRS sources, sha256 tracking)
  - a PdfPig layout reader
  - section and Rev. Proc. chunkers in `TaxWise.Core/Chunking` (pure code)
  - `VoyageClient` with jittered retry
  - `QdrantChunkStore`
  - the ingestion CLI (`--download`, `--dry-run`, `--recreate`, `--only`, `--year`, `--spot-check`)
  - `ingest-report.json`
  - 49 new xUnit tests
- **Human decisions:** 1 checkpoint with 3 questions, all answered with the recommended or factual option. "Do Phase 1 first (Recommended)" over building Phase 2 against fakes; Docker "installed, not on PATH"; API keys "not yet". We approved the restated Phase 1 plan before any code was written. **We tagged the session partial, overriding the AI's suggested "success"**: it needed us for Voyage billing, and it changed an acceptance check we hadn't asked it to change.
- **Verified by:**
  - fetched the Voyage embeddings and rerank API reference before writing the client (BUILD_PLAN §4.2 says not to guess field names)
  - downloaded all 9 IRS PDFs and probed their fonts, layout and covers with throwaway PdfPig scripts before writing the parser
  - `dotnet build` and `dotnet test` on `TaxWise.CI.slnf`: 50 tests pass
  - a live ingest of 1,487 chunks into Qdrant; a second run without `--recreate` still gave 1,487 points (no duplicates)
  - `--spot-check-only`: §3.15 (TY2027) and §4.14 (TY2026) standard-deduction chunks ranked #1 at relevance 0.878, and the demo scholarship question returns Pub 970 Ch. 1, p. 5 at #1
- **Worked?** yes, after one acceptance check was reworded (see AI limits).
- **Human change:** added a payment method to the Voyage account. No code edits.
- **AI caught in our materials:**
  - **BUILD_PLAN is wrong about Rev. Proc. 2026-35.** The 2027 amounts are in **§3**, not §4, so the standard deduction is §3.15. The Phase 5 source comments must cite §3.xx.
  - The plan's `irs-pdf/` URLs for the W-2/W-3 and 1098-E/T instructions already serve the **2026** editions. The manifest points at the `irs-prior/…--2025.pdf` copies, and the agent's cover-year check now catches this automatically.
  - Real IRS chapter headings are a large "1." plus a title, not "Chapter 1" as the plan assumed.
- **AI limits seen:**
  - **The first chunker failed on real data.** Chunks averaged 290 tokens (target 350–700), with a quarter under 100, Pub 970 detected no chapters, and section labels were noisy. Pub 970 lost its chapters because its own running-header filter dropped the big "1." chapter number. It took 4 rounds of measuring the chunks against the real PDFs to fix.
  - Guessed two library API names wrong: PdfPig's `DocstrumBoundingBox` is really `DocstrumBoundingBoxes`, and Qdrant's `Match(field, string)` is `MatchKeyword`. Both were fixed from compiler errors, as BUILD_PLAN §4 says to.
  - `dotnet add package` again picked .NET 11 (`Microsoft.Extensions.*` 11.0.0), the same problem as Phase 0. It pinned 10.0.15.
  - **The bare "room and board" spot check failed.** The top hits were Pub 970 Ch. 7 (529 plans) and the dependency support rules. The agent made it an informational row and added two scholarship questions as the pass/fail checks. It flagged the change to us and in the PR, but it is still the agent changing its own acceptance criteria.
  - Voyage returned 429 without a payment method on the account (3 requests and 10K tokens per minute), so live ingestion was blocked until we added one. Session `0640c1d8` had predicted this.
  - **Two Claude sessions worked on the same branch at once.** Mid-build, this agent noticed files changing that it hadn't edited (`UserSecretsId`, README). It inspected them, showing the stored secret only as `****`, and built on them rather than overwriting them.
- **Tag:** partial

### 2026-11-10 · Phase 1 setup · Claude Code (claude-opus-5-5) · @cnash03 · session `0640c1d8`
- **Role:** ad-hoc (secrets and environment setup for Phase 1). It ran alongside `cc8e27f2` on the same branch.
- **Prompt:** "help me set up the voyager api keys and what not that i need for phase 1 of the project"
  - Follow-up: "yes commit it"
- **Generated:** a shared `UserSecretsId` (`taxwise-dev`) for `TaxWise.Api` and `TaxWise.Ingestion`, `src/TaxWise.Ingestion/Properties/launchSettings.json` (runs ingestion as Development so user secrets load), and a README line. Commit `90a7b15`.
- **Human decisions:** no checkpoints. We followed its steps: created the Voyage key and saved it in our own terminal.
- **Verified by:** listed the existing secrets with values redacted. Its reply says the CI build still passes.
- **Worked?** yes. The key loaded in `cc8e27f2` with no further setup.
- **Human change:** none.
- **AI limits seen:**
  - **It told us to save the key in our own terminal, not in chat, "so it doesn't end up in the AI logs".** The logs confirm the key never appears.
  - **It predicted the Voyage rate limit** (3 requests and 10K tokens per minute without a payment method), and said one 128-chunk batch would get stuck. That is exactly what happened in `cc8e27f2`.
  - Said Docker "isn't installed on this machine" and gave an install command. Docker Desktop was installed and running, just not on that shell's PATH.
  - **Its commit swept in another session's work.** It said `90a7b15` contained "only the four files I changed", but the Ingestion `.csproj` hunk also carried `cc8e27f2`'s in-progress PdfPig and Hosting package references. The commit was harmless (both were needed), but its claim was wrong. Found during `/ai-report`.
  - A follow-up "push it" wasn't acted on before the session ended. `e3c316a6` pushed the branch later.
- **Tag:** success

### 2026-11-10 · Docs · Claude Code (claude-opus-5-5) · @cnash03 · session `e3c316a6`
- **Role:** ad-hoc (teammate onboarding docs).
- **Prompt:** "give me a setupguide for my partener - clear concise and not dependent on mac"
  - Follow-ups: "commit and push the setup guide", then "fill in the url and put it on main".
- **Generated:** [`docs/SETUP.md`](SETUP.md) (110 lines: tools per OS, clone and build, API keys, run order, AI logging with Git Bash on Windows, troubleshooting) and a README link. Commit `904db03`, which was the first push of `phase-1-ingestion`.
- **Human decisions:** no checkpoints. We later dropped the "put it on main" request.
- **Verified by:** nothing was run. It said plainly that it hadn't tested the steps on Windows or Linux.
- **Worked?** yes for the guide. The final request (fill in the URL, move the guide to `main`) wasn't done before the session ended, and we dropped it.
- **Human change:** none.
- **AI limits seen:**
  - Pushed to the current branch (`phase-1-ingestion`), not `main` where the partner would look. It flagged this right after pushing, offered to move the guide, and pointed out that merging the branch would drag in unfinished ingestion work.
  - Left `<repo-url>` as a placeholder and flagged it. It is still in the file.
  - Warned that the guide's ingestion step only works once the Phase 1 code is pushed.
- **Tag:** success

### 2026-11-10 · Evaluation · Claude Code (claude-opus-5-5) · @cnash03 · session `b1c9df54`
- **Role:** evaluation agent for Phases 0 and 1, which then became the fixer for two of its own findings. Branch `phase-1-ingestion`. This entry was written by `/ai-report` inside the session it describes, once the work was done.
- **Prompt:** "evaluate the first 2phases of the build"
  - Follow-ups: "fix issues 1 and 2 and re-ingest", "yes commit and update the PR".
- **Generated:**
  - An evaluation against BUILD_PLAN §4 that read the code, re-ran the checks and **profiled the 1,487 stored chunks straight from Qdrant**. It reported 6 issues, ranked by importance. The top two were:
    1. 99 chunks (6.7%) were rows from the IRS Tax Table and EIC Table. They embed poorly, and retrieving them would let the model read tax amounts off a row, against BUILD_PLAN §10.
    2. 584 chunks (39%) had a merged section label such as `Gambling Winnings · Social Security Benefits · …`, which Phase 2 would show in citations.
  - The fixes:
    - `SectionChunker` now skips lookup tables up to the next major heading or chapter.
    - `Chunk.Section` is now the section with the most text in the chunk, and a new `Chunk.Sections` field (payload `sections`) holds the full list. All section names still go into the text that gets embedded.
    - 7 new tests (57 in total).
    - A regenerated `ingest-report.json`.
    - Commit `02441ed` (11 files, +212/−55).
- **Human decisions:**
  - The evaluation recommended fixing issues 1 and 2 before merging PR #3 and called issues 3–6 optional. We agreed.
  - The AI suggested tagging the session "success". **We tagged it partial**, because the commit and push didn't land within the session.
- **Verified by:**
  - `dotnet build` (0 warnings) and `dotnet test`: 50 tests before the fixes, 57 after.
  - CI on PR #3.
  - A `--dry-run`, then the real `--recreate --spot-check` re-ingest: **1,377 points, matching the report** (only i1040 lost 73 chunks and p17 lost 37).
  - The spot checks pass: §3.15 for TY2027, §4.14 for TY2026, and both scholarship questions find Pub 970 p. 5.
  - After the re-ingest, it scrolled the whole collection again: 0 mostly-numeric chunks, 0 `·`-joined section labels, 0 lookup-table sections left.
- **Worked?** The evaluation and the fixes, yes. Landing the change, partly:
  - Claude Code's auto-mode safety check blocked the agent's `git commit && git push`, so we ran the commands by hand.
  - We updated the PR description first, but at that point the code it described wasn't on GitHub yet.
  - Our first `git commit` didn't land; the agent noticed by checking `git log` rather than trusting "done". We don't know why it failed.
  - The second commit landed as `02441ed`, but the push didn't run, so the branch is still one commit ahead of `origin`.
- **Human change:** ran the commit, push and `gh pr edit` commands. No code edits.
- **AI limits seen:**
  - **It evaluated the code the previous session wrote and found real problems that session missed or didn't flag.** Phase 1's own report listed the 2-token `"96,000"` chunk and the 798-token table chunk among the shortest and longest chunks, but neither the agent nor we treated them as a problem. The evaluation only found the scale (99 table chunks, 584 merged labels) by querying Qdrant, not by reading the code.
  - It read "the first 2 phases" as Phases 0 and 1, the two that exist, rather than BUILD_PLAN's Phase 2 (the query pipeline). It said so at the top of its report.
  - It didn't launch the MAUI app, so the green health dot from Phase 0 wasn't re-verified. It said so in the report.
  - It found a problem it didn't fix: p17's back matter (Taxpayer Bill of Rights, How To Get Tax Help) is labelled "Chapter 14" in citations. It's left for Phase 2.
  - It said the uncommitted AI logs should go in with the phase (BUILD_PLAN §0 step 5); they are in `02441ed`.
- **Tag:** partial
