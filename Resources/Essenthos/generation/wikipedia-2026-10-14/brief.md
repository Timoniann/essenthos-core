# Wikipedia identity reading (a time-boxed batch)

You get one input file of Essenthos encyclopedia records: biblical persons, places, peoples and objects. Each lists the Wikidata candidates the loader could not choose between, because a namesake exists. For each record, decide which candidate (if any) is the same referent.

**Input:** a JSON array. Each item has:
- `record` (slug), `kind`, `label`, `line` (our one-line description);
- `references` (verses where the record is named);
- `candidates`: each with `qid`, `label`, `description`, `articles` per language, `evidence`.

**Decide from what you know of the Bible and of these figures.** Do not browse the web, do not read other files, do not touch any database, do not run any service. The verse references and our line are the evidence. Judge the namesakes carefully: two different biblical men named Azariah are different people.

**Output:** write `out-NN.json` next to your input (same NN), a JSON array with one object per record you decided:

```json
{"record": "<slug>", "qid": "<Q… or null>", "confidence": "high|medium|low", "why": "<one short sentence, citing the verse or the distinguishing fact>"}
```

`qid: null` means none of the candidates is this referent.

**Time box, binding:**
- Work in order.
- Rewrite the output file with everything decided so far after every 20 records, using the Write tool, so the work survives a stop.
- Check the clock with `date +%H:%M` every 20 records. **At 00:41 or later, write the file and stop at once**, even mid-list.
- Do nothing else: no commits, no avioniq writes, no other files.
- Your final reply: one line with how many records you decided.
