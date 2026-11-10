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
