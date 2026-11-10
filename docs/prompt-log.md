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
