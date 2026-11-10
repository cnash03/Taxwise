# TaxWise Build Plan (for the execution agent)

> **Who this is for:** an AI coding agent (Claude Code / Copilot) that will build TaxWise from an empty repo.
> **Source specs:** `Idea 1 TaxWise — RAG Tax Learning Assistant.pdf` (design doc) and `Finance AI App.pdf` (slides + Figma mockups) in the repo root. When this plan and the PDFs disagree, **this plan wins** (it records decisions made after the PDFs).
> **Written:** 2026-11-10. Final demo: week of Nov 30. Report: Dec 10.

---

## 0. How to work through this plan

Follow the team's agent workflow (slide 5):

1. **Do one phase at a time.** Each phase lives on its own branch, `phase-N-short-name`, and ends with a PR.
2. **Before you write code**, restate the phase's tasks and acceptance criteria in a short plan and stop for human approval. The approval is a human checkpoint: the human checks scope and tax rules.
3. **Build** the phase.
4. **Verify** the phase. Run `dotnet build`, `dotnet test` and the phase's acceptance checks. If a check fails, fix it and run the checks again. Don't open the PR while anything is red.
5. **Log the session.** Project hooks already record every Claude Code session in `docs/ai-log/raw/`. Don't write prompt-log entries by hand. When the phase's PR is ready, run the `ai-report` skill (`/ai-report`). It adds the entry to `docs/prompt-log.md` (format in §9) and asks the human for the outcome tag and what they changed. Commit `docs/ai-log/raw/` with the phase.
6. **Never hard-code secrets** and never commit them. Never make up tax numbers: every constant in the calculator must cite its IRS source line in a code comment, and a human hand-checks it.

If something in this plan is ambiguous, pick the simplest option that satisfies the acceptance criteria and note it in the PR description. Don't stall.

---

## 1. Locked decisions

| Area | Decision |
|---|---|
| Runtime | **.NET 10** (LTS). C# end to end. |
| IDE | JetBrains Rider on macOS. Visual Studio also works. |
| UI host | **.NET MAUI Blazor Hybrid** desktop app, targets `net10.0-maccatalyst` and `net10.0-windows10.0.19041.0`. |
| Backend | **ASP.NET Core Web API** (minimal APIs). The desktop app calls it over HTTP. **API keys live only in the API, never in the desktop app.** |
| Vector store | **Qdrant in Docker** locally, Qdrant Cloud free tier for the deployed API. Accessed through Semantic Kernel's Qdrant connector / `Microsoft.Extensions.VectorData`. |
| Embeddings | **Voyage AI** `voyage-3.5` (1024-dim). `input_type: "document"` at ingestion, `"query"` at query time. |
| Reranker | **Voyage rerank API**, `rerank-2.5`. Vector top 8 → rerank → keep top 4. |
| LLM | **Claude via the official Anthropic C# SDK** (NuGet package `Anthropic`). Model `claude-opus-5-5`. |
| PDF parsing | **PdfPig** (`UglyToad.PdfPig`). |
| Tests | **xUnit**. |
| Hosting | API on **Azure App Service** (Azure for Students). The desktop app is distributed as a local build for the demo. |
| Tax years | **2025, 2026 and 2027.** TY2025 has the full knowledge base. TY2026 and TY2027 have calculator constants plus their inflation-adjustment Rev. Procs. See §1a. |
| Scope | All 4 core features (cited chat with plain-language mode, Explain my form, scenario calculator, quizzes), plus guardrails, an eval harness and deployment. Stretch goals (§11) are **out of scope** unless a human asks for them. |

---

## 1a. Tax-year support (added 2026-11-10)

The IRS released the TY2027 inflation adjustments (Rev. Proc. 2026-35) on Nov 5, 2026. The TY2026 adjustments came out in Oct 2025 (Rev. Proc. 2025-32). The full TY2026 publications (Pub 17, 970, 501, 519, 2026 Form 1040 instructions with Tax Table) usually come out **Dec 2026 – Jan 2027**. The TY2027 publications won't exist until late 2027.

| | TY2025 | TY2026 | TY2027 |
|---|---|---|---|
| What it is | Returns filed in spring 2026 | **Returns students file in spring 2027** | Next year's planning |
| Calculator constants | Rev. Proc. 2025-32 §3 (OBBBA-amended) + 2025 Form 1040 instructions | Rev. Proc. 2025-32 §4 | Rev. Proc. 2026-35 §4 |
| Official Tax Table published? | Yes (2025 i1040) | Not yet. Compute it with the Tax Table method and label the result **"estimate"** | No. Same as TY2026 |
| Knowledge base for chat | Full (all 7 docs in §4.1) | Rev. Proc. 2025-32 only, until the 2026 pubs come out | Rev. Proc. 2026-35 only |
| Default in the UI | Chat and Explain my form | **Calculator** | Selectable |

Rules:
- **Tax year is a first-class parameter everywhere.** That covers `AskRequest`, form explain, calculator and quiz. The App has a single **Tax year** picker in the header (2025 / 2026 / 2027) and remembers the choice in `Preferences`. Chat and forms default to 2025, the calculator to 2026.
- **One Qdrant collection, `taxwise`, holds all years.** Filter on the `taxYear` payload. Ingestion takes `--year`. When the IRS posts the 2026 publications, add them to the manifest with `taxYear: 2026` and re-run `--year 2026`. No code change is needed. Then switch the chat default to 2026.
- When a year doesn't have its full publications yet, the chat shows a banner: *"Full IRS guides for {year} aren't out yet. Answers use the IRS inflation-adjustment notice ({Rev. Proc.}) and may be limited."*
- Explain my form stays TY2025 until the 2026 Form W-2, 1098-T and 1040 instructions are released, because line numbers can change. The tax-year picker is disabled on that page and shows a tooltip explaining why.

