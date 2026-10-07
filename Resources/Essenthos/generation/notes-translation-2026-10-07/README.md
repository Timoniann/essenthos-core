# Notes of our own records in Ukrainian, German and Spanish (PRB-0807), 2026-10-07

- `extract.py` collects every English `notes` of our own records (185) with its SHA-256.
- `run.py` translated each with gpt-6.1-sol (OpenAI, through Codex, reasoning medium) under a checker: references kept, length plausible, no English left, books named in words translated, «проект».
- `review-brief.md` / `verdicts.json`: a second reading of all 185 by Claude Sonnet (Anthropic) — 25 points raised.
- `apply_fixes.py`: the points confirmed against the corpus's own Ohienko, Luther 1912 and Reina-Valera 1909 text, applied; the ones the corpus refuted (Гамор, Шадрах, «за пам'ятника», Gerson/Gersón, El Dios de Israel, Zelophehads, Уззійя) were not.
- The result is `Essenthos.Forge/Loading/Encyclopedia/NoteTranslations.json`.
