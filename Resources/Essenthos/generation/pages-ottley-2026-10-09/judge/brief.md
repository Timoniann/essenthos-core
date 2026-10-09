# Judge: Ottley's Isaiah, printed page against a proposed correction

You check proposed corrections to a digital transcription of R. R. Ottley, *The Book of Isaiah according to the Septuagint* (Codex Alexandrinus), vol. II, 1904, against scans of the printed pages.

Your worklist is `half-N.json` (N is given in your prompt), in this folder. Each item has:
- `verse`: chapter:verse. The verse numbers are small superscripts in the running text; chapters are roman numerals. The numbers in the outer margin are line numbers.
- `ours`: the transcription's word or words.
- `printed`: the word or words someone says the page prints in their place. An empty `printed` means the words should be removed.
- `images`: the page scan, and the next leaf in case the verse runs over.

## What to do

For each item:
1. Open the image with the Read tool and find the verse in the **main text**, not the apparatus at the foot of the page.
2. Decide:
   - `right`: the page prints exactly `printed` there, every letter, accent and breathing (punctuation aside), and `ours` is wrong.
   - `wrong`: the page prints `ours`, or something else. Put what it prints in `actual`.
   - `unsure`: the scan cannot settle it, for example a smudged accent.

Items are grouped by page. Read each image once and judge all of that page's items from it; zoom by reading the image again only if needed.

You are not told how anyone else judged these. Judge from the page alone.

## Output

Write `verdict-N.json` in this folder with the Write tool: a JSON array of `{"id": <id>, "verdict": "right"|"wrong"|"unsure", "actual": "<what the page prints, when wrong>", "note": "<short>"}`, one per item, in order.

- Write it in parts as you go (rewrite the whole file each time) so nothing is lost if you stop.
- Do not edit any other file.
- Do not run anything against a database.
- When done, reply with the counts of right, wrong and unsure.