---

## 2. Solution layout

```
Taxwise/
├─ TaxWise.sln
├─ Directory.Build.props          # net10.0, Nullable enable, TreatWarningsAsErrors, LangVersion latest
├─ .gitignore                     # dotnet template + .idea/ + data/qdrant/ + **/appsettings.Development.local.json
├─ docker-compose.yml             # qdrant:latest on 6333/6334, volume ./data/qdrant
├─ README.md                      # setup + run instructions (update every phase)
├─ docs/
│  ├─ BUILD_PLAN.md               # this file
│  └─ prompt-log.md
├─ data/
│  ├─ sources/manifest.json       # list of source PDFs (§4.1); the PDFs themselves are downloaded, not committed
│  ├─ glossary.json               # plain-language term definitions (§5.2)
│  ├─ forms/w2-sample.json        # sample form values + box definitions (§6)
│  └─ forms/1098t-sample.json
├─ src/
│  ├─ TaxWise.Shared/             # DTOs/contracts shared by Api and App (no dependencies)
│  ├─ TaxWise.Core/               # domain: TaxCalculator, chunk model, guardrail validators (no I/O, no SDKs)
│  ├─ TaxWise.Rag/                # Voyage client, Qdrant store, retriever, Claude answerer, prompts
│  ├─ TaxWise.Ingestion/          # console app: download → parse → chunk → embed → upsert
│  ├─ TaxWise.Api/                # ASP.NET Core minimal API
│  └─ TaxWise.App/                # .NET MAUI Blazor Hybrid desktop app
├─ tests/
│  ├─ TaxWise.Core.Tests/         # calculator + validators (pure unit tests)
│  ├─ TaxWise.Rag.Tests/          # chunker, prompt building, citation validation (Voyage/Claude faked)
│  └─ TaxWise.Api.Tests/          # WebApplicationFactory endpoint tests with fakes
└─ eval/
   ├─ TaxWise.Eval/               # console app that runs the gold set (§8)
   ├─ gold.jsonl
   └─ results/                    # generated reports (committed, used in the final report)
```

Dependency direction: `Shared ← Core ← Rag ← {Api, Ingestion, Eval}`. `App` depends only on `Shared`. **Core must not reference any SDK.**

---

## 3. Configuration and secrets

`TaxWise.Api` and `TaxWise.Ingestion` read these settings:

```jsonc
// appsettings.json (committed; no secrets)
{
  "Anthropic": { "Model": "claude-opus-5-5", "Effort": "medium", "MaxTokens": 4000 },
  "Voyage":    { "EmbedModel": "voyage-3.5", "RerankModel": "rerank-2.5", "BaseUrl": "https://api.voyageai.com/v1/" },
  "Qdrant":    { "Host": "localhost", "Port": 6334, "UseHttps": false, "Collection": "taxwise" },
  "Retrieval": { "VectorTopK": 8, "RerankTopK": 4, "MinRerankScore": 0.35 },
  "TaxYears":  {
    "Supported": [2025, 2026, 2027],
    "FullKnowledgeBase": [2025],          // add 2026 once the 2026 pubs are ingested
    "DefaultChat": 2025, "DefaultCalculator": 2026
  }
}
```

- Secrets come from `dotnet user-secrets` in development and App Service settings in Azure: `ANTHROPIC_API_KEY`, `Voyage:ApiKey`, `Qdrant:ApiKey` (cloud only).
- The App reads `ApiBaseUrl` from its own `appsettings.json` (embedded resource). Default: `http://localhost:5080`.
- Add GitHub secret scanning / push protection (repo settings) and mention it in the README.
- `MinRerankScore` is a starting guess. **Calibrate it in Phase 7** on the gold set and record the chosen value in the eval report.

---

## 4. Phase 0 and Phase 1: scaffold and ingestion

### Phase 0: Scaffold (target Nov 11)

Tasks:
1. Create the solution and projects from §2. Install the MAUI workload with `dotnet workload install maui`. Mac Catalyst needs Xcode; note this in the README.
2. Add `docker-compose.yml` for Qdrant. Add the `.gitignore`.
3. `docs/prompt-log.md` already exists, with the planning session logged. Leave it alone; `/ai-report` maintains it.
4. Add a GitHub Actions workflow `ci.yml` that runs `dotnet build` and `dotnet test` on `ubuntu-latest`. It excludes `TaxWise.App`: build `TaxWise.CI.slnf`, a solution filter without the App, because the MAUI workload is heavy.
5. Add a `/health` endpoint to the API, and an App home shell with the left nav from the mockups: *Ask a question · Explain my form · Scenario calculator · Learning quizzes* + the "Knowledge base" footer.

**Acceptance:** `dotnet build TaxWise.CI.slnf` and `dotnet test` pass. `docker compose up -d` starts Qdrant. The App launches on macOS and shows the nav shell. The App calls `/health` and shows a green dot when the API is up.

### Phase 1: Ingestion pipeline (target Nov 13)

#### 4.1 Sources (`data/sources/manifest.json`)

