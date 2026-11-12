# Decisions

Design decisions for TaxWise, newest last. Each one records what we chose, what else we considered, and why. How AI was used in reaching them is in [docs/AI_USAGE.md](docs/AI_USAGE.md).

---

## 1. Build Phase 1 before Phase 2

**Phase 1 · 2026-11-10 · session `cc8e27f2`**

**Decision:** We asked for Phase 2. The agent found that Phase 1 (ingestion) didn't exist yet, and we built Phase 1 first.

**Alternatives considered:** Build Phase 2 (the query API) against fake retrieval and ingest later.

**Why:** Phase 2's acceptance checks need real IRS chunks in Qdrant. Building it against fakes would have meant finishing it without being able to show it works.

## 2. Use the 2025 (`irs-prior`) editions of the W-2 and 1098-T instructions

**Phase 1 · 2026-11-10 · session `cc8e27f2`, [PR #3](https://github.com/cnash03/Taxwise/pull/3)**

**Decision:** [`data/sources/manifest.json`](data/sources/manifest.json) points at `irs-prior/iw2w3--2025.pdf` and `irs-prior/i1098et--2025.pdf`, not the `irs-pdf/` URLs in BUILD_PLAN. Ingestion checks each PDF's cover year and stops if it doesn't match.

**Alternatives considered:** The plan's `irs-pdf/` URLs.

**Why:** On 2026-11-10 those URLs already served the 2026 editions. The app answers for TY2025, so it has to cite the 2025 instructions. The cover-year check stops the same switch from happening silently to other sources.

## 3. Ingest Rev. Proc. 2026-35 §3, not §4

**Phase 1 · 2026-11-10 · session `cc8e27f2`, [PR #3](https://github.com/cnash03/Taxwise/pull/3)**

**Decision:** For TY2027 we ingest and cite §3 of Rev. Proc. 2026-35. The standard deduction is §3.15 and the rate tables are §3.01.

**Alternatives considered:** §4, as BUILD_PLAN said.

**Why:** BUILD_PLAN was wrong. The 2027 amounts are in §3 of the actual document. Rev. Proc. 2025-32 does match the plan (§4 = TY2026). **Phase 5's TY2027 source comments must cite §3.xx.**

## 4. The bare "room and board" spot check is informational only

**Phase 1 · 2026-11-10 · session `cc8e27f2`, [PR #3](https://github.com/cnash03/Taxwise/pull/3)**

**Decision:** The plan's spot check (search "room and board" and expect Pub 970 Ch. 1) is shown as an informational row. Two scholarship questions, including the demo question, are now the pass/fail checks. Both find Pub 970 Ch. 1, p. 5.

**Alternatives considered:** Keep the plan's check as pass/fail and tune retrieval until it passes.

**Why:** The bare phrase is ambiguous. Pub 970 Ch. 7 (529 plans) and the dependency support rules are also valid matches. The full questions test what a student would actually ask.

**Caveat:** The agent made this change to its own acceptance check without asking us first, and flagged it afterwards. That is why we tagged Phase 1 partial. We accepted the change when we merged PR #3.

## 5. One user-secrets store (`taxwise-dev`) for the API and ingestion

**Phase 1 setup · 2026-11-10 · session `0640c1d8`**

**Decision:** `TaxWise.Api` and `TaxWise.Ingestion` share the `UserSecretsId` `taxwise-dev`, so each key (Voyage, Anthropic) is set once. Keys are typed into our own terminal, never into chat.

**Alternatives considered:** A separate secrets store per project.

**Why:** Both projects call Voyage with the same key. With two stores, the keys would have to be set twice and could drift apart. Typing them in our own terminal keeps them out of the AI logs.

## 6. Talk to Qdrant through `Qdrant.Client` directly

**Phase 1 · 2026-11-10 · session `cc8e27f2`, [PR #3](https://github.com/cnash03/Taxwise/pull/3)**

**Decision:** `QdrantChunkStore` uses the `Qdrant.Client` library, not Semantic Kernel's Qdrant connector (`Microsoft.Extensions.VectorData`) that BUILD_PLAN names.

**Alternatives considered:** The Semantic Kernel connector.

**Why:** The agent's plan said it would use the client directly "unless the Semantic Kernel connector is clearly simpler", and it listed the change in PR #3. The store needs payload indexes on `docId` and `taxYear` and filtered search, and the client exposes these directly. The logs don't record a closer comparison, and we accepted the change when we merged PR #3.

## 7. `goesTo` comes from human-checked data, never from the model

**Phase 4 · 2026-11-10 · session `ce6c7853`**

**Decision:** Where each form box goes on the return (its `goesTo`, e.g. W-2 box 1 → Form 1040, line 1a) is stored as static data in [`data/forms/`](data/forms). A person checks every value against the TY2025 IRS instructions. The API returns `goesTo` from that file. The model only explains the mapping. Its prompt tells it not to change the mapping or name a different line.

**Alternatives considered:** Let the model work out the destination from the retrieved passages.

**Why:**
- A wrong line number is a filing error, and a student has no way to spot it.
- Retrieval can miss the passage that names the line, and the model would then guess.
- A fixed value can be checked once and tested (`FormDataTests`). A model's answer can change from one call to the next.

**Follow-up:** The human check is a checklist in [PR #6](https://github.com/cnash03/Taxwise/pull/6) and must be finished before it merges. When the 2026 instructions come out, the values have to be checked again.

## 8. The sample W-2 uses box 12 code DD

**Phase 4 · 2026-11-10 · session `ce6c7853`**

**Decision:** The sample W-2's box 12 entry is code **DD**, the cost of employer-sponsored health coverage.

**Alternatives considered:** Code D (401(k) deferrals) and code E (403(b) deferrals). We took the AI's recommendation.

**Why** (the session log records the choice but not the reasons, so these were written down afterwards):
- Students with part-time or campus jobs are more likely to see DD than retirement deferrals.
- It's informational only and not entered on Form 1040. That shows the most important lesson about box 12: not every number on a W-2 goes on your return.
- D and E bring in retirement-plan rules that are outside the app's student scope.
