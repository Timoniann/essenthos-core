# The names asked again, on candidate lists that had been repaired

The phase-2 run answered 9,993 occurrences of 515 names against a candidate list that was wrong in
two directions at once: it offered people who appear only in the Greek New Testament as referents
for words of the Masoretic text, and it could not offer any of the 1,233 places the geocoding
dataset supplies, because none of them carried a Strong number. Both are fixed in
`EntityCandidates`, and this is what the fix changed in the answers.

**94 of the 515 names had a list that changed** — 53 lost a candidate, 42 gained one, 1 did both —
and those names carry 3,629 of the 9,993 occurrences. **19 of the 94 stopped being questions
altogether**: with the impossible candidates gone they answer with exactly one entity, so 476 of
those occurrences can be annotated without a reading at all. The remaining 75 names, 3,153
occurrences, were extracted afresh and asked again, prompt `sense-1` unchanged, sonnet, six shards
in parallel. 3,151 came back; two occurrences of one batch went unanswered.

## What moved

| | occurrences |
|---|---|
| re-asked | 3,148 |
| the answer changed | 480 |
| `unlisted` → a record | 345 |
| a record → a different record | 67 |
| a record → `unlisted` | 45 |
| into or out of `unclear` | 23 |

**304 of the 345 recoveries are places** — the population the place join exists for. The largest are
H758 Aram and Syria (121), H2275 Hebron (61), H3157 Jezreel (33), H8659 Tarshish (20), H6068
Anathoth (13). Measured against the encyclopedia's own verse lists, which had nothing to do with the
join, **271 of the 345 agree, 5 disagree and 69 the list does not decide.**

**The 67 that changed from one record to another are almost all corrections.** The verse lists match
the new answer 53 times and the old answer 4 times. Most of them are H3568 Cush, H1568 Gilead and
H4124 Moab, where the land was not on offer and a person had to be chosen.

**The 45 that went to `unlisted` are mostly the tribe of Judah** — 33 of them — which the audit had
already said was the right reading and which the encyclopedia holds no record for.

## The agreement figure

Scored against the encyclopedia's verse lists as they now resolve, over the 9,993 occurrences that
have an answer:

| | decided | agree | disagree | unclear | unlisted | agreement |
|---|---|---|---|---|---|---|
| the run as it stood | 8,744 | 8,073 | 142 | 16 | 513 | 92.33% |
| with the 94 names asked again | 8,744 | 8,362 | 99 | 26 | 257 | 95.63% |

Two things about this table. The denominator is larger than the 8,281 the phase-2 report used,
because the verse lists now decide occurrences they could not reach before — a place-sense verse
whose only candidates were people decided nothing. And 92.33% is not a retraction of the published
99.00%: that figure was measured over the occurrences the old lists could decide, and this one is
measured over a wider set that includes the ones the old lists were silent about by construction.

## The audit's fifty-three, one by one

53 occurrences were judged wrong on the ground that the right answer was not among the candidates.
36 of them are on names whose list changed and were asked again; **28 of those 36 now answer what
the audit said.**

- **2 are answered because the corpus can now offer the referent.** 1 Chronicles 4:17 Eshtemoa is
  the Judahite town, `eshtemoa-3`, not the man; Ezekiel 27:13 Meshech is the trading nation,
  `meshech-2`.
- **26 are the tribe of Judah**, and the model now says `unlisted` and names the tribe, which is
  what the audit said and what the encyclopedia still cannot hold. The reading is no longer wrong;
  the coverage gap is.
- **4 had the right answer offered and did not take it.** 1 Chronicles 2:42 Ziph still answers with
  the man although `ziph-3` (Tell Zif) is now on the list, and the three Senaah occurrences still
  answer with the man although `senaah-2` is. This is the honest half of the result: making a
  referent available is necessary and is not sufficient.
- **4 were re-asked and unchanged for other reasons** — two Zechariahs, Eliam and Johanan, where the
  audit's referent is a person nobody holds.
- **17 were not re-asked**, because their lists did not change: 8 occurrences of the tribe of
  Benjamin, Canaan standing for Babylonia in Ezekiel 17:4, and 8 people the encyclopedia does not
  hold at all.

`again.json` beside this file lists all 480 changed answers with the reason the model gave.