| docId | Title | URL |
|---|---|---|
| `p17` | Pub 17, Your Federal Income Tax | https://www.irs.gov/pub/irs-pdf/p17.pdf |
| `p970` | Pub 970, Tax Benefits for Education | https://www.irs.gov/pub/irs-pdf/p970.pdf |
| `p501` | Pub 501, Dependents, Standard Deduction, and Filing Information | https://www.irs.gov/pub/irs-pdf/p501.pdf |
| `p519` | Pub 519, U.S. Tax Guide for Aliens | https://www.irs.gov/pub/irs-pdf/p519.pdf |
| `i1040` | Form 1040 Instructions (incl. Tax Table) | https://www.irs.gov/pub/irs-pdf/i1040gi.pdf |
| `iw2` | General Instructions for Forms W-2 and W-3 | https://www.irs.gov/pub/irs-pdf/iw2w3.pdf |
| `i1098t` | Instructions for Forms 1098-E and 1098-T | https://www.irs.gov/pub/irs-pdf/i1098et.pdf |
| `rp-25-32` | Rev. Proc. 2025-32, TY2026 inflation adjustments (taxYear **2026**) | https://www.irs.gov/pub/irs-drop/rp-25-32.pdf |
| `rp-26-35` | Rev. Proc. 2026-35, TY2027 inflation adjustments (taxYear **2027**) | https://www.irs.gov/pub/irs-drop/rp-26-35.pdf |

Each manifest entry has `docId`, `title`, `url`, `taxYear`, `kind` (`publication` | `instructions` | `rev-proc`), `sha256` (filled in on first download), and an optional `prior-year` URL in case the current URL is serving a different year.

**Check the tax year printed on each PDF's cover.** If it doesn't match the entry's `taxYear`, stop and flag it to the human. The `irs.gov/pub/irs-pdf/` URLs always serve the *latest* edition, so after the IRS posts the 2026 publications the 2025 entries must point at the `irs-prior/` copies (e.g. `https://www.irs.gov/pub/irs-prior/p17--2025.pdf`).

