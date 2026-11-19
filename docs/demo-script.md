# Demo rehearsal checklist (target: under 4 minutes)

Run it against the **deployed** API with the **Release** App (docs/DEPLOY.md). Rehearse with a timer at least twice. The times are the running total at the end of each step.

## Before you start (not timed)

- [ ] `curl https://<app-name>.azurewebsites.net/health` returns `ok`. On the F1 tier, call it a minute early so the cold start is over.
- [ ] Open the App. The **green dot** next to the logo is on, and the sidebar footer lists the knowledge base (documents, TY 2025 full, TY 2026 and 2027 Rev. Proc. only).
- [ ] Chat is on **Tax year 2025**, plain-language mode is **off**, **Compare with a plain chatbot** is **off**, and a **New chat** is open.
- [ ] Quizzes has at least one topic. If it doesn't, ask the scholarship question once beforehand.
- [ ] The deployed API has `POST /api/ask/baseline` (it was added after the first deploy; see docs/DEPLOY.md §5). Check with `curl -s -o /dev/null -w '%{http_code}\n' https://<app-name>.azurewebsites.net/api/ask/baseline -H 'content-type: application/json' -d '{"question":"","plainLanguage":false,"taxYear":2025}'`: `400` means it's there, `404` means redeploy.
- [ ] Each compared question uses two of the 60 Claude-backed requests per IP per hour (DECISIONS #20, #21). On a shared campus network, raise `UsageLimit__PermitLimit` first.
- [ ] Turn off notifications, and make the window large enough that the source panel shows.

## The run

1. **Problem (0:15).** "Students get tax answers from chatbots that sound sure and cite nothing. TaxWise answers only from IRS publications and shows you the page."
2. **Scholarship question (0:45).** Ask: *"My scholarship paid my tuition and also gave me $1,200 for housing. Do I have to pay tax on it?"* (the first suggestion on the welcome screen).
   - [ ] Point out the paragraph-level **citation chips** (`Pub 970 · Ch. 1 · p. 5 · TY 2025`).
3. **The citation (1:00).** Click a chip. The right panel shows the exact IRS passage with the **quoted sentence highlighted**.
4. **The PDF page (1:15).** Click **Open page N in the PDF**. It opens the IRS PDF at that page. Close the browser.
5. **Plain-language mode (1:35).** Turn on the toggle and ask the question again (or click a suggested follow-up). The same sources come back in simpler words. Hover a dotted term to show a human-reviewed glossary definition.
6. **W-2 box 1 (2:05).** **Explain my form** → W-2 → click **Box 1**. Show where it goes (`Form 1040, line 1a`). That mapping comes from human-checked data, not the model, and the explanation is cited.
7. **Calculator (2:35).** **Scenario calculator**, tax year **2026**, wages $28,000, withholding $1,500, single, not a dependent → **Calculate**. Show the step table: "every number is computed by code from IRS tables; Claude only explains them." Note the **Estimate** badge, since the 2026 Tax Table isn't published. Optionally turn on **Compare years**.
8. **Quiz (3:00).** **Learning quizzes** → the scholarship topic. Answer one question and show the feedback card's citation.
9. **Crypto refusal, side by side (3:25).** Back in chat, turn on **Compare with a plain chatbot** and ask: *"How do I stake crypto for yield?"* (gold question o01; use this exact wording, since it's the one the eval checked). The two answers appear next to each other:
   - [ ] Left, **TaxWise** **declines** because its IRS sources don't cover it. Its top rerank score (0.35) is under the 0.50 threshold, so Claude is never called.
   - [ ] Right, the **plain chatbot** (same model, no sources, labelled *"may be wrong or out of date"*) gives a staking how-to with no IRS citation. In the baseline run it refused only 1 of 12 out-of-scope questions.
10. **Close (3:50).** With Compare still on, optionally switch to **Tax year 2027** and ask *"What is the 2027 standard deduction for a single filer?"*: TaxWise answers $16,600 cited to Rev. Proc. 2026-35, while the plain chatbot says the 2027 amount hasn't been published. Skip it if you're past 3:45. Close with the eval numbers:
    - **100%** of out-of-scope and year-trap questions refused, against **8%** for the plain chatbot.
    - **100%** of citations valid.
    - **95%** retrieval hit@4.
    - On TY2027 questions the plain chatbot said the amounts weren't published yet. With 2027 selected, TaxWise answered all 3 correctly from Rev. Proc. 2026-35.

## If something goes wrong

- **Red dot / "TaxWise API unavailable":** check `/health`. If Azure is down, switch to the local API (`docker compose up -d` + `dotnet run --project src/TaxWise.Api`) with a Debug build of the App.
- **A slow answer** (more than 15 s): keep talking through the citation idea. The median answer time in the eval was about 8 s.
- **The plain chatbot column shows an error** (TaxWise still answers): press **Try again** in that column. If it says the endpoint isn't found, the deployed API predates the comparison; quote the baseline report ([eval/results/2026-11-12-baseline.md](../eval/results/2026-11-12-baseline.md)) instead.
- **PDF opens on page 1:** some PDF viewers ignore `#page=`. Say the page number out loud and scroll.
