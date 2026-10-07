You are the second reader of translations of short encyclopedia notes for a Bible research site (Essenthos, PRB-0807). Another model (OpenAI's gpt-6.1-sol through Codex) translated each English note into Ukrainian, German and Spanish. Check them independently. You change nothing in any repository or database and run no services; you only read one JSON file and write one JSON file.

Input: `C:\Users\timon\AppData\Local\Temp\claude\C--Users-timon-Projects-Essenthos\6ce57ccb-b364-495a-8563-3a5ef1bef131\scratchpad\notes\review-set.json` — a JSON array of 185 items `{id, english, ukr, deu, spa}`. Read it in slices with the Read tool (offset/limit, about 150 lines at a time), every item.

What the translations were asked to do:
- Same facts, same order, same hedges ("may", "is not certain"), nothing added or left out.
- Verse references in code form ("EXO 25:8-9", "1KI 6:1") kept exactly as written. A book named in words ("1 Samuel 14:50") is translated as that language's classic Bible titles it, numbers kept.
- Names of people, places, peoples as the classic Bible of that language writes them: Ukrainian as Ohienko (Мойсей, Аарон, Авімелех, Ґерар), German as Luther 1912, Spanish as Reina-Valera 1909.
- "the LORD" = «Господь» / "der HERR" / "Jehová"; "God" = Бог / Gott / Dios.
- Ukrainian writes «проект», never «проєкт». No English words left.

For each item and each language decide:
- `ok` — faithful and natural.
- `fix` — a real error: a fact changed, added or dropped; a hedge lost or strengthened; a wrong or garbled name (a different person, or clearly not that Bible's form); a reference changed; English left; ungrammatical. Give the exact wrong phrase and the correction.
- Style preferences are not errors. Do not mark a different-but-correct word choice.

Output: write with the Write tool to `C:\Users\timon\AppData\Local\Temp\claude\C--Users-timon-Projects-Essenthos\6ce57ccb-b364-495a-8563-3a5ef1bef131\scratchpad\notes\verdicts.json` a JSON array containing ONLY the items with at least one `fix`: `{"id": …, "lang": "ukr|deu|spa", "wrong": "…", "correct": "…", "why": "short"}` (one object per error). Then your final reply: the number of items read, the number of errors per language, and the path.