**Rev. Proc. chunking:** ingest only §4 (the year's adjusted amounts) of each Rev. Proc. Chunk per subsection (`.01 Tax Rate Tables`, `.15 Standard Deduction`, …) and keep each tax rate table as a single chunk. Rev. Proc. 2025-32 §3 restates the OBBBA-amended **2025** amounts. Ingest it as an extra chunk group tagged `taxYear: 2025`.

#### 4.2 Pipeline (`TaxWise.Ingestion`)

`dotnet run --project src/TaxWise.Ingestion -- [--download] [--recreate] [--only p970]`

1. **Download** the PDFs to `data/sources/pdf/` (gitignored), skipping files that are already present when the sha256 matches.
2. **Parse** each PDF with PdfPig, page by page. Use the `NearestNeighbourWordExtractor` and `DocstrumBoundingBox` page segmenter so multi-column pages read in the right order. Keep page numbers. Make them **1-based physical page indexes**, because those are what `#page=N` opens. Store the printed page label separately if you can detect it.
3. **Detect headings.** A text block is a heading when its font size is clearly above the body median for the document, or it is bold and short (fewer than 12 words), or it matches `^Chapter \d+`. Keep a running `chapter` and `section` breadcrumb.
4. **Chunk by section.**
   - Target 350–700 tokens per chunk. Approximate tokens as `words × 1.33`.
   - Overlap 60 tokens with the previous chunk in the same section.
   - Never cross a chapter boundary.
   - Prefix each chunk with its breadcrumb before embedding, e.g. `"Pub 970 > Chapter 1 > Scholarships and fellowships\n\n…"`.
   - Keep table rows together. If a block has 3 or more numeric columns, treat it as a table and don't split it.
5. **Chunk model** (`TaxWise.Core/Chunk.cs`):
   ```csharp
   public sealed record Chunk(
       string Id,            // deterministic: $"{docId}-ty{taxYear}-p{pageStart}-{ordinal:D3}"
       string DocId, string DocTitle, int TaxYear,
       string? Chapter, string Section,
       int PageStart, int PageEnd,
       string Text);         // without the breadcrumb prefix
   ```
6. **Embed** in batches of up to 128 texts with `input_type: "document"`. Retry with exponential backoff on 429 and 5xx responses.
7. **Upsert** into Qdrant, collection `taxwise` (all years), cosine distance, 1024 dims. The point id is a deterministic GUID derived from `Chunk.Id`, which makes re-runs idempotent. Store every chunk field as payload, with payload indexes on `docId` and `taxYear`.
8. Write `data/sources/ingest-report.json` with the chunk count per doc, the average tokens per chunk, and the 10 shortest and 10 longest chunks. The human uses this file to sanity-check the chunker.

**Voyage client** (`TaxWise.Rag/Voyage/VoyageClient.cs`): there is no official .NET SDK, so write a typed `HttpClient` wrapper for `POST /embeddings` and `POST /rerank`. **Check the request and response shapes against https://docs.voyageai.com before you write it.** Don't guess field names.

**Tests:** chunker unit tests over a small synthetic page model (heading detection, no chapter crossing, overlap, table kept intact, deterministic IDs) and a Voyage client test with a fake `HttpMessageHandler`.

**Acceptance:**
- All 9 docs are ingested, and every chunk has a non-empty `Section` and `PageStart`, and a `TaxYear` that matches its manifest entry.
- A spot check: searching "standard deduction" with `taxYear == 2027` returns the Rev. Proc. 2026-35 §4.15 chunk.
- A manual spot check: search for "room and board" and confirm that a Pub 970 Chapter 1 chunk comes back with the correct page.
- Re-running without `--recreate` creates no duplicate points.

---

## 5. Phase 2 and Phase 3: cited Q&A chat and plain-language mode

### Phase 2: Query pipeline and API (target Nov 15)

#### 5.1 Flow (`TaxWise.Rag/Answering/`)

```
question
  → embed (input_type=query)
  → Qdrant search top 8 (filter taxYear == request.TaxYear)
  → Voyage rerank → top 4 with scores
  → if top rerank score < MinRerankScore → return Refusal (do NOT call Claude)
  → build prompt with 4 passages labeled [P1]..[P4]
  → Claude (structured JSON output)
  → validate (citations ⊆ {P1..P4}, quotes are substrings, ≥1 citation per claim)
  → map Pn → chunkId → AnswerDto
```

**Claude call** (`ClaudeAnswerer`):
- Use the official `Anthropic` NuGet package: `AnthropicClient`, `client.Messages.Create(new MessageCreateParams { ... })`. Model `claude-opus-5-5`.
- `OutputConfig = new OutputConfig { Effort = <from config>, Format = new JsonOutputFormat { Schema = ... } }`. On this model thinking is always on, so **don't** send `ThinkingConfigDisabled` or a budget. Both return a 400.
- Use **structured output, not the API's native citations feature.** We need our own passage-ID validation, and native citations can't be combined with `OutputConfig.Format`.
- **Refusal handling:** check `StopReason` before reading content. If it is `refusal`, return a polite "I can't help with that one" response with no citations. Also enable the server-side refusal fallback on the beta messages path per the SDK docs (`fallbacks` + the server-side-fallback beta header). If that doesn't compile cleanly with the installed SDK version, leave it out and note it in the PR.
- Catch exceptions specific-first: rate limit, then 5xx/connection (retry once), then other API errors (return 502 with a safe message).
- The SDK reads `ANTHROPIC_API_KEY` from the environment. Don't log request bodies that contain the key.
- If an SDK type name in this plan is wrong, **write the code, compile, and fix it from the compiler errors.** Don't spend turns reflecting over the assembly.

**Answer JSON schema** (sent as the output format):
```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["status", "paragraphs"],
  "properties": {
    "status": { "enum": ["answered", "insufficient_sources"] },
    "paragraphs": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": ["text", "citations"],
        "properties": {
          "text": { "type": "string" },
          "citations": {
            "type": "array",
            "items": {
              "type": "object",
              "additionalProperties": false,
              "required": ["passage", "quote"],
              "properties": {
                "passage": { "enum": ["P1", "P2", "P3", "P4"] },
                "quote":   { "type": "string", "description": "Exact sentence copied from that passage that supports the paragraph" }
              }
            }
          }
        }
      }
    },
    "suggestedFollowUps": { "type": "array", "items": { "type": "string" }, "maxItems": 2 }
  }
}
```

**System prompt** (`TaxWise.Rag/Prompts/answer.system.md`, loaded as an embedded resource and kept byte-stable for caching):
```
You are TaxWise, an educational assistant that helps U.S. college students understand
their federal taxes for tax year {TaxYear}.

You will receive up to four passages from official IRS documents, labeled P1-P4.
Answer the student's question using ONLY those passages. Do not use outside knowledge,
even if you are confident. If the passages do not contain enough information to answer,
return status "insufficient_sources" with an empty paragraphs array.

Each paragraph must be supported by at least one citation. For each citation, copy one
sentence verbatim from the passage into "quote" so the app can highlight it.

Do not calculate tax amounts. If the student asks "how much will I owe", explain the
relevant rule and suggest the Scenario calculator.

{WritingStyle}
```
`{WritingStyle}` (the default mode) reads: "Write clearly and concisely for a college student. Use the passage's terminology." Plain-language mode swaps in different text (§5.2).

The user message is the passages, each in a `<passage id="P1" source="Pub 970 · Ch. 1 · p. 7 · TY 2025">…</passage>` block, followed by `<question>…</question>`.

**Validation** (`TaxWise.Core/Guardrails/CitationValidator.cs`, pure and unit-tested):
- Every `passage` must be one of the passages actually sent. Remove any other citation.
- After whitespace and quote normalization, `quote` must be a substring of that passage's text. If it isn't, keep the citation but set `highlight = null`.
- If a paragraph has no valid citations left, **drop the paragraph**.
- If every paragraph is dropped, or `status == insufficient_sources`, return a Refusal.

**API contract** (`TaxWise.Shared`):
```csharp
public sealed record AskRequest(string Question, bool PlainLanguage, int TaxYear);
public sealed record AskResponse(
    string Kind,                       // "answer" | "refusal"
    IReadOnlyList<AnswerParagraph> Paragraphs,
    IReadOnlyList<SourcePassage> Sources,   // the 4 passages used, for the right-hand panel
    int PassagesSearched, int PassagesKept, // "Searched 8 passages, kept the 4 most relevant"
    IReadOnlyList<string> SuggestedFollowUps,
    string Disclaimer);
public sealed record AnswerParagraph(string Text, IReadOnlyList<CitationRef> Citations);
public sealed record CitationRef(int Number, string ChunkId, string? Highlight);
public sealed record SourcePassage(int Number, string ChunkId, string DocId, string DocTitle,
    string? Chapter, string Section, int Page, int TaxYear, string Text, string PdfUrl);
```
A refusal response carries the text: *"I don't know. I couldn't find this in the IRS publications I have for tax year {TaxYear}. Try IRS.gov, IRS Free File, or a free VITA clinic."* The response also has links.

Endpoints:
- `POST /api/ask` → `AskResponse`
- `GET /api/sources/{docId}/pdf` streams the stored PDF. The UI opens `…/pdf#page=N`.
- `GET /api/knowledge-base` lists the docs and tax year for the nav footer.

**Tests:** `CitationValidator` cases (bad ID, missing quote, all paragraphs dropped), retriever threshold refusal with fake Voyage, and an Api test for `/api/ask` with fake `IRetriever` and `IAnswerer`.

**Acceptance:**
- "Is my scholarship taxable?" returns an answer whose citations point to Pub 970 Ch. 1 chunks.
- "How do I stake crypto for yield?" returns a refusal **without calling Claude**. Verify this through a log line or a test.
- No response ever contains a citation number outside 1–4.

### Phase 3: Chat UI and plain-language mode (target Nov 17)

Build the UI to match the slide 7 mockup (`Pages/Ask.razor`):
- **Header:** the conversation title (first question, truncated) and a **Plain-language mode** toggle, remembered with MAUI `Preferences`.
- **Message list:** user bubbles on the right. Answers are cards made of paragraphs, and each paragraph has numbered citation chips (`1 · Pub 970 · Ch. 1 · p. 6 · TY 2025`). Above each answer a status line reads "● Searched 8 passages, kept the 4 most relevant".
- **Right panel "Source passage":** clicking a chip shows the doc title and subtitle, tags (Tax year · Chapter · Page), the section heading, the passage text with the `Highlight` sentence marked, and an **"Open page N in the PDF"** button. The button calls `Launcher.Default.OpenAsync(new Uri(pdfUrl + "#page=N"))`.
- **Follow-up chips** below the answer. Always include "Quiz me on <topic>" (it routes to quizzes with this chat's chunk IDs), plus Claude's `suggestedFollowUps`.
- **Disclaimer** in the footer of every answer: "Educational tool, not professional tax advice."
- Loading state, error state (API down), and the refusal style (neutral card with links).
- **Styling:** plain CSS in `wwwroot/css/app.css` with tokens taken from the mockups: dark teal sidebar `#123038`, accent `#0F7A75`, citation chip background `#FDF0D5`. No CSS framework is required.

#### 5.2 Plain-language mode
- Turning the toggle **on** changes only `{WritingStyle}`: "Write at about an 8th-grade reading level. Use short sentences. Explain any tax term the first time you use it. Keep every fact from the passages; do not add facts." The passages are the same; the facts are the same.
- **Term definitions on hover are deterministic, not generated by the LLM.** `data/glossary.json` holds about 40 terms (AGI, standard deduction, withholding, taxable income, dependent, filing status, refundable credit, qualified education expenses, FICA, self-employment tax, nonresident alien, …). Each term has a one-sentence definition and a `chunkId` source, and the human reviews them. The UI wraps the first match of each term in an answer with a tooltip `<span>`. Matching is case-insensitive and whole-word.

**Acceptance:**
- The demo script works end to end: ask "My scholarship paid my tuition and also gave me $1,200 for housing. Do I have to pay tax on it?", click citation 2, see the highlighted sentence, and open the PDF at the right page.
- With the toggle on, the same question gives a simpler answer **with the same citation chunk IDs**. Check this manually on 3 questions.

---

## 6. Phase 4: Explain my form (target Nov 19)

Build the UI to match the slide 9 mockup. Tabs: **Sample W-2 · Sample 1098-T · Upload mine**. "Upload mine" is shown but disabled with a "Coming soon" tooltip, because uploads are a stretch goal.

**Data** (`data/forms/w2-sample.json`, `1098t-sample.json`, written by the agent and **reviewed by a human**):
```jsonc
{
  "form": "W-2", "taxYear": 2025, "employer": "Sample Employer Inc.",
  "boxes": [
    { "id": "1", "label": "Wages, tips, other compensation", "value": 28000.00,
      "goesTo": "Form 1040, line 1a",
      "retrievalQuery": "Form W-2 box 1 wages tips other compensation",
      "sourceDocs": ["iw2", "i1040"] },
    { "id": "2", "label": "Federal income tax withheld", "value": 1500.00, "goesTo": "Form 1040, line 25a", ... }
    // W-2: 1,2,3,4,5,6,12 (with a code),15,16,17. 1098-T: 1,4,5,6,7,8,9
  ]
}
```
- `goesTo` is **static data that a human verifies** against the TY2025 Form 1040 instructions. The LLM explains the mapping but never decides it.
- Endpoint `POST /api/forms/explain` takes `{ form, boxId, plainLanguage }`. It retrieves with the box's `retrievalQuery`, filtered to `sourceDocs`, then reranks and applies the same threshold refusal. Claude gets a schema of `{ status, explanation, citations[] }` and the instruction: "Explain what this one box means in plain language and why it goes to {goesTo}. Use only the passages." The same `CitationValidator` runs. The response carries `goesTo` from the JSON, not from the model.
- `GET /api/forms/{form}` returns the sample with its boxes.
- **UI:** a grid of boxes on the left; clicking one shows the explanation card on the right (box number badge, title, explanation, "WHERE IT GOES" callout, citation chips). Below it a **Next up** card has a "Walk me through the next box" button.
- Cache explanations in memory on the API, keyed by `(form, boxId, plainLanguage)`.

**Acceptance:** every box on both sample forms returns either a cited explanation or an "I don't know" refusal. A human checks every `goesTo` value against the 2025 instructions; record this in the PR.

---

## 7. Phase 5 and Phase 6: calculator and quizzes

### Phase 5: Scenario calculator (target Nov 21)

#### 7.1 `TaxWise.Core/Calculator/TaxCalculator.cs`: pure, deterministic, `decimal` only

Inputs: `TaxYear` (2025 | 2026 | 2027), `Wages` (W-2 box 1), `FederalWithholding` (box 2), `FilingStatus` (Single, MarriedFilingJointly, MarriedFilingSeparately, HeadOfHousehold), `CanBeClaimedAsDependent` (bool).

Each year's constants live in their own file: `TaxYear2025.cs`, `TaxYear2026.cs` and `TaxYear2027.cs`. Each one implements `ITaxYearRules` (standard deductions, dependent floor and add-on, bracket schedule per status, `HasOfficialTaxTable`). They are registered in a `TaxYearRegistry`. **Adding a year means adding a file. No other code changes.** An unsupported year returns a 400.

Steps (each step is returned as a `CalcStep(label, amount, sourceRef)`):
1. **Standard deduction.** For a dependent: `min(regular, max(1350, earnedIncome + 450))`.
2. **Taxable income** = `max(0, wages − standardDeduction)`.
3. **Tax.** When taxable income is under $100,000, use the **Tax Table method**: find the $50 row (or $25 row below $3,000) and compute the tax on that row's midpoint with the brackets, then round to the nearest dollar, the same way the IRS builds the table. At $100,000 and above, use the bracket formula (Tax Computation Worksheet). Also return the per-bracket breakdown (e.g. "10% × $11,925").
4. **Refund or amount owed** = `withholding − tax`.

Put each constant in its year file with a source comment, e.g. `// Rev. Proc. 2026-35 §4.01 Table 3`. A human still hand-checks every value before the Phase 5 PR merges.

**TY2025.** The standard deductions are confirmed in Rev. Proc. 2025-32 §2.08 (OBBBA). Check the brackets against the 2025 Form 1040 instructions (Tax Computation Worksheet) / Rev. Proc. 2024-40.

| TY2025 | Single | MFJ | HoH | MFS |
|---|---|---|---|---|
| Standard deduction | 15,750 | 31,500 | 23,625 | 15,750 |
| 10% up to | 11,925 | 23,850 | 17,000 | 11,925 |
| 12% up to | 48,475 | 96,950 | 64,850 | 48,475 |
| 22% up to | 103,350 | 206,700 | 103,350 | 103,350 |
| 24% up to | 197,300 | 394,600 | 197,300 | 197,300 |
| 32% up to | 250,525 | 501,050 | 250,500 | 250,525 |
| 35% up to | 626,350 | 751,600 | 626,350 | 375,800 |
| 37% | above | above | above | above |

The dependent standard deduction is the greater of $1,350 or earned income + $450, capped at the regular amount.

**TY2026, from Rev. Proc. 2025-32 §4.01 and §4.14** (I pulled these from the IRS PDF on 2026-11-10):

| TY2026 | Single | MFJ | HoH | MFS |
|---|---|---|---|---|
| Standard deduction | 16,100 | 32,200 | 24,150 | 16,100 |
| 10% up to | 12,400 | 24,800 | 17,700 | 12,400 |
| 12% up to | 50,400 | 100,800 | 67,450 | 50,400 |
| 22% up to | 105,700 | 211,400 | 105,700 | 105,700 |
| 24% up to | 201,775 | 403,550 | 201,750 | 201,775 |
| 32% up to | 256,225 | 512,450 | 256,200 | 256,225 |
| 35% up to | 640,600 | 768,700 | 640,600 | 384,350 |
| 37% | above | above | above | above |

The dependent standard deduction is the greater of **$1,350** or earned income + **$450**. Aged or blind additional: $1,650, or $2,050 if unmarried; not used in v1.

**TY2027, from Rev. Proc. 2026-35 §4.01 and §4.15** (released Nov 5, 2026; I pulled these from the IRS PDF on 2026-11-10):

| TY2027 | Single | MFJ | HoH | MFS |
|---|---|---|---|---|
| Standard deduction | 16,600 | 33,200 | 24,925 | 16,600 |
| 10% up to | 12,800 | 25,600 | 18,250 | 12,800 |
| 12% up to | 52,025 | 104,050 | 69,650 | 52,025 |
| 22% up to | 109,125 | 218,250 | 109,100 | 109,125 |
| 24% up to | 208,325 | 416,650 | 208,300 | 208,325 |
| 32% up to | 264,550 | 529,100 | 264,550 | 264,550 |
| 35% up to | 661,375 | 793,650 | 661,350 | 396,825 |
| 37% | above | above | above | above |

The dependent standard deduction is the greater of **$1,400** or earned income + **$500**. Aged or blind additional: $1,700, or $2,100 if unmarried; not used in v1.

**Free golden tests from the Rev. Procs.** Each rate table states the tax at every breakpoint. For example, TY2027 Single at $52,025 is **$5,987.00**, and TY2026 MFJ at $100,800 is **$11,600**. Assert the bracket formula reproduces every breakpoint's base tax in all 4 statuses × 2026 and 2027. Those are about 50 assertions copied straight from IRS text.

**Tax Table for 2026/2027.** The official tables aren't published yet. Compute the tax with the same Tax Table method (the midpoint of each $50 row) and set `IsEstimate = true`. The UI then labels the result **"Estimate: official {year} Tax Table not yet published"**. When the 2026 Form 1040 instructions come out, add golden cases taken from the official table and set `HasOfficialTaxTable = true` for 2026.

**Not included (show this as a line under every result):** credits (education credits, EITC, child tax credit), OBBBA deductions for qualified tips and overtime (2025–2028), other income, and itemized deductions. Students with tip income should see a note that their real tax may be lower.

> ⚠️ **Mockup discrepancy, which the human must resolve.** Slide 11 shows tax of **$1,231.50** on $12,250 taxable income. That figure is the exact bracket formula. The Tax Table, which is what a real 1040 filer with taxable income under $100k uses, gives the tax for the $12,250–$12,300 row, computed at the $12,275 midpoint ≈ **$1,235**. Implement the Tax Table method and show "Tax (from IRS Tax Table)". Update the demo numbers to match. Do **not** change the code to match the mockup.

**Tests** (`TaxWise.Core.Tests`), written before the implementation:
- 10 or more TY2025 golden cases. A human copies the expected values directly from the TY2025 Tax Table, covering every filing status, a dependent with low wages, zero wages, and income ≥ $100k.
- The Rev. Proc. breakpoint tests for 2026 and 2027 described above.
- The same wages give a lower or equal tax in a later year (inflation indexing), and dependent cases use the right year's floor ($1,350 / $1,350 / $1,400).
- Property tests: tax is monotonic non-decreasing in wages, and taxable income is never negative.

#### 7.2 API and explanation
- `POST /api/calculator` takes `CalcRequest` and returns `{ steps[], refundOrOwed, isRefund, explanation, citations[], taxYear }`.
- Claude receives the finished step table as **fixed data**, plus the retrieved Tax Table and standard deduction passages. Instruction: "Explain in 2–4 sentences why the result is what it is. Do not compute anything. You may only mention dollar amounts that appear in the provided numbers."
- **Dollar guardrail** (`TaxWise.Core/Guardrails/DollarAmountValidator.cs`): extract every `$X` and every bare number with comma thousands from the explanation. Each one must equal (to the cent) a value in the step table or a bracket threshold from the breakdown. On any mismatch, retry once with a stricter instruction. If the retry also fails, **show only the table and a canned explanation** and log the incident.
- The explanation retrieves passages for the **selected year**: the Tax Table / standard deduction passages for 2025, and the Rev. Proc. §4.01 and §4.14/§4.15 chunks for 2026 and 2027.
- **UI** (slide 11): the input form on the left, with a **Tax year** dropdown that defaults to 2026. On the right: the big result ("$X estimated refund" / "$X estimated amount owed"), the step table, and a "WHY THIS NUMBER" card with citations. A header badge reads "Tax year {year} · {status}", plus an "Estimate" badge when `IsEstimate`.
- **Compare years** toggle (optional; build it only if Phase 5 is on schedule): run the same inputs for 2025, 2026 and 2027 and show them side by side.

**Acceptance:** all golden tests pass. The dollar guardrail has unit tests: it accepts a correct explanation, and it rejects an explanation containing a made-up `$1,234.00`.

### Phase 6: Learning quizzes (target Nov 23)

- The App keeps a per-session list of the **chunk IDs cited** in chat answers, grouped by topic (the conversation title). It persists them as JSON in `FileSystem.AppDataDirectory`.
- `POST /api/quiz` takes `{ topic, chunkIds[] (≤10), count = 5, plainLanguage }`. If the list has fewer than 5 chunk IDs, fill up to 5 from the reranked neighbours of the topic text.
- **One Claude call per passage.** Calls run in parallel, at most 5 at a time. Structured output:
  `{ question, options[4], correctIndex (0–3), explanation, quote }`. Instruction: write the question **only** from this passage, with exactly one correct option, and plausible distractors that the passage itself rules out.
- **Validation:** `options` has 4 distinct entries, `correctIndex` is in range, and `quote` is a substring of the passage. Throw away failing items and regenerate once. Return the items with `chunkId` attached.
- **UI** (slide 13): the header shows "Quiz · {topic}" and a progress bar "Question 2 of 5". Above the question: "GENERATED FROM YOUR QUESTIONS ABOUT {TOPIC}". The four options are A–D. After the user answers, a feedback card shows Correct or Incorrect, the explanation and a citation chip that opens the source panel. Then a "Next question" button, and an end screen with the score.
- With no chat history, the quizzes page shows: "Ask a few questions first. Quizzes are built from what you've asked about."

**Acceptance:** a quiz generated after the scholarship demo has 5 valid questions, each citing a Pub 970 chunk. The validator tests pass.

---

## 8. Phase 7 and Phase 8: evaluation, deploy and polish

### Phase 7: Evaluation (target Nov 25)

**Gold set** `eval/gold.jsonl` has about 50 lines:
```json
{"id":"q01","question":"Is my scholarship taxable if it only paid tuition?","inScope":true,
 "expectedDocs":["p970"],"expectedPages":[6,7],"goldAnswer":"Generally no, if ... degree candidate ...","persona":"scholarship"}
```
- Mix: about 40 in-scope questions spread over the 5 personas from the design doc (W-2 first-timer, scholarship, dependent, gig/self-employment, international), and about 10 out-of-scope questions (crypto staking, state of NY rules, business depreciation, …).
- Include about 6 **year-specific** questions, each with a `taxYear` field. Examples: "What is the 2027 standard deduction for a single filer?" should give $16,600, citing rp-26-35. "What's the 2026 dependent standard deduction minimum?" should give $1,350. Also include 2 **year-trap** questions that must not mix years, such as asking about 2027 while the selected year is 2025.
- The agent **drafts** the questions and the expected doc and page. **A human writes or approves every `goldAnswer`** from the publications. Don't merge the gold set without that sign-off.

**Runner** (`dotnet run --project eval/TaxWise.Eval -- --mode taxwise|baseline`) computes these metrics:

| Metric | Definition |
|---|---|
| Retrieval hit@4 | At least one of the top-4 chunks is from an `expectedDoc` and within ±1 of an `expectedPage` |
| Citation validity | Share of citations that passed the validator *before* any were dropped |
| Answer correctness | An LLM judge (`claude-sonnet-5-5`, structured output `{verdict: correct/partial/incorrect, reason}`) compares the answer to `goldAnswer`. A human spot-checks 10 verdicts |
| Refusal precision / recall | On the out-of-scope set: the share that is refused. On the in-scope set: the share that is wrongly refused |
| Baseline | Same model, same question, **no retrieval**, judged the same way |

- Sweep `MinRerankScore` over {0.2, 0.3, 0.35, 0.4, 0.5}. Pick the value that maximizes out-of-scope refusal while keeping the in-scope false-refusal rate at or below 10%. Write the chosen value into `appsettings.json`.
- Write `eval/results/YYYY-MM-DD-{mode}.md`, a table plus per-question rows, and `.json`. These feed the final report.
- The **evaluation agent is a separate session** from the build agent (slide 5). It runs the tests and evals and reports failures back. It does not fix them.

### Phase 8: Deploy and polish (target Nov 29)

- **Qdrant Cloud** free cluster: run ingestion against it with `--qdrant-host <cloud> --qdrant-api-key`.
- **Azure App Service** (Linux, .NET 10): publish `TaxWise.Api` from Rider or with `az webapp up`. Set the secrets as app settings. Serve the PDFs by uploading `data/sources/pdf` with the deploy, or stream them from irs.gov with a redirect, which is simpler: `/api/sources/{docId}/pdf` → 302 to the manifest URL.
- **App:** a Release build for Mac Catalyst (`dotnet publish -f net10.0-maccatalyst -c Release`), with `ApiBaseUrl` pointing at Azure. Optionally a Windows build.
- **Polish:**
  - keyboard submit in the chat box
  - focus states
  - accessible labels on the toggle and the citation chips
  - an empty state for each page
  - a "Knowledge base" footer driven by `/api/knowledge-base`
  - an "About / disclaimer" dialog linking IRS Free File and VITA
- **README:** architecture diagram (mermaid from the design doc), setup steps, the key commands, the eval summary table, and the known limitations.
- **Demo rehearsal checklist** in `docs/demo-script.md`: the scholarship question, the citation, the PDF page, plain-language mode, W-2 box 1, the calculator, a quiz, and the crypto refusal, then a comparison with a plain chatbot.

**Acceptance:** the deployed API answers the demo questions from a fresh machine. The full demo runs in under 4 minutes.

---

## 9. Prompt log format (`docs/prompt-log.md`)

Append one entry per working session:

```markdown
### 2026-11-12 · Phase 1 · Claude Code
- **Prompt:** "Implement the section-aware chunker per BUILD_PLAN §4.2 …"
- **Generated:** SectionChunker.cs, ChunkerTests.cs (9 tests)
- **Worked?** partial: heading detection missed bold run-in headings in Pub 17
- **Human change:** added bold+short heuristic; re-ran ingest-report
- **Tag:** partial
- **Head-to-head:** (optional) same prompt to Copilot → needed 3 fixes vs 1
```

Run **head-to-head comparisons** (the same prompt given to Claude Code and to Copilot) for at least 3 of these: the PDF chunker, the Voyage client, the tax calculator, and the citation chip UI. Log which result needed fewer fixes.

---

## 10. Cross-cutting rules

- **The LLM never does math, and never decides a form-line mapping or a calculator constant.** Code and human-verified data do.
- **Every source shown to the user carries its tax year.**
- **Disclaimer** on every answer, explanation and calculator result.
- Prompts live in `TaxWise.Rag/Prompts/*.md` as embedded resources. Keep the system prompt byte-stable, with no timestamps, so prompt caching works. Put a `cache_control` breakpoint after the system prompt if the SDK exposes it.
- Log every Claude call with: endpoint, latency, input/output tokens, stop reason, validator outcome. **Never log API keys.**
- Use `IHttpClientFactory` with Polly retry (3 tries, jittered backoff) for Voyage.
- Use `CancellationToken` end to end. The App cancels a request when the user leaves the page.

---

## 11. Out of scope (stretch, only if a human asks)

- Parsing uploaded W-2 and 1098-T PDFs (PdfPig text extraction plus Claude structured extraction; scans need OCR).
- Colorado state tax coverage (a second collection, or `jurisdiction` payload filtering).
- A side-by-side view comparing TaxWise with a plain chatbot (Phase 7's baseline mode already gives the data).
- Voice input.

---

## 12. Items needing a human (the build agent must stop and ask)

1. Approve each phase plan before the build starts.
2. Confirm that every source PDF's edition matches its manifest `taxYear` (Phase 1).
3. Verify every `goesTo` line mapping (Phase 4).
4. Verify every TY2025, TY2026 and TY2027 calculator constant and the golden Tax Table cases. Decide how to handle the mockup's $1,231.50 discrepancy (Phase 5).
4a. **When the IRS posts the 2026 publications** (expected Dec 2026 – Jan 2027): add them to the manifest, re-point the 2025 entries to `irs-prior/`, re-run ingestion with `--year 2026`, add 2026 to `FullKnowledgeBase`, and switch `DefaultChat` to 2026.
5. Write or approve the gold answers (Phase 7).
6. Provision the Azure, Qdrant Cloud, Voyage and Anthropic accounts and keys. The agent never creates paid resources by itself.
