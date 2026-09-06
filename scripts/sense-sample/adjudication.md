# The 83 disagreements, adjudicated

Every occurrence where the reading model and BibleData's verse lists named different records,
judged on the Hebrew, the verse in its context and the parallel lists, and then put to a stronger
model as a third witness. Neither of the first two answers was treated as an authority and the
third is not one either: where the evidence does not decide, the case is left undecided and said
to be so.

## The count

| | cases |
|---|---|
| source-wrong | 57 |
| model-wrong | 7 |
| duplicate-records | 13 |
| contested | 2 |
| both wrong | 4 |
| **total** | **83** |

**7 of the 83 are the model being wrong.** Fifty-seven are the source assigning a verse
to a record its own evidence contradicts, thirteen are one man held under two records, and six are
questions the text does not settle — two where the answer is genuinely open and four where both
sides are wrong and the right referent is a third record or none at all.

The 83 rows are 82 occurrences. Ezra 10:31 was answered twice, in two shards, with the same answer
both times, and both rows are in the file. That is not peculiar to this word: 154 of the run's
10,147 answer rows are second answers to an occurrence another shard had already covered, and three
of those pairs disagree with themselves. Counting distinct occurrences instead of rows, the run is
8,093 agreements and 82 disagreements where the verse lists decide — 99.00%, the same
figure, because the repeats agree at the same rate as everything else.

## What the accuracy actually is

Agreement with BibleData was never accuracy, and the direction of the error turns out to be almost
entirely one way. Of the 82 occurrences where the two sources differ:

| | occurrences |
|---|---|
| the reading is right about the man | 69 |
| the reading is wrong | 7 |
| nothing settles it, or both are wrong | 6 |

Over the 8,175 occurrences the verse lists decide and the model answered, the reading is
demonstrably wrong 7 times. That is **99.91% accuracy**,
against the 99.00% agreement the run reported. Counting the six undecided
against it as well gives 99.84%.

**Both figures are floors, not measurements, and the reason matters more than the number.** They
rest on the 8,093 occurrences where the two sources agree, and not one of those was audited.
Two sources agreeing is not two sources being right, and this adjudication has just established
that BibleData is wrong in most of the places the two differ — on Zadok, on Shishak, on Gemariah,
on Samuel, on Darius it is wrong systematically, name by name rather than verse by verse. Wherever
the model agreed with a systematically wrong record, the agreement is a shared error and nothing
here would find it. The honest statement is: **of the disagreements, 7 are the model; how often the
model is wrong where the two agree is not known and this exercise cannot say.**

## How this was judged

The evidence for every case was rebuilt from the live `essenthos_core` database with
`scripts/sense.py extract`: Strong's entry, every candidate record with its distinguisher, its
alternative name labels, every verse BibleData attests it in and every kinship edge recorded for
it, then the occurrence itself — the Hebrew verse with the word marked, the King James verse and
its neighbours, and the King James words the alignment puts opposite that Hebrew word. Nothing was
written to the database.

Two properties of the source decide a great many of these, and neither was known when the run was
scored.

**BibleData's distinguisher and its verse list contradict each other.** A record whose prose says
*explained the law to the people (NEH 8:7)* whose verse list does not contain Nehemiah 8:7, and
Nehemiah 8:7 sitting instead on a record whose prose says nothing of the kind. The prompt shows the
model the distinguisher and never the verse list, so wherever the two halves disagree the model was
answering a different question from the one it was scored against. Four of the seven wrong readings
are exactly this: Azariah at Nehemiah 8:7, Hashabiah at Nehemiah 10:11 and Akkub at Nehemiah 8:7,
where the prose of a record cites the disputed verse and its own verse list does not, and Shebaniah
at Nehemiah 10:12, where the prose is ambiguous enough to be read as claiming two verses. In each
the model reasoned correctly from what it was shown. That is a fault in the harness as much as in
the reading, and it is fixable: show the model both halves, or neither.

**BibleData's verse list is verse-level.** When one verse names the same Strong number twice —
Judges 18:29 with the city and the patriarch, Ezra 2:61 with the priest and his father-in-law,
1 Chronicles 1:41 with two generations of Dishon, 2 Chronicles 35:15 with the guild and its
founder — the list has one answer for two words and cannot be right about both. 1 Chronicles 4:4 is
the sharpest form of it: the verse holds both *Ephratah* and *Bethlehem*, and BibleData's Bethlehem
record carries a name label for each, so its attestation of the verse was read as an answer about a
word it was never about. A word-level annotation cannot be scored against a verse-level list at
those places without counting a correct reading as a disagreement.

## The third witness

Every case went to Fable 5.1 in a one-shot call with no tools and no repository context, batched by
Strong number so that a name was judged with all of its occurrences and all of its candidates in
front of it, with the same evidence and with both earlier answers shown. Its reply is quoted under
each case and printed in full in the appendix.

It reached the same verdict and the same referent as this adjudication on 68 of
83 cases at the first pass. On the other 15 its
argument was better than mine and I changed my answer; every one of those is marked *revised after
Fable* with what the first verdict was and what changed it. That is not three independent witnesses
converging — it is one reader persuaded by another, and it should be read as one opinion, not two.

There is no case where all three differ and nothing was decided. The two left contested are cases
where Fable and this adjudication agree that the evidence does not decide.


## source-wrong — 57

### Case 2 · 1 Chronicles 15:11 · H6659 · word 6865955

> And David called for Zadok and Abiathar the priests, and for the Levites, for Uriel, Asaiah, and Joel, Shemaiah, and Eliel, and Amminadab,

- **the reading:** `zadok` at high confidence — son of Ahitub and priest during King David's reign (2SA 8:17), a mighty of valor (1CH 12:28) — "David calls for Zadok and Abiathar the priests."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** source-wrong, referent `zadok`, confidence high

David calling for Zadok and Abiathar the priests is the high priest of 2 Samuel 8:17, not the later Zadok of the Ezra genealogy.

**Fable 5.1** — source-wrong, `zadok`, high confidence, the same verdict. Verbatim: "David summons 'Zadok and Abiathar the priests'; this is the Davidic high priest of 2 Sam 8:17 and 15:24, not the post-Davidic genealogical Zadok that zadok-3's own distinguisher describes."

### Case 3 · 1 Chronicles 16:39 · H6659 · word 6866885

> And Zadok the priest, and his brethren the priests, before the tabernacle of the Lord in the high place that was at Gibeon,

- **the reading:** `zadok` at high confidence — son of Ahitub and priest during King David's reign (2SA 8:17), a mighty of valor (1CH 12:28) — "Zadok the priest ministering at Gibeon under David."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** source-wrong, referent `zadok`, confidence high

Zadok the priest before the tabernacle at Gibeon under David is the high priest; zadok-3 belongs to the later line that ends in Ezra.

**Fable 5.1** — source-wrong, `zadok`, high confidence, the same verdict. Verbatim: "Zadok the priest stationed by David before the tabernacle at Gibeon is David's high priest; zadok-3 (ancestor of Hilkiah) lived generations later."

### Case 4 · Ezekiel 40:46 · H6659 · word 6752759

> And the chamber whose prospect is toward the north is for the priests, the keepers of the charge of the altar: these are the sons of Zadok among the sons of Levi, which come near to the Lord to minister unto him.

- **the reading:** `zadok` at high confidence — son of Ahitub and priest during King David's reign (2SA 8:17), a mighty of valor (1CH 12:28) — "Sons of Zadok denotes the priestly line descended from David's high priest Zadok."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** source-wrong, referent `zadok`, confidence high

Ezekiel's sons of Zadok are the Zadokite priesthood, whose eponym is David's high priest.

**Fable 5.1** — source-wrong, `zadok`, high confidence, the same verdict. Verbatim: "'Sons of Zadok' is the Zadokite priestly line founded by Solomon's high priest (1 Kgs 2:35), the same 'house of Zadok' BibleData itself files under zadok at 2 Chr 31:10."

### Case 5 · Ezekiel 43:19 · H6659 · word 6754325

> And thou shalt give to the priests the Levites that be of the seed of Zadok, which approach unto me, to minister unto me, saith the Lord God, a young bullock for a sin offering.

- **the reading:** `zadok` at high confidence — son of Ahitub and priest during King David's reign (2SA 8:17), a mighty of valor (1CH 12:28) — "Seed of Zadok, same priestly lineage as Ezekiel's other references."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** source-wrong, referent `zadok`, confidence high

The seed of Zadok is the same Zadokite line.

**Fable 5.1** — source-wrong, `zadok`, high confidence, the same verdict. Verbatim: "'Seed of Zadok' names the legitimate priesthood descended from David's high priest; zadok-3 is a bare genealogical name with no founding role."

### Case 6 · Ezekiel 44:15 · H6659 · word 6754843

> But the priests the Levites, the sons of Zadok, that kept the charge of my sanctuary when the children of Israel went astray from me, they shall come near to me to minister unto me, and they shall stand before me to offer unto me the fat and the blood, saith the Lord God:

- **the reading:** `zadok` at high confidence — son of Ahitub and priest during King David's reign (2SA 8:17), a mighty of valor (1CH 12:28) — "Sons of Zadok, priestly line, consistent with Ezekiel's usage elsewhere."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** source-wrong, referent `zadok`, confidence high

The sons of Zadok who kept the charge of the sanctuary are the Zadokite priesthood.

**Fable 5.1** — source-wrong, `zadok`, high confidence, the same verdict. Verbatim: "The priests 'the sons of Zadok that kept the charge of my sanctuary' are the Jerusalem line established under Solomon in place of Abiathar's house; the eponym is the Davidic high priest."

### Case 7 · Ezekiel 48:11 · H6659 · word 6757161

> It shall be for the priests that are sanctified of the sons of Zadok; which have kept my charge, which went not astray when the children of Israel went astray, as the Levites went astray.

- **the reading:** `zadok` at high confidence — son of Ahitub and priest during King David's reign (2SA 8:17), a mighty of valor (1CH 12:28) — "Sons of Zadok, priestly line, consistent with Ezekiel's usage elsewhere."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** source-wrong, referent `zadok`, confidence high

The same Zadokite line as Ezekiel 40, 43 and 44.

**Fable 5.1** — source-wrong, `zadok`, high confidence, the same verdict. Verbatim: "Same Zadokite formula as 40:46, 43:19, 44:15; the eponym is David's and Solomon's high priest, not the later Zadok of 1 Chr 6:12."

### Case 8 · 2 Chronicles 31:13 · H5838 · word 6889628

> And Jehiel, and Azaziah, and Nahath, and Asahel, and Jerimoth, and Jozabad, and Eliel, and Ismachiah, and Mahath, and Benaiah, were overseers under the hand of Cononiah and Shimei his brother, at the commandment of Hezekiah the king, and Azariah the ruler of the house of God.

- **the reading:** `azariah-15` at high confidence — chief priest of the house of Zadok (2CH 31:10) — "Same Hezekiah temple-reform narrative, ruler of house of God, same chief priest."
- **BibleData:** `azariah-7` — son of Hilkiah (1CH 6:13), father of Seriah (1CH 6:14), and priest (EZR 2:2, NEH 7:7, NEH 12:1), explained the law to the people (NEH 8:7)
- **verdict:** source-wrong, referent `azariah-15`, confidence high

2 Chronicles 31:10 names Azariah the chief priest of the house of Zadok three verses earlier, and 31:13 calls the same man the ruler of the house of God. azariah-7 is the pre-exilic son of Hilkiah of 1 Chronicles 6:13.

**Fable 5.1** — source-wrong, `azariah-15`, high confidence, the same verdict. Verbatim: "The 'ruler of the house of God' acting with Hezekiah in 31:13 is the chief priest Azariah of 31:10 three verses earlier; azariah-7 (son of Hilkiah, grandfather of the exiled Jehozadak) belongs to Josiah's generation and BibleData's own kinship contradicts placing him under Hezekiah."

### Case 12 · Ezra 8:33 · H3442 · word 6848942

> Now on the fourth day was the silver and the gold and the vessels weighed in the house of our God by the hand of Meremoth the son of Uriah the priest; and with him was Eleazar the son of Phinehas; and with them was Jozabad the son of Jeshua, and Noadiah the son of Binnui, Levites;

- **the reading:** `jeshua-5` at medium confidence — Levite whose sons (descendants) returned to Judah after captivity (EZR 2:40) — "Jozabad's father Jeshua best fits the returned Levitical family head of Ezra 2:40."
- **BibleData:** `jeshua-3` — returned to Judah after the Babylonian captivity (EZR 2:2, NEH 7:7)
- **verdict:** source-wrong, referent `jeshua-5`, confidence high

BibleData's own kinship graph makes jeshua-5 the father of jozabad-5, and jozabad-5 is its record for the Levite of Ezra 8:33. Its verse list contradicts its graph by putting the verse on jeshua-3, the high priest, whose son was Joiakim.

**Fable 5.1** — source-wrong, `jeshua-5`, high confidence, the same verdict. Verbatim: "Jozabad son of Jeshua is expressly a Levite, so his father cannot be the Aaronide high priest Jeshua son of Jozadak; he belongs to the Levitical house of Ezra 2:40, and BibleData's own kinship table already records jeshua-5 as father of jozabad-5, contradicting its verse list."

### Case 14 · Nehemiah 12:10 · H3442 · word 6856606

> And Jeshua begat Joiakim, Joiakim also begat Eliashib, and Eliashib begat Joiada,

- **the reading:** `jeshua-3` at high confidence — returned to Judah after the Babylonian captivity (EZR 2:2, NEH 7:7) — "Genealogy: Jeshua begat Joiakim begat Eliashib, the high-priestly line."
- **BibleData:** `jeshua-7` — explained the law to the people (NEH 8:7), stood on the Levites platform (NEH 9:4), and exhorted Israel to bless the Lord (NEH 9:5)
- **verdict:** source-wrong, referent `jeshua-3`, confidence high

Jeshua begat Joiakim, Joiakim Eliashib, Eliashib Joiada is the high-priestly succession, and Nehemiah 12:26 spells it out: Joiakim the son of Jeshua, the son of Jozadak.

**Fable 5.1** — source-wrong, `jeshua-3`, high confidence, the same verdict. Verbatim: "'Jeshua begat Joiakim... Eliashib... Joiada... Jaddua' is the high-priestly succession, and Neh 12:26 (which BibleData itself assigns to jeshua-3) names Joiakim as son of Jeshua son of Jozadak; filing it under the Levite jeshua-7 contradicts BibleData's own evidence and spawned a spurious jeshua-7 -> joiakim edge."

### Case 15 · 1 Kings 15:33 · H281 · word 6655793

> In the third year of Asa king of Judah began Baasha the son of Ahijah to reign over all Israel in Tirzah, twenty and four years.

- **the reading:** `ahijah-3` at high confidence — of the house of Issachar, father of Baasha (1KI 15:27) — "'Baasha the son of Ahijah' - father of Baasha again."
- **BibleData:** `ahijah` — priest at Shiloh, nephew of Ichabod (1 SA 14:3), and prophet (1KI 11:29)
- **verdict:** source-wrong, referent `ahijah-3`, confidence high

BibleData's ahijah-3 is by its own distinguisher the father of Baasha, and the verse says Baasha the son of Ahijah.

**Fable 5.1** — source-wrong, `ahijah-3`, high confidence, the same verdict. Verbatim: "The word is the patronymic in 'Baasha the son of Ahijah', identical to 1 Kings 15:27, 21:22 and 2 Kings 9:9, which BibleData itself assigns to ahijah-3 (father of Baasha, kinship father->baasha). The verse-list entry under the Shilonite prophet contradicts BibleData's own distinguisher and edges and is an indexing slip."

### Case 18 · Ezra 10:31 · H2766 · word 6850099

> And of the sons of Harim; Eliezer, Ishijah, Malchiah, Shemaiah, Shimeon,

- **the reading:** `harim-2` at high confidence — his sons (descendants) returned to Judah after captivity (EZR 2:32) — "Listed among lay 'Israel' families (Ezra 10:25ff), the lay Harim family."
- **BibleData:** `harim-3` — his sons (descendants) returned to Judah after captivity (EZR 2:39)
- **verdict:** source-wrong, referent `harim-2`, confidence high

Ezra 10:25 begins the lay families - moreover of Israel - and 10:31 stands inside that section, so this Harim is the lay house of Ezra 2:32. The priestly Harim of Ezra 2:39 is answered at 10:21.

**Fable 5.1** — source-wrong, `harim-2`, high confidence, the same verdict. Verbatim: "Same word as above: the lay Harim clan of Ezra 2:32, confirmed by the chapter's priest/laity structure and by BibleData's harim-2 kinship record listing exactly the sons of Ezra 10:31-32; the verse list's harim-3 is the priestly clan of 10:21."

### Case 19 · 1 Chronicles 4:4 · H672 · word 6859629

> And Penuel the father of Gedor, and Ezer the father of Hushah. These are the sons of Hur, the firstborn of Ephratah, the father of Bethlehem.

- **the reading:** `ephrath` at medium confidence — second wife of Chelubai [Caleb] (1CH 2:19) — "Again Hur is 'firstborn of Ephratah,' echoing 1 Chr 2:19/2:50 genealogical link to his mother."
- **BibleData:** `bethlehem` — (no distinguisher)
- **verdict:** source-wrong, referent `ephrath`, confidence high

1 Chronicles 4:4 contains two words BibleData's bethlehem record answers for: אֶפְרָתָה (H672) and בֵּית לָחֶם (H1035), because that record carries a name label for each. Its attestation of the verse is for the second word, and the scoring read it as the answer for the first. Judged on the marked word, *Hur the firstborn of Ephratah* repeats 1 Chronicles 2:19 and 2:50, where Ephrath is Caleb's wife and Hur's mother — BibleData's own ephrath record.

*Revised after Fable.* I had called this contested, on BHSA's topo,pers marking and the place-name reading of Ephratah. Fable found the mechanism: the verse holds Bethlehem as a separate word, so BibleData never answered for this one. Verified in the database — 1 Chronicles 4:4 word 6859631 is בֵּית לָחֶם, H1035.

**Fable 5.1** — source-wrong, `ephrath`, high confidence, the same verdict. Verbatim: "BibleData's 'bethlehem' for this verse is the verse-level attestation of the other word, בֵּית לָחֶם (H1035), not of אֶפְרָתָה; 'Hur the firstborn of Ephrathah' repeats 1 Chr 2:19/2:50, where Ephrath is Caleb's wife and Hur's mother, i.e. BibleData's own 'ephrath' record."

### Case 20 · Genesis 36:2 · H5711 · word 6486326

> Esau took his wives of the daughters of Canaan; Adah the daughter of Elon the Hittite, and Aholibamah the daughter of Anah the daughter of Zibeon the Hivite;

- **the reading:** `basemath` at high confidence — daughter of Elon the Hittite, and wife of Esau (GEN 26:34) — "Adah, daughter of Elon the Hittite, is Gen 26:34's Bashemath under a variant name."
- **BibleData:** `adah` — wife of Lamech (GEN 4:19)
- **verdict:** source-wrong, referent `basemath`, confidence high

BibleData's basemath record carries Adah as one of its own name labels for Esau's wife, and its adah record is the wife of Lamech by its own distinguisher. Genesis 36 cannot be Lamech's wife however the Adah and Bashemath identification is settled.

**Fable 5.1** — source-wrong, `basemath`, high confidence, the same verdict. Verbatim: "The Hebrew names her 'Adah daughter of Elon the Hittite', Esau's wife; BibleData's own `basemath` record carries the label Adah=H5711, the distinguisher 'daughter of Elon the Hittite, wife of Esau', and kinship daughter→elon/wife→esau, while `adah`'s distinguisher and kinship are Lamech's antediluvian wife. The `adah` verse list contradicts its own record."

### Case 21 · Genesis 36:4 · H5711 · word 6486349

> And Adah bare to Esau Eliphaz; and Bashemath bare Reuel;

- **the reading:** `basemath` at high confidence — daughter of Elon the Hittite, and wife of Esau (GEN 26:34) — "Same Adah of 36:2, Elon's daughter, mother of Eliphaz."
- **BibleData:** `adah` — wife of Lamech (GEN 4:19)
- **verdict:** source-wrong, referent `basemath`, confidence high

Adah bearing Eliphaz to Esau is not the wife of Lamech.

**Fable 5.1** — source-wrong, `basemath`, high confidence, the same verdict. Verbatim: "This Adah bears Eliphaz to Esau; `basemath` has the edge mother→eliphaz and wife→esau, whereas `adah` is mother of Jabal and Jubal and wife of Lamech. Only the verse list of `adah` claims this verse, against its distinguisher and kinship."

### Case 22 · Genesis 36:10 · H5711 · word 6486461

> These are the names of Esau's sons; Eliphaz the son of Adah the wife of Esau, Reuel the son of Bashemath the wife of Esau.

- **the reading:** `basemath` at high confidence — daughter of Elon the Hittite, and wife of Esau (GEN 26:34) — "Eliphaz's mother, same Elon's-daughter wife as 36:2/4."
- **BibleData:** `adah` — wife of Lamech (GEN 4:19)
- **verdict:** source-wrong, referent `basemath`, confidence high

Eliphaz the son of Adah the wife of Esau.

**Fable 5.1** — source-wrong, `basemath`, high confidence, the same verdict. Verbatim: "'Eliphaz son of Adah the wife of Esau' matches `basemath`'s kinship (mother→eliphaz, wife→esau) exactly and cannot be Lamech's wife. The neighbouring Bashemath (H1315, mother of Reuel) is a different lexeme and a different wife, so there is no word-level ambiguity."

### Case 23 · Genesis 36:12 · H5711 · word 6486496

> And Timna was concubine to Eliphaz Esau's son; and she bare to Eliphaz Amalek: these were the sons of Adah Esau's wife.

- **the reading:** `basemath` at high confidence — daughter of Elon the Hittite, and wife of Esau (GEN 26:34) — "Continues genealogy of Eliphaz's mother, Elon's daughter."
- **BibleData:** `adah` — wife of Lamech (GEN 4:19)
- **verdict:** source-wrong, referent `basemath`, confidence high

The sons of Adah, Esau's wife.

**Fable 5.1** — source-wrong, `basemath`, high confidence, the same verdict. Verbatim: "'These were the sons of Adah, Esau's wife' closes the Eliphaz clan list; the woman is Esau's Hittite wife, held by BibleData under `basemath` with the Adah=H5711 label. Assigning it to Lamech's wife contradicts `adah`'s own distinguisher and kinship."

### Case 24 · Genesis 36:16 · H5711 · word 6486568

> Duke Korah, duke Gatam, and duke Amalek: these are the dukes that came of Eliphaz in the land of Edom; these were the sons of Adah.

- **the reading:** `basemath` at high confidence — daughter of Elon the Hittite, and wife of Esau (GEN 26:34) — "Dukes descending from Eliphaz son of this same Adah."
- **BibleData:** `adah` — wife of Lamech (GEN 4:19)
- **verdict:** source-wrong, referent `basemath`, confidence high

The sons of Adah in Esau's Edomite chief list.

**Fable 5.1** — source-wrong, `basemath`, high confidence, the same verdict. Verbatim: "'These were the sons of Adah' sums up the chieftains descended from Eliphaz, Esau's firstborn by Adah; the referent is Esau's wife, i.e. BibleData's `basemath` record, not the antediluvian Adah of Genesis 4 that the `adah` record's distinguisher and kinship describe."

### Case 28 · 1 Chronicles 25:22 · H3406 · word 6870953

> The fifteenth to Jeremoth, he, his sons, and his brethren, were twelve:

- **the reading:** `jerimoth-3` at high confidence — son of Heman_3 (1CH 25:4), chosen fifteenth by lot (1CH 25:22) — "The fifteenth lot, matching candidate's exact distinguisher (chosen fifteenth by lot)."
- **BibleData:** `jeremoth-3` — descendant of Mushi (1CH 23:23)
- **verdict:** source-wrong, referent `jerimoth-3`, confidence high

1 Chronicles 25 casts lots among the sons of Asaph, Jeduthun and Heman, and Jerimoth is named in 25:4 among Heman's sons; jeremoth-3 is a Merarite of the Levite courses of 1 Chronicles 23 and 24, not a singer.

**Fable 5.1** — source-wrong, `jerimoth-3`, high confidence, the same verdict. Verbatim: "The lots of 1 Chronicles 25:9-31 are distributed exclusively among the sons of Asaph, Jeduthun, and Heman named in 25:2-4, and the fifteenth lot falls to Heman's son Jerimoth; BibleData's own distinguisher for jerimoth-3 cites 1CH 25:22, but its verse list misfiles the verse under the Merarite jeremoth-3 (descendant of Mushi), a different man from a different roster."

### Case 29 · Nehemiah 12:8 · H6934 · word 6856587

> Moreover the Levites: Jeshua, Binnui, Kadmiel, Sherebiah, Judah, and Mattaniah, which was over the thanksgiving, he and his brethren.

- **the reading:** `kadmiel` at medium confidence — his sons (descendants) returned to Judah after captivity (EZR 2:40) — "Listed 'in the days of Jeshua,' marking this as the earlier returnee-era Kadmiel."
- **BibleData:** `kadmiel-2` — stood on the Levites platform (NEH 9:4) and exhorted Israel to bless the Lord (NEH 9:5)
- **verdict:** source-wrong, referent `kadmiel`, confidence high

Nehemiah 12:1 opens the roster of those who came up with Zerubbabel and Jeshua and 12:7 closes it *in the days of Jeshua*; 12:8 continues it for the Levites. BibleData's split between the two Kadmiels is by era — kadmiel the returnee, kadmiel-2 the Levite of Nehemiah 9 and 10 — and a verse the text dates to Jeshua's day belongs on the returnee's side of its own split.

*Revised after Fable.* I had called this duplicate-records on Strong's holding only one Kadmiel. Fable's point is better: whether the two records should be one is a question about the whole name, and within the split BibleData actually made, this verse is on the wrong side of it.

**Fable 5.1** — source-wrong, `kadmiel`, high confidence, the same verdict. Verbatim: "Nehemiah 12:8 continues the list of Levites who came up with Zerubbabel and Jeshua (12:1, 12:7 'in the days of Jeshua'), i.e. the returnee Kadmiel of Ezra 2:40 / 3:9, whereas kadmiel-2 is by BibleData's own distinguisher the Nehemiah-era Levite of 9:4–5 and 10:9; the verse list contradicts the record's own era-based definition. Residual doubt is only whether the two records are really one Levitical house, which is a Strong-number-wide question and does not change the answer for this word."

### Case 30 · 1 Kings 11:40 · H7895 · word 6652648

> Solomon sought therefore to kill Jeroboam. And Jeroboam arose, and fled into Egypt, unto Shishak king of Egypt, and was in Egypt until the death of Solomon.

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Jeroboam fled to Shishak king of Egypt, same figure as in 1Ki 14:25."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

The verse says Shishak king of Egypt and BibleData's pharaoh-5 is by its own distinguisher the father of Solomon's wife. BibleData holds a shishak record and gives it no verses at all.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "The text names Shishak precisely to distinguish him from the unnamed Pharaoh who was Solomon's father-in-law; BibleData's own `shishak` record (distinguisher 1KI 14:25) and its `pharaoh-5` distinguisher (father of Solomon's wife) both contradict placing this verse under pharaoh-5."

### Case 31 · 1 Kings 14:25 · H7895 · word 6655000

> And it came to pass in the fifth year of king Rehoboam, that Shishak king of Egypt came up against Jerusalem:

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Shishak king of Egypt attacked Jerusalem under Rehoboam; the Shishak candidate."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

Shishak king of Egypt came up against Jerusalem in Rehoboam's fifth year.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "Ketiv שׁושׁק / Qere שִׁישַׁק is the king who plundered Jerusalem in Rehoboam's fifth year — the very verse BibleData's `shishak` distinguisher cites — not Solomon's allied father-in-law of 1KI 3:1."

### Case 32 · 2 Chronicles 12:2 · H7895 · word 6879410

> And it came to pass, that in the fifth year of king Rehoboam Shishak king of Egypt came up against Jerusalem, because they had transgressed against the Lord,

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Same event, Shishak king of Egypt attacking Jerusalem in Rehoboam's 5th year."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

The Chronicler's account of the same campaign.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "Chronicles' parallel to 1KI 14:25; same named hostile king of Egypt, whom BibleData's own separate `shishak` record was created for."

### Case 33 · 2 Chronicles 12:5 · H7895 · word 6879473

> Then came Shemaiah the prophet to Rehoboam, and to the princes of Judah, that were gathered together to Jerusalem because of Shishak, and said unto them, Thus saith the Lord, Ye have forsaken me, and therefore have I also left you in the hand of Shishak.

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Same Shishak narrative in 2 Chronicles 12."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

Shemaiah's oracle names Shishak.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "First of two Shishak occurrences in 2CH 12:5, both referring to the invader of Rehoboam's day; identifying him with the father-in-law Pharaoh is an unattested harmonisation that BibleData's own distinguishers do not make."

### Case 34 · 2 Chronicles 12:5 · H7895 · word 6879490

> Then came Shemaiah the prophet to Rehoboam, and to the princes of Judah, that were gathered together to Jerusalem because of Shishak, and said unto them, Thus saith the Lord, Ye have forsaken me, and therefore have I also left you in the hand of Shishak.

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Same Shishak narrative in 2 Chronicles 12."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

The second Shishak of the same verse.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "Second Shishak occurrence in 2CH 12:5 ('into the hand of Shishak'); same invader, same misassignment to the father-in-law record."

### Case 35 · 2 Chronicles 12:7 · H7895 · word 6879533

> And when the Lord saw that they humbled themselves, the word of the Lord came to Shemaiah, saying, They have humbled themselves; therefore I will not destroy them, but I will grant them some deliverance; and my wrath shall not be poured out upon Jerusalem by the hand of Shishak.

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Same Shishak narrative in 2 Chronicles 12."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

By the hand of Shishak.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "'By the hand of Shishak' continues the 2CH 12 invasion narrative; the referent is the `shishak` record, not the unnamed Pharaoh of Solomon's marriage alliance."

### Case 36 · 2 Chronicles 12:9 · H7895 · word 6879549

> So Shishak king of Egypt came up against Jerusalem, and took away the treasures of the house of the Lord, and the treasures of the king's house; he took all: he carried away also the shields of gold which Solomon had made.

- **the reading:** `shishak` at high confidence — King of Egypt (1KI 14:25) — "Same Shishak narrative in 2 Chronicles 12."
- **BibleData:** `pharaoh-5` — Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)
- **verdict:** source-wrong, referent `shishak`, confidence high

Shishak king of Egypt took the treasures.

**Fable 5.1** — source-wrong, `shishak`, high confidence, the same verdict. Verbatim: "The temple raid itself, parallel to 1KI 14:25–26; BibleData's verse list contradicts both of its own distinguishers by filing it under Solomon's father-in-law."

### Case 37 · 1 Chronicles 15:8 · H469 · word 6865923

> Of the sons of Elizaphan; Shemaiah the chief, and his brethren two hundred:

- **the reading:** `elzaphan` at medium confidence — son of Uzziel (EXO 6:22) — "Levitical family group distinct from Uzziel's line listed separately; likely Kohathite Elzaphan's descendants."
- **BibleData:** `elizaphan` — the leader of the tribe of Zebulun (NUM 34:25) to apportion the Land (NUM 34:17)
- **verdict:** source-wrong, referent `elzaphan`, confidence high

1 Chronicles 15:5-10 lists the Levite houses - Kohath, Merari, Gershom, Elizaphan, Hebron, Uzziel - so the sons of Elizaphan are the Kohathite house of Elzaphan son of Uzziel, not the prince of Zebulun. The two are distinct men, not a duplicate pair.

**Fable 5.1** — source-wrong, `elzaphan`, high confidence, the same verdict. Verbatim: "1 Chr 15:8 lists Kohathite Levitical houses (Hebron, Uzziel, Elizaphan) and parallels 2 Chr 29:13, which BibleData itself files under elzaphan; BibleData's own kinship edges make shemaiah-7 of this verse a descendant of elzaphan. The Zebulunite of Num 34:25 cannot head a Levitical clan, so the verse-list assignment contradicts the record's own evidence."

### Case 38 · Nehemiah 10:18 · H2828 · word 6855573

> Hodijah, Hashum, Bezai,

- **the reading:** `hashum` at high confidence — his sons (descendants) returned to Judah after captivity (EZR 2:19) — "Covenant-sealer list order matches the Ezra2/Neh7 family sequence, identifying this as the family head."
- **BibleData:** `hashum-2` — stood on Ezra's left hand as he read from the Law (NEH 8:4)
- **verdict:** source-wrong, referent `hashum`, confidence medium

Nehemiah 10:14 opens the chief of the people, and the sealers from there on are lay family heads in the order of Ezra 2 and Nehemiah 7; hashum-2 is the man who stood beside Ezra at the reading.

**Fable 5.1** — source-wrong, `hashum`, high confidence, the same verdict. Verbatim: "Neh 10:15-28 is a list of 'heads of the people' by clan name paralleling Ezra 2 / Neh 7, and the sequence Ater, Hezekiah ... Hashum, Bezai, Hariph, Anathoth, Nebai matches Ezra 2:16-19, 29-30 exactly, so Hashum here is the returnee clan of Ezra 2:19 / Neh 7:22, not the individual of Neh 8:4."

### Case 40 · Judges 18:29 · H1835 · word 6605787

> And they called the name of the city Dan, after the name of Dan their father, who was born unto Israel: howbeit the name of the city was Laish at the first.

- **the reading:** `dan` at high confidence — fifth-born son of Jacob (GEN 30:5), son of Bilhah, Rachel's handmaid — "'After the name of Dan their father' — the ancestor."
- **BibleData:** `dan-2` — (no distinguisher)
- **verdict:** source-wrong, referent `dan`, confidence high

The marked word is the second Dan of the verse, in the phrase after the name of Dan their father, and BibleData's verse list holds only the city for this verse. A verse-level list cannot separate the two words.

**Fable 5.1** — source-wrong, `dan`, high confidence, the same verdict. Verbatim: "The marked word is 'Dan their father, who was born to Israel' — the patriarch, son of Jacob, not the city. BibleData's verse-level list only captured the first occurrence in the verse (the city, dan-2) and cannot distinguish the second."

### Case 41 · 1 Chronicles 6:14 · H8304 · word 6860842

> And Azariah begat Seraiah, and Seraiah begat Jehozadak,

- **the reading:** `seraiah-2` at high confidence — the chief priest (2KI 25:18) who was killed by the king of Babylon (2KI 25:21) — "Azariah begat Seraiah, Seraiah begat Jehozadak — matches high-priest genealogy."
- **BibleData:** `seraiah-5` — son of Asiel (1CH 4:35), Simeonite (1CH 4:24)
- **verdict:** source-wrong, referent `seraiah-2`, confidence high

Seraiah who begat Jehozadak is the chief priest of 2 Kings 25:18, and BibleData's own kinship graph says seraiah-2 is the father of jehozadak. seraiah-5 is a Simeonite of 1 Chronicles 4:35.

**Fable 5.1** — source-wrong, `seraiah-2`, high confidence, the same verdict. Verbatim: "1 Chr 6:14 is the high-priestly line (Hilkiah → Azariah → Seraiah → Jehozadak, who went into exile, v. 15), i.e. the chief priest of 2 Kgs 25:18 / Jer 52:24 / Ezra 7:1; BibleData's own seraiah-2 kinship (son of azariah-7, father of jehozadak) is drawn from this very verse, while seraiah-5 is the unrelated Simeonite son of Asiel."

### Case 42 · 1 Chronicles 6:14 · H8304 · word 6860844

> And Azariah begat Seraiah, and Seraiah begat Jehozadak,

- **the reading:** `seraiah-2` at high confidence — the chief priest (2KI 25:18) who was killed by the king of Babylon (2KI 25:21) — "Same clause, same Seraiah who begat Jehozadak, the chief priest."
- **BibleData:** `seraiah-5` — son of Asiel (1CH 4:35), Simeonite (1CH 4:24)
- **verdict:** source-wrong, referent `seraiah-2`, confidence high

The second Seraiah of the same clause, the same man.

**Fable 5.1** — source-wrong, `seraiah-2`, high confidence, the same verdict. Verbatim: "Second mention of the same man in one chain link ('Azariah begat Seraiah, and Seraiah begat Jehozadak'); the father-of-Jehozadak edge on seraiah-2 comes only from here, and the Simeonite seraiah-5 cannot be meant."

### Case 43 · Nehemiah 12:12 · H8304 · word 6856639

> And in the days of Joiakim were priests, the chief of the fathers: of Seraiah, Meraiah; of Jeremiah, Hananiah;

- **the reading:** `seraiah-6` at medium confidence — returned to Judah after the Babylonian captivity (EZR 2:2, NEH 7:7) [called Azariah in NEH 7:7] — "Priestly course named for Seraiah of Neh 12:1, same returnee family head."
- **BibleData:** `seraiah-8` — priest, son of Hilkiah (NEH 11:11)
- **verdict:** source-wrong, referent `seraiah-6`, confidence medium

Nehemiah 12:12 names the priestly houses of 12:1-7, where Seraiah stands first, and BibleData's own graph makes seraiah-6 the father of meraiah, which is the very pairing this verse records.

**Fable 5.1** — source-wrong, `seraiah-6`, high confidence, the same verdict. Verbatim: "Neh 12:12–21 repeats the priestly house names of 12:1–7 in the same order with the next generation's heads ('of Seraiah, Meraiah'), so the eponym here is the Seraiah of 12:1, which BibleData assigns to seraiah-6 and whose 'father -> meraiah' edge is itself derived from this verse; the verse list's seraiah-8 (son of Hilkiah, Neh 11:11) contradicts BibleData's own kinship, though whether seraiah-6 and seraiah-8 are ultimately one priestly house remains an open, separate question."

### Case 44 · Genesis 29:5 · H5152 · word 6481805

> And he said unto them, Know ye Laban the son of Nahor? And they said, We know him.

- **the reading:** `nahor-2` at high confidence — son of Terah (GEN 11:26) — "Laban descends from Bethuel son of Nahor, Abraham's brother."
- **BibleData:** `nahor` — son of Serug (GEN 11:22)
- **verdict:** source-wrong, referent `nahor-2`, confidence high

Laban descends from Bethuel son of Nahor, Abraham's brother, whom BibleData holds as nahor-2.

**Fable 5.1** — source-wrong, `nahor-2`, high confidence, the same verdict. Verbatim: "Laban is son of Bethuel (Gen 28:5), and Bethuel is the son of Nahor and Milcah (Gen 22:22-23; 24:15, 24, 47), so 'son of Nahor' means grandson of Abraham's brother; BibleData's verse list contradicts its own 'son of Serug' distinguisher here."

### Case 45 · Genesis 31:53 · H5152 · word 6484158

> The God of Abraham, and the God of Nahor, the God of their father, judge betwixt us. And Jacob sware by the fear of his father Isaac.

- **the reading:** `nahor-2` at high confidence — son of Terah (GEN 11:26) — "God of Abraham and God of Nahor his brother, sons of Terah."
- **BibleData:** `nahor` — son of Serug (GEN 11:22)
- **verdict:** source-wrong, referent `nahor-2`, confidence high

The God of Abraham and the God of Nahor, the God of their father - their father is Terah, so Nahor is Abraham's brother.

**Fable 5.1** — source-wrong, `nahor-2`, high confidence, the same verdict. Verbatim: "'The God of Abraham and the God of Nahor, the God of their father' pairs the two sons of Terah (Gen 11:26-27; Josh 24:2, which BibleData itself files under nahor-2); the elder Nahor shares no father with Abraham, so only Abraham's brother fits."

### Case 46 · Ezra 7:3 · H568 · word 6847735

> The son of Amariah, the son of Azariah, the son of Meraioth,

- **the reading:** `amariah-2` at high confidence — son of Azariah and father of Ahitub (1CH 6:11), Levite (1CH 6:1), chief priest (2CH 19:11) — "Ezra's ancestor: son of Azariah, father-line to Ahitub, as in 1Chr6:11."
- **BibleData:** `amariah` — son of Meraioth (1CH 6:7) and father of Ahitub (1CH 6:7)
- **verdict:** source-wrong, referent `amariah-2`, confidence medium

Ezra's chain runs Ahitub, Zadok, Shallum, and it is 1 Chronicles 6:11-13 that has Amariah begetting the Ahitub whose Zadok begat Shallum. The Amariah of 1 Chronicles 6:7 fathers the earlier Ahitub, whose Zadok begat Ahimaaz.

**Fable 5.1** — source-wrong, `amariah-2`, high confidence, the same verdict. Verbatim: "Ezra 7:3 calls this Amariah 'son of Azariah' and places him under Ahitub–Zadok–Shallum, which matches the second Amariah of 1 Chr 6:11-12 (BibleData's own amariah-2, 'son of Azariah and father of Ahitub'), not the first Amariah son of Meraioth; the Meraioth in the same verse results from Ezra's abridgment that skips six generations."

### Case 47 · Ezra 2:61 · H1271 · word 6845696

> And of the children of the priests: the children of Habaiah, the children of Koz, the children of Barzillai; which took a wife of the daughters of Barzillai the Gileadite, and was called after their name:

- **the reading:** `barzillai` at high confidence — the Gileadite from Rogelim (2SA 17:27) — "Explicitly 'Barzillai the Gileadite', the father-in-law."
- **BibleData:** `barzillai-3` — man who took a wife from the the daughters of Barzillai and was called by their name (EZR 2:61)
- **verdict:** source-wrong, referent `barzillai`, confidence high

The marked word is the second Barzillai of the verse and carries the gentilic the Gileadite; barzillai-3 is by BibleData's own distinguisher the priest who married into that family, which is the verse's first Barzillai.

**Fable 5.1** — source-wrong, `barzillai`, high confidence, the same verdict. Verbatim: "The marked word carries the gentilic הַגִּלְעָדִי, the title of the Barzillai of Rogelim (2Sa 17:27; 19:32; 1Ki 2:7), and is the father-in-law whose name the priestly clan adopted; BibleData's verse-level list assigns both Barzillais of the verse to barzillai-3, whose own distinguisher describes the priest (the verse's first occurrence), not the Gileadite."

### Case 48 · Nehemiah 7:63 · H1271 · word 6853729

> And of the priests: the children of Habaiah, the children of Koz, the children of Barzillai, which took one of the daughters of Barzillai the Gileadite to wife, and was called after their name.

- **the reading:** `barzillai` at high confidence — the Gileadite from Rogelim (2SA 17:27) — "Explicitly 'Barzillai the Gileadite', matching Ezra parallel."
- **BibleData:** `barzillai-3` — man who took a wife from the the daughters of Barzillai and was called by their name (EZR 2:61)
- **verdict:** source-wrong, referent `barzillai`, confidence high

The Nehemiah parallel of Ezra 2:61, the same phrase and the same word.

**Fable 5.1** — source-wrong, `barzillai`, high confidence, the same verdict. Verbatim: "Identical phrase to the Ezra 2:61 parallel: 'Barzillai the Gileadite' is the man from Rogelim (BibleData's barzillai), while barzillai-3 is by its own distinguisher the priest who married his daughter; the verse-level list cannot separate the two occurrences and misassigns this one."

### Case 49 · Nehemiah 12:6 · H3048 · word 6856568

> Shemaiah, and Joiarib, Jedaiah,

- **the reading:** `jedaiah-2` at high confidence — priest (1CH 9:10, NEH 7:39, NEH 12:6), of the house of Jeshua (EZR 2:36) — "Priest who returned with Zerubbabel, matches distinguisher exactly."
- **BibleData:** `jedaiah-5` — priest, son of Joiarib (NEH 11:10, NEH 12:7)
- **verdict:** source-wrong, referent `jedaiah-2`, confidence high

Nehemiah 12:1-7 and 12:12-21 run in the same order, so 12:6's Jedaiah, after Joiarib, is the house whose head is Uzzi in 12:19, and 12:7's is the house whose head is Nethaneel in 12:21. jedaiah-2's distinguisher cites 12:6 and its kinship names Uzzi; jedaiah-5's cites 12:7 and names Nethaneel. The verse lists have 12:6 and 12:7 exchanged.

*Revised after Fable.* I had called this contested. Fable laid the two lists side by side and showed that three signals in each record agree and only the verse list dissents, which settles it.

**Fable 5.1** — source-wrong, `jedaiah-2`, high confidence, the same verdict. Verbatim: "Neh 12:6's Jedaiah (paired with Joiarib) is the house headed by Uzzi in the parallel 12:19; jedaiah-2's own distinguisher cites NEH 12:6 and its kinship names Uzzi as son, so BibleData's verse-list entry contradicts its own record — the 12:6/12:7 entries are simply swapped between jedaiah-2 and jedaiah-5."

### Case 50 · Nehemiah 12:7 · H3048 · word 6856572

> Sallu, Amok, Hilkiah, Jedaiah. These were the chief of the priests and of their brethren in the days of Jeshua.

- **the reading:** `jedaiah-5` at high confidence — priest, son of Joiarib (NEH 11:10, NEH 12:7) — "Second Jedaiah of the same returnee list, matches son-of-Joiarib distinguisher (NEH 12:7)."
- **BibleData:** `jedaiah-2` — priest (1CH 9:10, NEH 7:39, NEH 12:6), of the house of Jeshua (EZR 2:36)
- **verdict:** source-wrong, referent `jedaiah-5`, confidence high

The other half of the same swap: 12:7's Jedaiah follows Hilkiah, and 12:21 gives that house's head as Nethaneel, which is jedaiah-5's own kinship edge.

*Revised after Fable.* I had called this contested, for the same reason as case 49.

**Fable 5.1** — source-wrong, `jedaiah-5`, high confidence, the same verdict. Verbatim: "Neh 12:7's Jedaiah (after Hilkiah) is the house headed by Nethaneel in the parallel 12:21; jedaiah-5's distinguisher cites NEH 12:7 and its kinship names Nethanel as son, so the verse list's assignment of 12:7 to jedaiah-2 is the swapped half of the same error."

### Case 51 · 1 Chronicles 25:20 · H7619 · word 6870934

> The thirteenth to Shubael, he, his sons, and his brethren, were twelve:

- **the reading:** `shebuel-2` at high confidence — son of Heman_3 (1CH 25:4), chosen thirteenth by lot (1CH 25:20) — "'The thirteenth to Shubael' matches candidate's 'chosen thirteenth by lot.'"
- **BibleData:** `shebuel` — descendant of Gershom (1CH 23:16), officer over the treasures (1CH 26:24)
- **verdict:** source-wrong, referent `shebuel-2`, confidence high

The thirteenth lot in 1 Chronicles 25 falls among the sons of Asaph, Jeduthun and Heman, and Shubael is named in 25:4 among Heman's sons; shebuel is the Gershomite treasurer of 1 Chronicles 26:24.

**Fable 5.1** — source-wrong, `shebuel-2`, high confidence, the same verdict. Verbatim: "The lot list of 1 Chr 25:9–31 distributes exactly the singers named in 25:2–4, and the thirteenth lot goes to Shubael = Shebuel son of Heman (25:4); the Gershonite treasurer of 23:16/24:20/26:24 is not among the musicians. BibleData's own distinguisher for shebuel-2 already cites 1CH 25:20, contradicting its verse list, and the distinguisher is the half that matches the text."

### Case 52 · 1 Chronicles 9:22 · H8050 · word 6863113

> All these which were chosen to be porters in the gates were two hundred and twelve. These were reckoned by their genealogy in their villages, whom David and Samuel the seer did ordain in their set office.

- **the reading:** `samuel-2` at high confidence — son of Elkanah and Hannah (1SA 1:20) — "David and Samuel the seer ordained the porters."
- **BibleData:** `samuel` — the leader of the tribe of Simeon (NUM 34:20) to apportion the Land (NUM 34:17)
- **verdict:** source-wrong, referent `samuel-2`, confidence high

David and Samuel the seer ordaining the porters is the prophet; samuel is the Simeonite prince of Numbers 34:20.

**Fable 5.1** — source-wrong, `samuel-2`, high confidence, the same verdict. Verbatim: "The word is 'Samuel the seer' paired with David — the prophet's title (1 Sam 9:9, 1 Chr 26:28, 29:29) and era; BibleData's `samuel` record is by its own distinguisher and kinship (son of Ammihud) the Simeonite prince of Numbers 34:20, so its verse list is misfiled here."

### Case 53 · 1 Chronicles 11:3 · H8050 · word 6863840

> Therefore came all the elders of Israel to the king to Hebron; and David made a covenant with them in Hebron before the Lord; and they anointed David king over Israel, according to the word of the Lord by Samuel.

- **the reading:** `samuel-2` at high confidence — son of Elkanah and Hannah (1SA 1:20) — "David anointed king according to word of the Lord by Samuel."
- **BibleData:** `samuel` — the leader of the tribe of Simeon (NUM 34:20) to apportion the Land (NUM 34:17)
- **verdict:** source-wrong, referent `samuel-2`, confidence high

Anointed king according to the word of the Lord by Samuel - the prophet.

**Fable 5.1** — source-wrong, `samuel-2`, high confidence, the same verdict. Verbatim: "'The word of the LORD by the hand of Samuel' concerning David's kingship points to the prophet's anointing of David (1 Sam 16) and his oracle against Saul (1 Sam 15:28); the Simeonite Shemuel of Numbers 34 cannot be meant, so BibleData's `samuel` verse list is wrong at this verse."

### Case 54 · 1 Chronicles 5:17 · H3379 · word 6860483

> All these were reckoned by genealogies in the days of Jotham king of Judah, and in the days of Jeroboam king of Israel.

- **the reading:** `jeroboam-2` at medium confidence — King of Israel (2KI 13:13), son of Joash [Jehoash] (2KI 14:16) — "Paired with Jotham king of Judah, chronologically fits Jeroboam II's era."
- **BibleData:** `jeroboam` — Solomon's servant, son of Nebat, who rebelled against him (1KI 11:26), and was later made king of Israel (1KI 12:20)
- **verdict:** source-wrong, referent `jeroboam-2`, confidence high

Jotham of Judah and Jeroboam of Israel are named as contemporaries; Jeroboam son of Nebat is two centuries earlier.

**Fable 5.1** — source-wrong, `jeroboam-2`, high confidence, the same verdict. Verbatim: "The verse dates the Transjordan register 'in the days of Jotham king of Judah' (c. 750–732 BCE); the only Jeroboam whose reign is contemporaneous is Jeroboam II son of Joash, while Jeroboam I died some 150 years earlier. BibleData's verse list put the verse under the son-of-Nebat record against its own definition of jeroboam-2."

### Case 56 · Nehemiah 12:24 · H2811 · word 6856743

> And the chief of the Levites: Hashabiah, Sherebiah, and Jeshua the son of Kadmiel, with their brethren over against them, to praise and to give thanks, according to the commandment of David the man of God, ward over against ward.

- **the reading:** `hashabiah-4` at medium confidence — Levite sent to Ezra (EZR 8:19) — "Paired with Sherebiah as chief of Levites, matching the Ezra 8:24 duo reappearing."
- **BibleData:** `hashabiah-7` — son of Mattaniah (NEH 11:22)
- **verdict:** source-wrong, referent `hashabiah-4`, confidence high

BibleData's hashabiah-7 is, on its own kinship edges, the great-grandfather of Uzzi the serving overseer of Nehemiah 11:22, so he cannot be a chief of the Levites in the same generation. Hashabiah with Sherebiah and Jeshua son of Kadmiel is the company of Ezra 8:18-24 and Nehemiah 9 and 10.

*Revised after Fable.* I had called this contested between hashabiah-4 and hashabiah-7. The generation count in Nehemiah 11:22 rules hashabiah-7 out and I had not read it that closely.

**Fable 5.1** — source-wrong, `hashabiah-4`, high confidence, the same verdict. Verbatim: "BibleData's hashabiah-7 is, by its own kinship edges, the great-grandfather of Uzzi the current overseer (Neh 11:22), so he cannot be a serving chief Levite in Nehemiah's day. The trio Hashabiah–Sherebiah–Jeshua b. Kadmiel is the Ezra 8 / Neh 9–10 company, making this the same man as Ezra 8:19, 24 and Neh 10:11."

### Case 58 · Daniel 11:1 · H1867 · word 6843688

> Also I in the first year of Darius the Mede, even I, stood to confirm and to strengthen him.

- **the reading:** `darius-2` at medium confidence — son of Ahaseurus, a Mede (DAN 9:1) — "Same 'first year of Darius the Mede' framing as Dan 9:1's genealogical identification."
- **BibleData:** `darius` — King of Persia (EZR 4:5)
- **verdict:** source-wrong, referent `darius-2`, confidence high

Darius the Mede is the son of Ahasuerus of Daniel 9:1, not the Persian of Ezra 4:5.

**Fable 5.1** — source-wrong, `darius-2`, high confidence, the same verdict. Verbatim: "The Hebrew reads דָרְיָוֶשׁ הַמָּדִי, 'Darius the Mede,' the same man Dan 9:1 calls son of Ahasuerus of Median seed, whom BibleData itself holds as darius-2; the darius record is distinguished as the Persian king of Ezra 4:5 (Darius Hystaspes), so its verse list contradicts its own distinguisher by including this verse."

### Case 63 · 1 Chronicles 1:41 · H1787 · word 6858440

> The sons of Anah; Dishon. And the sons of Dishon; Amram, and Eshban, and Ithran, and Cheran.

- **the reading:** `dishon-2` at high confidence — son of Anah (GEN 36:25) — ""Sons of Anah: Dishon" parallels Genesis 36:25, son of Anah."
- **BibleData:** `dishon` — son of Seir (GEN 36:21) and chief in Edom (GEN 36:29)
- **verdict:** source-wrong, referent `dishon-2`, confidence medium

The sons of Anah, Dishon - the Dishon of Genesis 36:25, Anah's son. dishon is Seir's son, already named in verse 38.

**Fable 5.1** — source-wrong, `dishon-2`, high confidence, the same verdict. Verbatim: ""The sons of Anah: Dishon" in 1 Chr 1:41 reproduces Gen 36:25, which BibleData itself assigns to dishon-2 (distinguisher 'son of Anah'); the son of Seir appears in the Chronicler's list at 1:38, not 1:41."

### Case 64 · 1 Chronicles 1:41 · H1787 · word 6858443

> The sons of Anah; Dishon. And the sons of Dishon; Amram, and Eshban, and Ithran, and Cheran.

- **the reading:** `dishon-2` at high confidence — son of Anah (GEN 36:25) — "Sons Hemdan(Amram)/Eshban/Ithran/Cheran match Gen 36:26, same Dishon son of Anah."
- **BibleData:** `dishon` — son of Seir (GEN 36:21) and chief in Edom (GEN 36:29)
- **verdict:** source-wrong, referent `dishon-2`, confidence medium

The sons of Dishon - Hamran, Eshban, Ithran and Cheran - are Genesis 36:26's sons of Anah's Dishon.

**Fable 5.1** — source-wrong, `dishon-2`, high confidence, the same verdict. Verbatim: "The Dishon whose sons are Hamran/Hemdan, Eshban, Ithran, Cheran is exactly dishon-2 per BibleData's own kinship edges and Gen 36:26; both occurrences in the verse are the same son of Anah, so the verse-level placement under dishon is wrong for both."

### Case 65 · Jeremiah 36:11 · H1587 · word 6721263

> When Michaiah the son of Gemariah, the son of Shaphan, had heard out of the book all the words of the Lord,

- **the reading:** `gemariah-2` at high confidence — son of Shaphan the scribe (JER 36:10) — "Explicitly son of Shaphan the scribe, matching that candidate."
- **BibleData:** `gemariah` — son of Hilkiah (JER 29:3)
- **verdict:** source-wrong, referent `gemariah-2`, confidence high

The verse says Gemariah the son of Shaphan, which is BibleData's gemariah-2 - a record it gives no verses at all, having put all four Jeremiah 36 verses on the son of Hilkiah.

**Fable 5.1** — source-wrong, `gemariah-2`, high confidence, the same verdict. Verbatim: "The Hebrew reads מִכָיְהוּ בֶן גְּמַרְיָהוּ בֶן שָׁפָן — son of Shaphan and father of Micaiah — exactly the kinship edges BibleData records for gemariah-2; its verse list filed the verse under the son of Hilkiah, contradicting its own distinguisher and genealogy."

### Case 66 · Jeremiah 36:12 · H1587 · word 6721302

> Then he went down into the king's house, into the scribe's chamber: and, lo, all the princes sat there, even Elishama the scribe, and Delaiah the son of Shemaiah, and Elnathan the son of Achbor, and Gemariah the son of Shaphan, and Zedekiah the son of Hananiah, and all the princes.

- **the reading:** `gemariah-2` at high confidence — son of Shaphan the scribe (JER 36:10) — "Same list, explicitly Gemariah son of Shaphan."
- **BibleData:** `gemariah` — son of Hilkiah (JER 29:3)
- **verdict:** source-wrong, referent `gemariah-2`, confidence high

Gemariah the son of Shaphan, named in the list of princes.

**Fable 5.1** — source-wrong, `gemariah-2`, high confidence, the same verdict. Verbatim: "The text gives the patronym explicitly, וּגְמַרְיָהוּ בֶן שָׁפָן, among Jehoiakim's princes; only gemariah-2 is son of Shaphan, and BibleData's verse list contradicts its own record distinguisher."

### Case 67 · Jeremiah 36:25 · H1587 · word 6721621

> Nevertheless Elnathan and Delaiah and Gemariah had made intercession to the king that he would not burn the roll: but he would not hear them.

- **the reading:** `gemariah-2` at high confidence — son of Shaphan the scribe (JER 36:10) — "Same episode as 36:11-12, same Gemariah son of Shaphan."
- **BibleData:** `gemariah` — son of Hilkiah (JER 29:3)
- **verdict:** source-wrong, referent `gemariah-2`, confidence high

The same episode and the same three princes.

**Fable 5.1** — source-wrong, `gemariah-2`, high confidence, the same verdict. Verbatim: "Elnathan, Delaiah, and Gemariah in 36:25 are the same three princes named with patronyms in 36:12 (Achbor, Shemaiah, Shaphan), continuing the same scene before the king; no son of Hilkiah appears in Jeremiah 36."

### Case 68 · 1 Chronicles 26:2 · H2148 · word 6871069

> And the sons of Meshelemiah were, Zechariah the firstborn, Jediael the second, Zebadiah the third, Jathniel the fourth,

- **the reading:** `zechariah-4` at high confidence — son of Meshelemiah [Shallum] (1CH 9:21), gatekeeper (1CH 26:14) — "Firstborn son of Meshelemiah, same family as 1Ch9:21 gatekeeper."
- **BibleData:** `zechariah-5` — second rank Levite, gatekeeper (1CH 15:18), a trumpeter (1CH 15:24)
- **verdict:** source-wrong, referent `zechariah-4`, confidence high

Zechariah the firstborn son of Meshelemiah is 1 Chronicles 9:21's porter; zechariah-5 is the musician of 1 Chronicles 15:18-24.

**Fable 5.1** — source-wrong, `zechariah-4`, high confidence, the same verdict. Verbatim: "The word is 'Zechariah the firstborn' of Meshelemiah the Korahite (26:1-2), the same man 9:21 calls 'Zechariah son of Meshelemiah, gatekeeper' — BibleData's own zechariah-4 record; the verse list misfiles it under zechariah-5, the 15:18/16:5 musician, whom the text never links to Meshelemiah."

### Case 69 · 1 Chronicles 26:14 · H2148 · word 6871262

> And the lot eastward fell to Shelemiah. Then for Zechariah his son, a wise counsellor, they cast lots; and his lot came out northward.

- **the reading:** `zechariah-4` at high confidence — son of Meshelemiah [Shallum] (1CH 9:21), gatekeeper (1CH 26:14) — "Son of Shelemiah (=Meshelemiah), same gatekeeper family as 26:2."
- **BibleData:** `zechariah-5` — second rank Levite, gatekeeper (1CH 15:18), a trumpeter (1CH 15:24)
- **verdict:** source-wrong, referent `zechariah-4`, confidence high

Zechariah his son, the son of Shelemiah - Meshelemiah - of the gatekeeper family of verse 2.

**Fable 5.1** — source-wrong, `zechariah-4`, high confidence, the same verdict. Verbatim: "'Zechariah his son' after Shelemiah (= Meshelemiah of 26:1, 2, 9) is the firstborn gatekeeper of 26:2 and 9:21; zechariah-4's own distinguisher cites 1CH 26:14 while its verse list omits it, and I trust the distinguisher because the filiation in the Hebrew settles it."

### Case 75 · Jeremiah 37:3 · H8018 · word 6721910

> And Zedekiah the king sent Jehucal the son of Shelemiah and Zephaniah the son of Maaseiah the priest to the prophet Jeremiah, saying, Pray now unto the Lord our God for us.

- **the reading:** `shelemiah-8` at high confidence — father of Jucal (JER 38:1) — "Father of Jehucal/Jucal, same person named again in Jer 38:1."
- **BibleData:** `shelemiah-6` — son of Adbeel, sent to sieze Baruch and Jeremiah (JER 36:26)
- **verdict:** source-wrong, referent `shelemiah-8`, confidence medium

Jehucal son of Shelemiah in 37:3 is Jucal son of Shelemiah in 38:1, which is shelemiah-8. shelemiah-6 is the son of Abdeel sent to seize Baruch, and BibleData has given it both Abdeel as father and Jehucal as son.

**Fable 5.1** — source-wrong, `shelemiah-8`, medium confidence, the same verdict. Verbatim: "Jehucal (37:3) and Jucal (38:1) are certainly the same son of Shelemiah under Zedekiah, so the father here is the man of Jer 38:1, shelemiah-8; BibleData's placement under shelemiah-6 rests on an unsupported identification of Jehucal's father with Abdeel's son of 604 BC, which BibleData itself contradicts by keeping shelemiah-8 separate."

### Case 78 · Ezra 7:2 · H285 · word 6847733

> The son of Shallum, the son of Zadok, the son of Ahitub,

- **the reading:** `ahitub-3` at medium confidence — son of Amariah (1CH 6:11), Levite (1CH 6:1), father of Meraioth (1CH 9:11) — "Ezra's genealogy chain (Ahitub-Zadok-Shallum-Hilkiah-Azariah-Seraiah) matches the later Ahitub of 1CH6:11-12."
- **BibleData:** `ahitub-2` — (grand) father of Zadok the priest (2SA 8:17, 1CH 9:11), son of Amariah (1CH 6:11)
- **verdict:** source-wrong, referent `ahitub-3`, confidence medium

Ezra 7:2 has Ahitub's son Zadok and Zadok's son Shallum, which is the 1 Chronicles 6:11-12 line; ahitub-2's Zadok begets Ahimaaz.

**Fable 5.1** — source-wrong, `ahitub-3`, high confidence, the same verdict. Verbatim: "Ezra's chain Seraiah–Azariah–Hilkiah–Shallum–Zadok–Ahitub reproduces 1 Chr 6:11–14 exactly; only the second Zadok fathers Shallum, so this Ahitub is the 1 Chr 6:11–12 man BibleData holds as ahitub-3, not David's Zadok's father (ahitub-2). BibleData's own verse list for ahitub-2 is otherwise coherent for the first Ahitub; its mis-cited distinguisher led the Ezra verse astray."

### Case 81 · Jeremiah 21:1 · H4441 · word 6711709

> The word which came unto Jeremiah from the Lord, when king Zedekiah sent unto him Pashur the son of Melchiah, and Zephaniah the son of Maaseiah the priest, saying,

- **the reading:** `malchijah-10` at medium confidence — father of Pashhur (JER 38:1) and son of King Zedekiah (JER 38:6) — "Pashur son of Malchiah, same father-son pair identified with this candidate at Jer 38:1."
- **BibleData:** `malchijah-2` — father of Pashhur (1CH 9:12), stood on Ezra's left hand as he read from the Law (NEH 8:4)
- **verdict:** source-wrong, referent `malchijah-10`, confidence medium

Pashhur son of Malchiah of Jeremiah 21:1 is the same man as Jeremiah 38:1; malchijah-2 is the priestly ancestor of the Pashhur of 1 Chronicles 9:12.

**Fable 5.1** — source-wrong, `malchijah-10`, high confidence, the same verdict. Verbatim: "Jer 21:1 and Jer 38:1 name the identical pair 'Pashhur son of Malchiah' under the same king Zedekiah, and BibleData itself files 38:1 under malchijah-10; malchijah-2's verse list makes him a contemporary of Nehemiah (Neh 8:4, 10:3, 12:42), which is chronologically impossible for the father of a royal official of 588. Whether this Malchiah is also the 1 Chr 9:12 priestly ancestor remains open, but that would be a later split-and-merge of malchijah-2, not a reason to keep this verse under it."

### Case 82 · Ezra 10:31 · H2766 · word 6850099

> And of the sons of Harim; Eliezer, Ishijah, Malchiah, Shemaiah, Shimeon,

- **the reading:** `harim-2` at medium confidence — his sons (descendants) returned to Judah after captivity (EZR 2:32) — "Listed among lay 'of Israel' families with foreign wives, same clan as EZR 2:32."
- **BibleData:** `harim-3` — his sons (descendants) returned to Judah after captivity (EZR 2:39)
- **verdict:** source-wrong, referent `harim-2`, confidence high

The same word as case 18, answered twice in two runs.

**Fable 5.1** — source-wrong, `harim-2`, high confidence, the same verdict. Verbatim: "Same word as above: the lay Harim clan of Ezra 2:32, confirmed by the chapter's priest/laity structure and by BibleData's harim-2 kinship record listing exactly the sons of Ezra 10:31-32; the verse list's harim-3 is the priestly clan of 10:21."

### Case 83 · 2 Kings 8:29 · H3088 · word 6666988

> And king Joram went back to be healed in Jezreel of the wounds which the Syrians had given him at Ramah, when he fought against Hazael king of Syria. And Ahaziah the son of Jehoram king of Judah went down to see Joram the son of Ahab in Jezreel, because he was sick.

- **the reading:** `jehoram` at high confidence — King of Judah and son of Jehoshaphat (1KI 22:50) — ""Ahaziah son of Jehoram king of Judah" contrasted with Joram son of Ahab in same verse."
- **BibleData:** `jehoram-2` — King of Israel (2KI 1:17) and son of Ahab (2KI 3:1)
- **verdict:** source-wrong, referent `jehoram`, confidence high

Ahaziah the son of Jehoram king of Judah, set against Joram the son of Ahab in the same verse.

**Fable 5.1** — source-wrong, `jehoram`, high confidence, the same verdict. Verbatim: "The marked H3088 word is in the phrase 'Ahaziah son of Jehoram king of Judah', explicitly the Judahite king and father of Ahaziah, contrasted in the same verse with Joram (H3141) son of Ahab; BibleData's verse-level list only registers the Israelite king here and omits the Judahite one."


## model-wrong — 7

### Case 9 · Nehemiah 8:7 · H5838 · word 6854130

> Also Jeshua, and Bani, and Sherebiah, Jamin, Akkub, Shabbethai, Hodijah, Maaseiah, Kelita, Azariah, Jozabad, Hanan, Pelaiah, and the Levites, caused the people to understand the law: and the people stood in their place.

- **the reading:** `azariah-7` at high confidence — son of Hilkiah (1CH 6:13), father of Seriah (1CH 6:14), and priest (EZR 2:2, NEH 7:7, NEH 12:1), explained the law to the people (NEH 8:7) — "Distinguisher explicitly cites Nehemiah 8:7, explained the law to the people."
- **BibleData:** `azariah-16` — carried out repairs beside his house (NEH 3:23)
- **verdict:** model-wrong, referent `azariah-16`, confidence medium

azariah-7 is the son of Hilkiah of 1 Chronicles 6:13, father of the Seraiah killed at the fall of Jerusalem; he cannot stand in Ezra's assembly. The model was reading BibleData's distinguisher, which cites Nehemiah 8:7 although its own verse list does not.

**Fable 5.1** — model-wrong, `azariah-16`, medium confidence, the same verdict. Verbatim: "azariah-7 is the pre-exilic son of Hilkiah and cannot teach the law in 445 BC; the model trusted a distinguisher whose Ezra-Nehemiah citations are a conflation. azariah-16 is the only listed record of the right period, though its identification of the Levite teacher with the wall-builder son of Maaseiah is unproven and would better be a separate record."

### Case 13 · Nehemiah 12:8 · H3442 · word 6856585

> Moreover the Levites: Jeshua, Binnui, Kadmiel, Sherebiah, Judah, and Mattaniah, which was over the thanksgiving, he and his brethren.

- **the reading:** `jeshua-7` at medium confidence — explained the law to the people (NEH 8:7), stood on the Levites platform (NEH 9:4), and exhorted Israel to bless the Lord (NEH 9:5) — "Leading Levite grouped with Kadmiel and Sherebiah, as in Neh 8:7/9:4-5."
- **BibleData:** `jeshua-5` — Levite whose sons (descendants) returned to Judah after captivity (EZR 2:40)
- **verdict:** model-wrong, referent `jeshua-5`, confidence medium

Ezra 2:40 pairs Jeshua and Kadmiel as the returning Levite houses, exactly as Nehemiah 12:8 does. jeshua-7 is the Levite of Nehemiah 8:7 and 9:4.

**Fable 5.1** — model-wrong, `jeshua-5`, medium confidence, the same verdict. Verbatim: "Neh 12:1-9 is the roster of those who came up with Zerubbabel and Jeshua, i.e. the same return generation as Ezra 2:40, so the Levite Jeshua here is the returning house-head jeshua-5; the Neh 8-9 Levite (jeshua-7) is two or three generations later, and the recurrence of Kadmiel and Sherebiah reflects persistent house names, not identity of person."

### Case 55 · Nehemiah 10:11 · H2811 · word 6855548

> Micha, Rehob, Hashabiah,

- **the reading:** `hashabiah` at high confidence — son of Amaziah (1CH 6:45), Levite during the return (NEH 10:11, NEH 12:24) — "Candidate's own citation names this verse among Levites sealing the covenant."
- **BibleData:** `hashabiah-4` — Levite sent to Ezra (EZR 8:19)
- **verdict:** model-wrong, referent `hashabiah-4`, confidence medium

hashabiah is the Merarite son of Amaziah of 1 Chronicles 6:45, an ancestor of the exile, and cannot seal Nehemiah's covenant. The model followed BibleData's distinguisher, which cites Nehemiah 10:11 although its verse list does not.

**Fable 5.1** — model-wrong, `hashabiah-4`, high confidence, the same verdict. Verbatim: "The model followed the stray '(NEH 10:11, NEH 12:24)' tail in the distinguisher of `hashabiah`, whose verse list and kinship edges (son of Amaziah, 1 Chr 6:45) make him a David-era ancestor of Ethan, impossible among Nehemiah's covenant-sealers. The sealer paired with Sherebiah is the Levite who came up with Ezra (Ezra 8:19, 24), i.e. hashabiah-4; the empty stub hashabiah-6 should be folded into it."

### Case 62 · Nehemiah 8:7 · H6126 · word 6854125

> Also Jeshua, and Bani, and Sherebiah, Jamin, Akkub, Shabbethai, Hodijah, Maaseiah, Kelita, Azariah, Jozabad, Hanan, Pelaiah, and the Levites, caused the people to understand the law: and the people stood in their place.

- **the reading:** `akkub-3` at high confidence — temple servant (EZR 2:45), explained the law to the people (NEH 8:7) — "Listed among Levites who helped expound the law, matching Nehemiah 8:7 distinguisher."
- **BibleData:** `akkub-2` — one of the gatekeepers (1CH 9:17, EZR 2:42, NEH 7:45, NEH 12:25)
- **verdict:** model-wrong, referent `akkub-2`, confidence medium

Ezra 2 separates the returnees by class, and akkub-3 is a head of Nethinim at 2:45 — temple servants, not Levites — so he cannot be one of the Levites expounding the law in Nehemiah's day. The model followed akkub-3's distinguisher, which cites Nehemiah 8:7 against its own verse list. akkub-2 the gatekeeper is a Levite of the right generation and the only record that fits, though Strong keeps the law-expounder as a separate man and the encyclopedia may simply not hold him.

*Revised after Fable.* I had called this both-wrong on Strong's separating the two. Fable is right that Strong's numbering counts contexts rather than men, and that the class boundary in Ezra 2 rules akkub-3 out decisively while nothing rules akkub-2 out.

**Fable 5.1** — model-wrong, `akkub-2`, medium confidence, the same verdict. Verbatim: "akkub-3 is a Nethinim family head of the 538 return (Ezra 2:45, a class distinct from Levites, and dropped in Neh 7:48), so it cannot be a Levite teaching Torah in Nehemiah's day; the model followed the record's erroneous '(NEH 8:7)' distinguisher, which its own verse list contradicts. The gatekeeper akkub-2 is a Levite contemporary of Nehemiah (Neh 11:19; 12:25) and the only record that fits, though the lexica's view that Neh 8:7 names a separate Levite (Strong's #5, no BibleData record) cannot be excluded."

### Case 73 · 1 Chronicles 6:26 · H511 · word 6860944

> As for Elkanah: the sons of Elkanah; Zophai his son, and Nahath his son,

- **the reading:** `elkanah-4` at high confidence — father of Amasai and Ahimoth (1CH 6:25) and son of Joel (1CH 6:36) — "'As for Elkanah' resumes the Elkanah just named as father of Amasai/Ahimoth in v.25."
- **BibleData:** `elkanah-5` — father of Zophai [Zuph] (1CH 6:26) and son of Mahath (1CH 6:35)
- **verdict:** model-wrong, referent `elkanah-5`, confidence medium

The ascending line of 1 Chronicles 6:35 runs Zuph, Elkanah, Mahath, Amasai, Elkanah, so the Elkanah whose son is Zophai stands one generation below Amasai's father. elkanah-4 is that father.

**Fable 5.1** — model-wrong, `elkanah-5`, high confidence, the same verdict. Verbatim: "The first Elkanah of 6:26 is the father of Zophai/Zuph on every reading (ketiv 'his son' = son of Ahimoth/Mahath; qere casus pendens resumed by the same name; or dittography), and 6:35 makes Zuph's father the Elkanah son of Mahath, three generations below the Elkanah of v.25. The model's 'as for' resumption rests on the KJV gloss and would contradict the parallel pedigree."

### Case 77 · 2 Chronicles 25:23 · H3059 · word 6886221

> And Joash the king of Israel took Amaziah king of Judah, the son of Joash, the son of Jehoahaz, at Bethshemesh, and brought him to Jerusalem, and brake down the wall of Jerusalem from the gate of Ephraim to the corner gate, four hundred cubits.

- **the reading:** `jehoahaz` at high confidence — King of Israel, son of Jehu (2KI 10:35) — "Joash king of Israel, son of Jehoahaz, in same account."
- **BibleData:** `ahaziah-2` — King of Judah, youngest (2CH 21:17) son of Jehoram (2KI 8:24) son of Jehoshapat
- **verdict:** model-wrong, referent `ahaziah-2`, confidence high

Amaziah of Judah's father was Joash of Judah and his grandfather Ahaziah, whom the Chronicler spells Jehoahaz here and at 21:17. Jehoahaz of Israel is the father of the Joash who took Amaziah captive, not Amaziah's own grandfather.

**Fable 5.1** — model-wrong, `ahaziah-2`, high confidence, the same verdict. Verbatim: "The בֶּן chain 'son of Joash, son of Jehoahaz' modifies Amaziah king of Judah (the object), whose grandfather was Ahaziah of Judah, spelled Jehoahaz by the Chronicler as in 2 Chr 21:17; the parallel 2 Kings 14:13 reads 'son of Jehoash the son of Ahaziah'. The model attached the patronymic to the Israelite Joash, the verse's post-verbal subject, which carries no patronymic."

### Case 80 · Nehemiah 10:12 · H7645 · word 6855551

> Zaccur, Sherebiah, Shebaniah,

- **the reading:** `shebaniah-4` at high confidence — second Levite named Shebaniah in the sealed document of NEH 10:1-27 (v10 and v12) — "Candidate explicitly cited as appearing at both v10 and v12 of the Levite sealers list."
- **BibleData:** `shebaniah-2` — stood on the Levites platform (NEH 9:4) and exhorted Israel to bless the Lord (NEH 9:5)
- **verdict:** model-wrong, referent `shebaniah-2`, confidence medium

BibleData partitions the four Shebaniahs of Nehemiah 10 cleanly — the priest at 10:4, shebaniah-4 at 10:10, shebaniah-2 at 10:12 — and the parenthetical *(v10 and v12)* in shebaniah-4's distinguisher reads more naturally as *the two Levite Shebaniahs are at v10 and v12, and this is the other one* than as a claim to both. The model's reading puts two records on one word. What the text does not settle is which of the two sealers is the Levite of Nehemiah 9:4, and BibleData's answer to that is a convention rather than a demonstration.

*Revised after Fable.* I had called this contested, and the earlier phase-2 report called it a case where nothing decides it. That is still true of which sealer is which; it is not true of this word, because within BibleData's partition only one record claims it.

**Fable 5.1** — model-wrong, `shebaniah-2`, medium confidence, the same verdict. Verbatim: "The Levite sealers list has two Shebaniahs (10:10 and 10:12); BibleData's verse lists partition them cleanly as shebaniah-4 at 10:10 and shebaniah-2 (the 9:4-5 platform Levite) at 10:12, and Sonnet's answer rests on misreading the ambiguous parenthetical '(v10 and v12)' in shebaniah-4's distinguisher as a double attestation, which would put two records on one word. Confidence is medium only because the text itself cannot show which of the two sealers is the 9:4 man; the assignment is BibleData's coherent convention, not a demonstrable identification."


## duplicate-records — 13

### Case 1 · 1 Chronicles 9:11 · H6659 · word 6862917

> And Azariah the son of Hilkiah, the son of Meshullam, the son of Zadok, the son of Meraioth, the son of Ahitub, the ruler of the house of God;

- **the reading:** `zadok-6` at high confidence — son of Meraioth (NEH 11:11) — "Zadok son of Meraioth, father's line to Meshullam, matches Neh 11:11 genealogy exactly."
- **BibleData:** `zadok-3` — (grand)son of Ahitub and father of Shallum (1CH 6:12)
- **verdict:** duplicate-records, referent `zadok-3`, confidence high

1 Chronicles 9:11 and Nehemiah 11:11 are the same chain, so both records are right about the man: zadok-3 is anchored on 1 Chronicles 6:12 and Ezra 7:2 and zadok-6 exists for the Nehemiah verse alone. zadok-3 should survive and absorb zadok-6, and zadok-6's other verse, Nehemiah 13:13, is the scribe-treasurer and a third man who needs a record of his own.

*Revised after Fable.* I had called this source-wrong with zadok-6 as the referent. Fable is right that neither record is wrong about the person, which is what duplicate-records means, and that zadok-3 is the better survivor because it carries the two anchoring genealogies.

**Fable 5.1** — duplicate-records, `zadok-3`, high confidence, the same verdict. Verbatim: "1 Chr 9:11's chain (Hilkiah–Meshullam–Zadok–Meraioth–Ahitub) is verbatim Neh 11:11 and matches 1 Chr 6:12 / Ezra 7:2 with Shallum=Meshullam; zadok-3 and zadok-6 are the same later Zadok held twice." Survivor, verbatim: "zadok-3 survives and absorbs zadok-6 (Neh 11:11 merges in; zadok-6's Neh 13:13, the scribe-treasurer, is a different man and should get its own record)"

### Case 17 · Nehemiah 12:8 · H4983 · word 6856590

> Moreover the Levites: Jeshua, Binnui, Kadmiel, Sherebiah, Judah, and Mattaniah, which was over the thanksgiving, he and his brethren.

- **the reading:** `mattaniah-2` at high confidence — son of Mica (1CH 9:15), Levite (NEH 12:8) — "Candidate's distinguisher explicitly cites this verse: over the thanksgiving."
- **BibleData:** `mattaniah-9` — descendant of Mica (NEH 11:17), gatekeeper (NEH 12:25)
- **verdict:** duplicate-records, referent `mattaniah-9`, confidence medium

1 Chronicles 9:15 and Nehemiah 11:17 are parallel lists of the same man, Mattaniah son of Micha of the sons of Asaph, so mattaniah-2 and mattaniah-9 are one person twice. Nehemiah 12:8 says he was over the thanksgiving, which is Nehemiah 11:17's own phrase.

**Fable 5.1** — duplicate-records, `mattaniah-9`, high confidence, the same verdict. Verbatim: "Neh 12:8's Mattaniah over the thanksgiving, paired with Bakbukiah, is the Mattaniah son of Mica of Neh 11:17 and its Chronicles parallel 1 Chr 9:15; BibleData holds that one Asaphite under both mattaniah-2 (1 Chr 9:15, whose distinguisher itself cites Neh 12:8) and mattaniah-9 (Neh 11:17). The chronological stretch from Zerubbabel to Nehemiah is a real wrinkle but does not yield a distinct candidate record." Survivor, verbatim: "mattaniah-9 survives and absorbs mattaniah-2 (taking 1 Chr 9:15); mattaniah-2's 2 Chr 20:14 attestation belongs to a different, Jehoshaphat-era Mattaniah and should be split into its own record"

### Case 25 · Joshua 17:2 · H44 · word 6589163

> There was also a lot for the rest of the children of Manasseh by their families; for the children of Abiezer, and for the children of Helek, and for the children of Asriel, and for the children of Shechem, and for the children of Hepher, and for the children of Shemida: these were the male children of Manasseh the son of Joseph by their families.

- **the reading:** `abiezer-2` at high confidence — son of Hammolecheth (1CH 7:18) — "Manassite clan, son of Gilead's sister Hammolecheth per 1 Chr 7:18."
- **BibleData:** `iezer` — son of Gilead (NUM 26:30)
- **verdict:** duplicate-records, referent `iezer`, confidence medium

Strong states that the Manassite Abiezer is called a son of Gilead and also the son of Gilead's sister, so iezer (Numbers 26:30) and abiezer-2 (1 Chronicles 7:18) are one man held twice.

**Fable 5.1** — duplicate-records, `iezer`, high confidence, the same verdict. Verbatim: "Josh 17:2's six-clan list is identical to Num 26:30-32 where the first clan is spelled Iezer, so this is the Manassite clan eponym BibleData holds as `iezer`; the model's `abiezer-2` (son of Hammolecheth, 1 Chr 7:18) is the Chronicler's re-parented version of the same clan founder, i.e. the same man under a second record." Survivor, verbatim: "iezer survives, absorbing abiezer-2 (carry over 1 Chr 7:18 and the Hammolecheth edge as an alternative genealogy)"

### Case 26 · Judges 8:2 · H44 · word 6598942

> And he said unto them, What have I done now in comparison of you? Is not the gleaning of the grapes of Ephraim better than the vintage of Abiezer?

- **the reading:** `abiezer-2` at high confidence — son of Hammolecheth (1CH 7:18) — "Same Manassite clan tied to Gideon's vintage/harvest imagery."
- **BibleData:** `iezer` — son of Gilead (NUM 26:30)
- **verdict:** duplicate-records, referent `iezer`, confidence medium

The same clan and the same man; the vintage of Abiezer is Gideon's Manassite house.

**Fable 5.1** — duplicate-records, `iezer`, high confidence, the same verdict. Verbatim: "'The vintage of Abiezer' is Gideon's own clan (Joash the Abiezrite, Judg 6:11, 6:34, 8:32), the Manassite clan of Num 26:30/Josh 17:2 that BibleData's `iezer` record already links to Joash; the model's `abiezer-2` names the same clan eponym via 1 Chr 7:18." Survivor, verbatim: "iezer survives, absorbing abiezer-2"

### Case 39 · 1 Chronicles 6:37 · H43 · word 6861066

> The son of Tahath, the son of Assir, the son of Ebiasaph, the son of Korah,

- **the reading:** `abiasaph` at high confidence — son of Korah (EXO 6:24) — "Genealogy makes him son of Korah directly (Assir son of Ebiasaph son of Korah)."
- **BibleData:** `ebiasaph` — son of Elkanah (1CH 6:23)
- **verdict:** duplicate-records, referent `abiasaph`, confidence high

Strong H43 holds one referent, a son or descendant of Korah, and abiasaph already carries Ebiasaph as one of its name labels. abiasaph should survive and absorb ebiasaph.

**Fable 5.1** — duplicate-records, `abiasaph`, medium confidence, the same verdict. Verbatim: "1 Chr 6:37 explicitly makes this Ebiasaph the son of Korah, contradicting the 'son of Elkanah' distinguisher of the record BibleData files it under; with Exod 6:24, 1 Chr 9:19 and 26:1 all agreeing on son-of-Korah, the 6:23 'son of Elkanah' is the same man in a garbled chain, not a second person." Survivor, verbatim: "abiasaph survives; it absorbs ebiasaph (1 Chr 6:23, 6:37 and the father-of-Assir edge)"

### Case 57 · 1 Chronicles 1:41 · H6034 · word 6858439

> The sons of Anah; Dishon. And the sons of Dishon; Amram, and Eshban, and Ithran, and Cheran.

- **the reading:** `beeri` at high confidence — the Hittite (GEN 26:34), also called Anah (GEN 36:2) — "Sons of Anah: Dishon, parallel to Genesis 36:25's same Anah."
- **BibleData:** `anah` — son of Seir (GEN 36:20) and chief in Edom (GEN 36:29)
- **verdict:** duplicate-records, referent `anah`, confidence medium

1 Chronicles 1:41 abridges Genesis 36:25 and stands in the fourth-son-of-Seir slot opened at 1:38, and the Anah who fathers Dishon and Aholibamah is the Anah counted among Seir's sons. anah should survive and absorb the Anah-labelled verses of beeri; Genesis 26:34 should stay behind as a bare Beeri-the-Hittite record, because equating Beeri with Anah is rabbinic harmonisation the Hebrew does not make.

*Revised after Fable.* I had called this source-wrong with beeri as the referent, taking BibleData's two Anahs at face value. Fable's reading — that the doubled genealogy is the text's own and the two records are one man — is better, and it also exposes the Beeri identification as an inference rather than a reading.

**Fable 5.1** — duplicate-records, `anah`, medium confidence, the same verdict. Verbatim: "1 Chr 1:41 'sons of Anah: Dishon' copies Gen 36:25 in the fourth-son-of-Seir slot of 1:38, and Gen 36:25/36:2/36:14 make this Dishon-and-Aholibamah-fathering Anah the son of Zibeon counted among Seir's sons — one man that BibleData splits between `anah` and the composite `beeri`." Survivor, verbatim: "anah survives, absorbing the Anah-labelled verses and Dishon/Aholibamah/Zibeon edges of beeri; Gen 26:34 should remain as a separate Beeri-the-Hittite record, since the Beeri=Anah equation is rabbinic harmonization, not the Hebrew text"

### Case 59 · Ezra 2:37 · H564 · word 6845479

> The children of Immer, a thousand fifty and two.

- **the reading:** `immer` at high confidence — father of Meshillemith (1CH 9:12), priest (EZR 2:37, NEH 7:40) — "Distinguisher explicitly cites this verse for the priestly clan count."
- **BibleData:** `immer-2` — sixteenth-chosen priest for the twenty-four courses (1CH 24:14)
- **verdict:** duplicate-records, referent `immer-2`, confidence high

Both records claim this verse: immer by its distinguisher and immer-2 by its verse list, and both describe the same priestly clan founded by the sixteenth course of 1 Chronicles 24:14. The 1,052 children of Immer are that course counted by clan. immer-2 should survive, since its distinguisher, verse list and kinship agree with each other.

*Revised after Fable.* I had called this contested because both records are priestly Immers. Fable's observation that the two halves of one record and the whole of the other claim the same verses is the definition of a duplicate, and I had read the same contradiction without drawing that conclusion.

**Fable 5.1** — duplicate-records, `immer-2`, high confidence, the same verdict. Verbatim: "The 1,052 'children of Immer' are the priestly course founded in 1 Chr 24:14 counted by clan; BibleData's `immer` claims this verse in its distinguisher while `immer-2` holds it in its verse list, so the one clan eponym sits under two records." Survivor, verbatim: "immer-2 survives, absorbing immer"

### Case 60 · Ezra 10:20 · H564 · word 6849958

> And of the sons of Immer; Hanani, and Zebadiah.

- **the reading:** `immer` at medium confidence — father of Meshillemith (1CH 9:12), priest (EZR 2:37, NEH 7:40) — "Sons of Immer clan, same priestly lineage counted in Ezra 2:37."
- **BibleData:** `immer-2` — sixteenth-chosen priest for the twenty-four courses (1CH 24:14)
- **verdict:** duplicate-records, referent `immer-2`, confidence high

The sons of Immer at Ezra 10:20 are Hanani and Zebadiah, and BibleData records them as descendants of immer while filing the verse under immer-2 — one clan, two records.

*Revised after Fable.* I had called this contested, for the same reason as case 59.

**Fable 5.1** — duplicate-records, `immer-2`, high confidence, the same verdict. Verbatim: "Hanani and Zebadiah are priests of the house of Immer (the Ezra 2 priestly clans in order); `immer` carries the kinship edges to hanani-3 and zebadiah-9 while `immer-2` carries the verse, the same man split across two records." Survivor, verbatim: "immer-2 survives, absorbing immer"

### Case 61 · Nehemiah 7:40 · H564 · word 6853521

> The children of Immer, a thousand fifty and two.

- **the reading:** `immer` at high confidence — father of Meshillemith (1CH 9:12), priest (EZR 2:37, NEH 7:40) — "Distinguisher explicitly cites this verse, duplicate of Ezra 2:37's list."
- **BibleData:** `immer-2` — sixteenth-chosen priest for the twenty-four courses (1CH 24:14)
- **verdict:** duplicate-records, referent `immer-2`, confidence high

The Nehemiah 7:40 parallel of Ezra 2:37, the same clan under the same two records.

*Revised after Fable.* I had called this contested, for the same reason as case 59.

**Fable 5.1** — duplicate-records, `immer-2`, high confidence, the same verdict. Verbatim: "Verbatim parallel of Ezra 2:37; both `immer` (distinguisher) and `immer-2` (verse list) claim it for the same clan eponym of 1 Chr 24:14." Survivor, verbatim: "immer-2 survives, absorbing immer"

### Case 70 · 2 Chronicles 35:15 · H623 · word 6892506

> And the singers the sons of Asaph were in their place, according to the commandment of David, and Asaph, and Heman, and Jeduthun the king's seer; and the porters waited at every gate; they might not depart from their service; for their brethren the Levites prepared for them.

- **the reading:** `asaph-2` at high confidence — son of Berechiah (1CH 6:39), chosen first by lot (1CH 25:9) — "Sons of Asaph in their place per commandment of David and Asaph, the musician's guild."
- **BibleData:** `asaph-3` — father of Zichri (1CH 9:15), singer (EZR 2:41, NEH 7:44)
- **verdict:** duplicate-records, referent `asaph-2`, confidence medium

asaph-3 is described by the guild's post-exilic appearances and asaph-2 by the Davidic musician himself, but the sons of Asaph are descendants of one man; the two records are one person split between his life and his guild.

**Fable 5.1** — duplicate-records, `asaph-2`, high confidence, the same verdict. Verbatim: ""The singers the sons of Asaph" is the guild named for the Davidic Asaph, and the same verse grounds that guild in his own commandment; BibleData's asaph-3 is the same man filed a second time as guild-eponym, as its own kinship (asaph-2 ancestor of mattaniah-4, whose verse Neh 11:17 sits under asaph-3) shows." Survivor, verbatim: "asaph-2 survives and absorbs asaph-3"

### Case 71 · 2 Chronicles 35:15 · H623 · word 6892513

> And the singers the sons of Asaph were in their place, according to the commandment of David, and Asaph, and Heman, and Jeduthun the king's seer; and the porters waited at every gate; they might not depart from their service; for their brethren the Levites prepared for them.

- **the reading:** `asaph-2` at high confidence — son of Berechiah (1CH 6:39), chosen first by lot (1CH 25:9) — "Same verse, 'commandment of David and Asaph' the original chief musician."
- **BibleData:** `asaph-3` — father of Zichri (1CH 9:15), singer (EZR 2:41, NEH 7:44)
- **verdict:** duplicate-records, referent `asaph-2`, confidence high

asaph-3 is the Davidic Asaph filed a second time as the guild's ancestor: BibleData's own kinship makes Mattaniah of Nehemiah 11:17 a descendant of asaph-2 while filing that verse under asaph-3. So BibleData is not wrong about the man here, only about which of its two records for him it used.

*Revised after Fable.* I had called this source-wrong because *the commandment of David, and Asaph, and Heman, and Jeduthun* is unmistakably the Davidic musician. That is still true, but if the two records are one man then the source is not wrong about who is meant, and the same verdict should cover all three Asaph occurrences.

**Fable 5.1** — duplicate-records, `asaph-2`, high confidence, the same verdict. Verbatim: "The name stands in the fixed Davidic triad Asaph–Heman–Jeduthun (1 Chr 25:1; 2 Chr 5:12; 29:13), David's contemporary, which even BibleData's own asaph-2 distinguisher describes; the asaph-3 label here is a verse-level artefact of the duplicate record." Survivor, verbatim: "asaph-2 survives and absorbs asaph-3"

### Case 72 · Ezra 3:10 · H623 · word 6846158

> And when the builders laid the foundation of the temple of the Lord, they set the priests in their apparel with trumpets, and the Levites the sons of Asaph with cymbals, to praise the Lord, after the ordinance of David king of Israel.

- **the reading:** `asaph-2` at medium confidence — son of Berechiah (1CH 6:39), chosen first by lot (1CH 25:9) — "Cymbals 'after the ordinance of David' point to the original Davidic musician's guild."
- **BibleData:** `asaph-3` — father of Zichri (1CH 9:15), singer (EZR 2:41, NEH 7:44)
- **verdict:** duplicate-records, referent `asaph-2`, confidence medium

The Levites the sons of Asaph with cymbals after the ordinance of David - the same guild and the same eponym.

**Fable 5.1** — duplicate-records, `asaph-2`, high confidence, the same verdict. Verbatim: ""The Levites the sons of Asaph" acting "after the ordinance of David" is the singers' guild of the Davidic Asaph; no verse anywhere introduces a second singer-Asaph, so asaph-3 is a redundant record for the same man." Survivor, verbatim: "asaph-2 survives and absorbs asaph-3"

### Case 79 · Nehemiah 11:11 · H285 · word 6856184

> Seraiah the son of Hilkiah, the son of Meshullam, the son of Zadok, the son of Meraioth, the son of Ahitub, was the ruler of the house of God.

- **the reading:** `ahitub-3` at high confidence — son of Amariah (1CH 6:11), Levite (1CH 6:1), father of Meraioth (1CH 9:11) — "Parallel list to 1CH9:11 with identical Zadok-Meraioth-Ahitub chain."
- **BibleData:** `ahitub-4` — leader of the house of God (NEH 11:11)
- **verdict:** duplicate-records, referent `ahitub-3`, confidence medium

Nehemiah 11:11 and 1 Chronicles 9:11 are the same chain, and ahitub-4 exists only for the Nehemiah verse while ahitub-3 holds the Chronicles one.

**Fable 5.1** — duplicate-records, `ahitub-3`, high confidence, the same verdict. Verbatim: "Neh 11:11 and 1 Chr 9:11 give the identical chain Hilkiah–Meshullam–Zadok–Meraioth–Ahitub, so this is the same man BibleData already assigns to ahitub-3 at 1 Chr 9:11; ahitub-4 (with its twin meraioth-3) is a duplicate created from the Nehemiah parallel." Survivor, verbatim: "ahitub-3 survives, absorbing ahitub-4"


## contested — 2

### Case 27 · 2 Kings 9:31 · H2174 · word 6667754

> And as Jehu entered in at the gate, she said, Had Zimri peace, who slew his master?

- **the reading:** `zimri-2` at high confidence — commander of Elah's chariots (1KI 16:9) — "Jezebel alludes to Zimri who slew his master, the earlier usurper king."
- **BibleData:** `jehu-2` — anointed king over Israel (1KI 19:16) at the direction of Elisha (2KI 9:1-3)
- **verdict:** contested, referent `undecided`, confidence low

Jezebel's words can be read as naming the historical usurper Zimri or as addressing Jehu himself as a second Zimri, and the vocative reading is what BibleData has recorded by giving Jehu a Zimri name label.

**Fable 5.1** — contested, `undecided`, medium confidence, the same verdict. Verbatim: "The only Zimri who 'slew his master' is the usurper of 1 Kings 16 (zimri-2), but the word is either the subject of a rhetorical question about him (KJV) or a vocative epithet flung at Jehu (most modern readers); the evidence does not settle the syntax, and how a dataset links antonomasia is a scheme decision. If forced to one node, zimri-2 is the more defensible link, since 'Zimri' is never Jehu's name and BibleData's label on jehu-2 treats an epithet as a name."

### Case 76 · Judges 4:11 · H2246 · word 6596381

> Now Heber the Kenite, which was of the children of Hobab the father in law of Moses, had severed himself from the Kenites, and pitched his tent unto the plain of Zaanaim, which is by Kedesh.

- **the reading:** `hobab` at high confidence — the son of Reuel, Moses' father-in-law (NUM 10:29) — "Explicitly called father-in-law of Moses, same figure as in Numbers 10:29."
- **BibleData:** `reuel-2` — Moses' father-in-law (EXO 2:18, NUM 10:29)
- **verdict:** contested, referent `undecided`, confidence low

Judges 4:11 calls Hobab Moses' father-in-law while Numbers 10:29 calls Hobab the son of Reuel and Exodus 2:18 calls Reuel the father-in-law. The Hebrew chothen is read as father-in-law or as brother-in-law, and the question is old and unresolved.

**Fable 5.1** — contested, `undecided`, medium confidence, the same verdict. Verbatim: "The Hobab of Judg 4:11 is certainly the Hobab of Num 10:29, but whether that man is Moses' father-in-law (Jethro/Reuel, record reuel-2 — favored by חֹתֵן always meaning wife's father) or Reuel's son and Moses' brother-in-law (record hobab — favored by בֶּן־רְעוּאֵל in Num 10:29) is an unresolved crux; BibleData itself hedges by assigning Num 10:29 to both. Whatever is decided, Judg 4:11 and Num 10:29 must go to the same record."


## both wrong — 4

### Case 10 · Nehemiah 10:2 · H5838 · word 6855504

> Seraiah, Azariah, Jeremiah,

- **the reading:** `azariah-7` at medium confidence — son of Hilkiah (1CH 6:13), father of Seriah (1CH 6:14), and priest (EZR 2:2, NEH 7:7, NEH 12:1), explained the law to the people (NEH 8:7) — "Post-exilic priest sealing the covenant, fitting azariah-7's Ezra-Nehemiah era role."
- **BibleData:** `azariah-15` — chief priest of the house of Zadok (2CH 31:10)
- **verdict:** both wrong, referent `none`, confidence medium

azariah-15 is Hezekiah's chief priest and cannot seal Nehemiah's covenant; azariah-7 is older still. The Azariah of Nehemiah 10:2 is a post-exilic priest the encyclopedia does not hold.

**Fable 5.1** — both-wrong, `none`, high confidence, the same verdict. Verbatim: "The sealing priests of Neh 10:2 are post-exilic; azariah-15 is Hezekiah's chief priest and azariah-7 is the pre-exilic son of Hilkiah, both centuries too early, and seraiah-6 is excluded because Seraiah is listed beside him in the same verse. This is a distinct post-exilic priest (Strong's #9) with no record in the list."

### Case 11 · Nehemiah 12:33 · H5838 · word 6856923

> And Azariah, Ezra, and Meshullam,

- **the reading:** `azariah-7` at medium confidence — son of Hilkiah (1CH 6:13), father of Seriah (1CH 6:14), and priest (EZR 2:2, NEH 7:7, NEH 12:1), explained the law to the people (NEH 8:7) — "Priest in wall-dedication procession, likely the same restoration-era Azariah."
- **BibleData:** `seraiah-6` — returned to Judah after the Babylonian captivity (EZR 2:2, NEH 7:7) [called Azariah in NEH 7:7]
- **verdict:** both wrong, referent `undecided`, confidence medium

azariah-7 is pre-exilic and cannot march in Nehemiah's dedication. But seraiah-6 is the head who returned in 538, and BibleData's own graph makes his son Meraiah head of the house in Joiakim's day, so the father walking in 445 is implausible too. Whether the names in Nehemiah 12:33-34 are lay princes or priestly houses is what would decide it, and the text does not say.

*Revised after Fable.* I had called this model-wrong with seraiah-6 as the referent, accepting BibleData's Seraiah-called-Azariah identification. Fable's chronology against seraiah-6 is sound and I had not weighed it.

**Fable 5.1** — both-wrong, `undecided`, medium confidence, the same verdict. Verbatim: "azariah-7 is pre-exilic; seraiah-6 returned in 538 and BibleData's own record makes his son Meraiah head of the house in Nehemiah's day, so the father marching in 445 is implausible. Whether this Azariah is the lay wall-builder azariah-16 (he walks with the princes of Judah) or the Neh 10:2 priest (the names echo priestly houses) the text does not settle."

### Case 16 · 2 Chronicles 17:7 · H5662 · word 6881451

> Also in the third year of his reign he sent to his princes, even to Benhail, and to Obadiah, and to Zechariah, and to Nethaneel, and to Michaiah, to teach in the cities of Judah.

- **the reading:** `obadiah` at high confidence — feared the Lord (1KI 18:3), sent by Jehoshaphat to teach (2CH 17:7) — "Sent by Jehoshaphat to teach, matching this candidate's own distinguisher verse."
- **BibleData:** `obadiah-5` — son of Shemaiah (1CH 9:16)
- **verdict:** both wrong, referent `none`, confidence medium

The Obadiah of 2 Chronicles 17:7 is a prince of Judah sent to teach; obadiah is Ahab's steward in the northern kingdom and obadiah-5 a post-exilic Levite. Strong keeps the prince as a separate man and the encyclopedia does not hold him.

**Fable 5.1** — both-wrong, `none`, high confidence, the same verdict. Verbatim: "The Obadiah of 2 Chr 17:7 is one of Jehoshaphat's Judahite princes (c. 870 BCE), distinguished from the Levites of v. 8; obadiah-5 is a post-exilic Levite (1 Chr 9:16 / Neh 11:17) and obadiah is Ahab's steward in Israel (1 Kgs 18), whose distinguisher clause about 2 Chr 17:7 is an unsupported conflation. No BibleData record exists for this man (Strong's #10, prince of Judah under Jehoshaphat)."

### Case 74 · Nehemiah 10:14 · H1137 · word 6855562

> The chief of the people; Parosh, Pahathmoab, Elam, Zatthu, Bani,

- **the reading:** `bani-3` at high confidence — descendant of Perez (1CH 9:4) and leader during the return (NEH 10:14) — "Candidate distinguisher explicitly cites this verse as lay leader Bani."
- **BibleData:** `bani-6` — father of Rehum (NEH 3:17)
- **verdict:** both wrong, referent `bani-4`, confidence medium

Parosh, Pahath-moab, Elam, Zattu, Bani is the order of Ezra 2:3-10, so this is the lay house of Ezra 2:10, which BibleData holds as bani-4 — a record neither side named. bani-6 is a Levite line and cannot stand in the lay block; bani-3 is the pre-exilic Judahite of 1 Chronicles 9:4 whose own verse list disowns the distinguisher's claim to this verse.

*Revised after Fable.* Same verdict as before; Fable named the record that fits, which I had left as none.

**Fable 5.1** — both-wrong, `bani-4`, medium confidence, the same verdict. Verbatim: "Neh 10:14 lists 'heads of the people' under their Ezra 2 clan names (Parosh, Pahath-moab, Elam, Zattu, Bani, Azgad, Bebai...), so this Bani is the eponymous lay family of Ezra 2:10 = bani-4; bani-6 is a Levite lineage (Neh 3:17) and cannot sit in the lay block, and bani-3 is the pre-exilic Judahite of 1 Chr 9:4 whose own verse list disowns the distinguisher's claim to 10:14."


## What is left for the owner

Six occurrences are not settled by either of the two answers on record. Four of them have a
right answer that neither side gave and two have no right answer the evidence can reach, but all
six need a decision rather than another reading.

- **Nehemiah 10:2 — H5838, case 10.** azariah-15 is Hezekiah's chief priest and cannot seal Nehemiah's covenant; azariah-7 is older still. The Azariah of Nehemiah 10:2 is a post-exilic priest the encyclopedia does not hold.
- **Nehemiah 12:33 — H5838, case 11.** azariah-7 is pre-exilic and cannot march in Nehemiah's dedication. But seraiah-6 is the head who returned in 538, and BibleData's own graph makes his son Meraiah head of the house in Joiakim's day, so the father walking in 445 is implausible too. Whether the names in Nehemiah 12:33-34 are lay princes or priestly houses is what would decide it, and the text does not say.
- **2 Chronicles 17:7 — H5662, case 16.** The Obadiah of 2 Chronicles 17:7 is a prince of Judah sent to teach; obadiah is Ahab's steward in the northern kingdom and obadiah-5 a post-exilic Levite. Strong keeps the prince as a separate man and the encyclopedia does not hold him.
- **2 Kings 9:31 — H2174, case 27.** Jezebel's words can be read as naming the historical usurper Zimri or as addressing Jehu himself as a second Zimri, and the vocative reading is what BibleData has recorded by giving Jehu a Zimri name label.
- **Nehemiah 10:14 — H1137, case 74.** Parosh, Pahath-moab, Elam, Zattu, Bani is the order of Ezra 2:3-10, so this is the lay house of Ezra 2:10, which BibleData holds as bani-4 — a record neither side named. bani-6 is a Levite line and cannot stand in the lay block; bani-3 is the pre-exilic Judahite of 1 Chronicles 9:4 whose own verse list disowns the distinguisher's claim to this verse.
- **Judges 4:11 — H2246, case 76.** Judges 4:11 calls Hobab Moses' father-in-law while Numbers 10:29 calls Hobab the son of Reuel and Exodus 2:18 calls Reuel the father-in-law. The Hebrew chothen is read as father-in-law or as brother-in-law, and the question is old and unresolved.

Two more things are the owner's and are not questions about a verse.

**Thirteen occurrences are one man under two records, and merging records is destructive.** The pairs
are zadok-3 and zadok-6; mattaniah-2 and mattaniah-9; iezer and abiezer-2; abiasaph and ebiasaph;
anah and beeri; immer and immer-2; asaph-2 and asaph-3; ahitub-3 and ahitub-4. Each entry names the
record that should survive. Two of them carry a rider: zadok-6 also holds Nehemiah 13:13, which is
a third Zadok and needs a record of its own rather than being merged away, and mattaniah-2 also
holds 2 Chronicles 20:14, which is a Jehoshaphat-era Mattaniah and the same problem.

**The harness shows the model a distinguisher it should not trust.** Four of the seven wrong
readings come from a record whose prose names the disputed verse while its own verse list does not.
Deciding what the prompt shows is a change to the method and would invalidate the 99.00% figure for
a new run, so it is not made here.


## Fable's replies in full

One per Strong number, verbatim and unedited, in the order the batches were sent.

### H1137

**The verse.** Nehemiah 10:14 (MT 10:15) opens the third block of the covenant signatories: וְרָאשֵׁי הָעָם "and the heads of the people," i.e. the lay clan-heads, after the priests (10:2–8) and the Levites (10:9–13). The names that follow — Parosh, Pahath-moab, Elam, Zattu, **Bani**, Bunni, Azgad, Bebai, Adonijah, Bigvai, Adin, Ater, Hezekiah, Azzur, Hodiah, Hashum, Bezai … — are, with only minor variation, the lay family names of the return list in Ezra 2 / Nehemiah 7 (Parosh 2:3, Pahath-moab 2:6, Elam 2:7, Zattu 2:8, **Bani 2:10**, Azgad 2:12, Bebai 2:11, Adonikam 2:13, Bigvai 2:14, Adin 2:15, Ater 2:16, Hashum 2:19, Bezai 2:17 …). The signatories in this block are therefore signing under their ancestral clan names: the marked word בָּנִי is the eponym of the "sons of Bani" of Ezra 2:10 (Neh 7:15 Binnui), the family that also supplies the largest contingent of mixed-marriage offenders in Ezra 10:29, 34.

**BibleData's answer (bani-6).** This record is "father of Rehum (NEH 3:17)". But Neh 3:17 reads "after him repaired *the Levites*, Rehum son of Bani" — that Bani is a Levite lineage. The Levite Bani has already signed in the previous verse (10:13, where BibleData rightly has a Levite record). A Levite cannot be a "head of the people" in the lay block of 10:14; attaching the verse to bani-6 contradicts the structure of the list and the record's own Levite attestation. Source-wrong.

**The model's answer (bani-3).** The model followed bani-3's distinguisher, which does cite NEH 10:14 — but the record's verse list does not contain it, so the two halves of BibleData contradict each other here, and the model chose the prose. bani-3 is the Bani of 1 Chr 9:4, an ancestor (Uthai ← Ammihud ← Omri ← Imri ← Bani) in a Judahite Perez genealogy, several generations before the returnee Uthai. Could he be the same eponym as the clan Bani of Ezra 2:10? It is possible — both are lay Judahite lines — but nothing in the text connects them, and BibleData itself keeps them apart (bani-3 has no kinship edge to bani-4 and its own verse list disowns 10:14). If one wanted to defend the model one would have to first merge bani-3 into bani-4, which is a separate, unproven identification. I trust the verse list over the distinguisher for bani-3, because the verse list matches what the record actually documents (1 Chr 9:4 only) and the distinguisher's "leader during the return" is a guess at the identification rather than evidence.

**What fits.** The record that actually holds this clan is `bani-4`: "his sons returned to Judah after captivity (EZR 2:10)", with the Binnui label for Neh 7:15 and the Ezra 10:29, 34 descendants. Neh 10:14's Bani is the same family name in the same list of lay families. The only residual uncertainty is whether one prefers to treat each signatory as an unnamed contemporary head (which would make the referent "none"/a new record) rather than the eponymous ancestor; but BibleData's own treatment of the parallel names (Parosh, Zattu, etc. as family records spanning Ezra 2 and Neh 10) shows the eponym-record is the convention, so `bani-4` is the consistent answer.

---ANSWER---
[
  {
    "word_id": 6855562,
    "verdict": "both-wrong",
    "referent": "bani-4",
    "confidence": "medium",
    "reason": "Neh 10:14 lists 'heads of the people' under their Ezra 2 clan names (Parosh, Pahath-moab, Elam, Zattu, Bani, Azgad, Bebai...), so this Bani is the eponymous lay family of Ezra 2:10 = bani-4; bani-6 is a Levite lineage (Neh 3:17) and cannot sit in the lay block, and bani-3 is the pre-exilic Judahite of 1 Chr 9:4 whose own verse list disowns the distinguisher's claim to 10:14."
  }
]

### H1271

Both disputed words are the same phrase in the two parallel census lists (Ezra 2:61 ‖ Neh 7:63), and the question is the same in both: which Barzillai is meant by the *second* occurrence in the verse, the one marked <<בַּרְזִלַּי>> and immediately followed by הַגִּלְעָדִי.

**The Hebrew settles the referent of the marked word.** Each verse contains two Barzillais. The first, בְּנֵי בַרְזִלַּי, names the priestly clan; the relative clause then explains where that clan-name came from: the ancestor "took a wife from the daughters of **Barzillai the Gileadite** and was called by their name." The gentilic הַגִּלְעָדִי is attached to the marked word, and "Barzillai the Gileadite" is precisely the title of the man from Rogelim in 2 Samuel 17:27; 19:32; 1 Kings 2:7 — BibleData's own distinguisher for `barzillai` is "the Gileadite from Rogelim (2SA 17:27)". The narrative logic requires it too: a clan can only be "called by their name" if the name was borrowed from someone else, and the text identifies that someone as the famous Gileadite whose daughters the priest married into. So the marked word refers to `barzillai`, the Gileadite, not to the priest.

**What BibleData actually holds.** Its record `barzillai-3` is defined as "man who took a wife from the daughters of Barzillai and was called by their name" — that is the *priest*, i.e. the first Barzillai of the verse (the man whose adopted name the clan carries). That record is correct for word 6845691 / 6853724. But BibleData's verse list is verse-level: it lists Ezra 2:61 and Neh 7:63 under `barzillai-3` and does not list them under `barzillai`, so when the two occurrences are pulled apart by word, both come out as `barzillai-3`. The verse list therefore cannot distinguish the priest from his father-in-law; it is the verse-granularity trap described in the brief, and here it produces a genuinely wrong per-word answer rather than merely an ambiguous one. The distinguisher for `barzillai-3` does not contradict the verse list — it just describes a different man from the one the marked word names, which shows the record was never intended to cover the Gileadite in these verses. The omission of Ezra 2:61 / Neh 7:63 from the `barzillai` record's verse list is the actual gap.

**Is this duplicate-records?** No. `barzillai` (Gileadite) and `barzillai-3` (priest who married his daughter) are two distinct men — Strong's own note lists them separately (senses 1 and 2) — so neither should absorb the other. The right classification is that the source is wrong for this specific word: BibleData's verse-level assignment puts the marked Hebrew word under a record whose own distinguisher describes the other Barzillai in the verse, while the gentilic הַגִּלְעָדִי ties the word to the record BibleData calls "the Gileadite from Rogelim".

**The model.** Sonnet's answer `barzillai` with the reason "explicitly 'Barzillai the Gileadite', the father-in-law" is correct for both words, and Nehemiah's identical parallel confirms Ezra.

Confidence is high in both cases: the gentilic is in the word's own phrase, and the narrative sense (a clan named after the wife's father) is unambiguous.

---ANSWER---
[
  {
    "word_id": 6845696,
    "verdict": "source-wrong",
    "referent": "barzillai",
    "confidence": "high",
    "reason": "The marked word carries the gentilic הַגִּלְעָדִי, the title of the Barzillai of Rogelim (2Sa 17:27; 19:32; 1Ki 2:7), and is the father-in-law whose name the priestly clan adopted; BibleData's verse-level list assigns both Barzillais of the verse to barzillai-3, whose own distinguisher describes the priest (the verse's first occurrence), not the Gileadite."
  },
  {
    "word_id": 6853729,
    "verdict": "source-wrong",
    "referent": "barzillai",
    "confidence": "high",
    "reason": "Identical phrase to the Ezra 2:61 parallel: 'Barzillai the Gileadite' is the man from Rogelim (BibleData's barzillai), while barzillai-3 is by its own distinguisher the priest who married his daughter; the verse-level list cannot separate the two occurrences and misassigns this one."
  }
]

### H1587

The evidence here is unusually clean, and the trouble is entirely on BibleData's side.

**The two men.** The Masoretic text knows two Gemariahs. Jeremiah 29:3 names גְּמַרְיָה בֶּן־חִלְקִיָּה, Gemariah son of Hilkiah, whom Zedekiah sent to Babylon with Elasah son of Shaphan as a courier for Jeremiah's letter to the exiles. Jeremiah 36 names גְּמַרְיָהוּ בֶן־שָׁפָן, Gemariah son of Shaphan, a prince (שַׂר) with a chamber in the upper court of the temple, father of Micaiah, who sat among the princes in the scribe's chamber and pleaded with Jehoiakim not to burn the scroll. Different fathers, different offices, different episodes a decade or so apart; there is no reading on which these collapse into one person. Strong's own lexicon entry says "the name of two Israelites" and lists exactly these two.

**BibleData's own records agree.** `gemariah` is distinguished "son of Hilkiah (JER 29:3)" and carries a kinship edge son → hilkiah-3. `gemariah-2` is distinguished "son of Shaphan the scribe (JER 36:10)" and carries son → shaphan and father → micaiah-6. The encyclopedia has therefore correctly modelled two men and even cited Jeremiah 36:10 as the anchor verse for the second. This is not a duplicate-records situation: the two records are for two genuinely different men, and both should exist.

**Where the verse list went wrong.** Every Jeremiah 36 verse — 36:10, 36:11, 36:12, 36:25 — has been filed under `gemariah`, the son of Hilkiah, while `gemariah-2` has been left with zero attestations. This is precisely the distinguisher-versus-verse-list contradiction the brief warns about, and here it is easy to say which half to trust: the distinguisher and the kinship edges of `gemariah-2` explicitly name Shaphan as father and Jeremiah 36:10 as the anchor, matching the Hebrew of 36:10–12 word for word (בֶן שָׁפָן in both 36:11 and 36:12). The verse list contradicts BibleData's own prose and its own genealogy. The most likely mechanism is mechanical: whoever populated verse lists keyed every H1587 occurrence to the first record with that Strong number.

**The individual words.**

*Jeremiah 36:11, word 6721263.* מִכָיְהוּ בֶן גְּמַרְיָהוּ בֶן שָׁפָן. The Hebrew is unambiguous: this Gemariah is the son of Shaphan and the father of Micaiah — exactly the two kinship edges BibleData records for `gemariah-2`. There is only one occurrence of the name in the verse, so the verse-level list cannot hide behind a same-verse ambiguity. Sonnet is right; the verse list is wrong.

*Jeremiah 36:12, word 6721302.* וּגְמַרְיָהוּ בֶן שָׁפָן in the roll of princes seated in the scribe's chamber. Again patronym given in the text, again Shaphan, again a single occurrence. Sonnet is right.

*Jeremiah 36:25, word 6721621.* אֶלְנָתָן וּדְלָיָהוּ וּגְמַרְיָהוּ — the three named without patronyms. But the three are the same Elnathan (son of Achbor), Delaiah (son of Shemaiah), and Gemariah (son of Shaphan) listed in 36:12 as the princes present when Micaiah reported; the narrative of 36:20–25 has these same princes now before the king. No Gemariah son of Hilkiah appears anywhere in chapter 36, and a courier to Babylon under Zedekiah is not a prince in Jehoiakim's court. The identification with the son of Shaphan is as secure as any patronym-less mention can be. Sonnet is right.

**Verdict.** All three are `source-wrong`: BibleData's verse list assigns the Jeremiah 36 occurrences to `gemariah` (son of Hilkiah), contradicting its own distinguisher and kinship edges for `gemariah-2`, which name Shaphan as father and 36:10 as the anchor. The correct fix is to move Jeremiah 36:10, 36:11, 36:12, and 36:25 from `gemariah` to `gemariah-2`, leaving `gemariah` with only Jeremiah 29:3. (Note that 36:10 has no BHSA H1587 word listed in the occurrence table — presumably a lemma-tagging gap — but it too belongs to `gemariah-2` by BibleData's own anchor citation.)

---ANSWER---
[
  {
    "word_id": 6721263,
    "verdict": "source-wrong",
    "referent": "gemariah-2",
    "confidence": "high",
    "reason": "The Hebrew reads מִכָיְהוּ בֶן גְּמַרְיָהוּ בֶן שָׁפָן — son of Shaphan and father of Micaiah — exactly the kinship edges BibleData records for gemariah-2; its verse list filed the verse under the son of Hilkiah, contradicting its own distinguisher and genealogy."
  },
  {
    "word_id": 6721302,
    "verdict": "source-wrong",
    "referent": "gemariah-2",
    "confidence": "high",
    "reason": "The text gives the patronym explicitly, וּגְמַרְיָהוּ בֶן שָׁפָן, among Jehoiakim's princes; only gemariah-2 is son of Shaphan, and BibleData's verse list contradicts its own record distinguisher."
  },
  {
    "word_id": 6721621,
    "verdict": "source-wrong",
    "referent": "gemariah-2",
    "confidence": "high",
    "reason": "Elnathan, Delaiah, and Gemariah in 36:25 are the same three princes named with patronyms in 36:12 (Achbor, Shemaiah, Shaphan), continuing the same scene before the king; no son of Hilkiah appears in Jeremiah 36."
  }
]

### H1787

#### Reasoning

**The text.** 1 Chronicles 1:41 reads בְּנֵי עֲנָה דִּישׁוֹן וּבְנֵי דִישׁוֹן חַמְרָן וְאֶשְׁבָּן וְיִתְרָן וּכְרָן — "The sons of Anah: Dishon. And the sons of Dishon: Hamran, Eshban, Ithran, Cheran." This is a verbatim condensation of Genesis 36:25–26: וְאֵלֶּה בְנֵי־עֲנָה דִּשֹׁן ... וְאֵלֶּה בְּנֵי דִישָׁן חֶמְדָּן וְאֶשְׁבָּן וְיִתְרָן וּכְרָן. The only variation is Hemdan → Hamran (a ד/ר confusion; KJV's "Amram" in Chronicles is a further corruption in the English tradition), and the Genesis spelling דִישָׁן at 36:26 for the same man that Chronicles gives as דִישׁוֹן. The four sons are identical.

**The two Dishons.** Genesis 36:20–21 lists Dishon as the fifth son of Seir (brother of Anah); Genesis 36:25 gives a second Dishon who is Anah's son, i.e. the first Dishon's nephew. BibleData holds these correctly as two records: `dishon` (son of Seir, chief in Edom; Gen 36:21, 30; 1 Chr 1:38) and `dishon-2` (son of Anah; Gen 36:25–26; father of Hemdan, Eshban, Ithran, Cheran). The records are genuinely distinct men, so this is not a duplicate-records case.

**The verse in Chronicles.** 1 Chr 1:38 is where the Chronicler reproduces the Seir list (Lotan, Shobal, Zibeon, Anah, Dishon, Ezer, Dishan) — that is the son of Seir, and BibleData rightly puts 1:38 under `dishon`. Verses 1:39–42 then run through the sons of each of Seir's sons in turn: Lotan (39), Shobal and Zibeon (40), Anah and *his* Dishon (41), Ezer and Dishan (42). Both words in 1:41 belong to the Anah paragraph. The first (6858440) is the predicate of "sons of Anah" — the nephew. The second (6858443) is the head of the list Hamran/Eshban/Ithran/Cheran — which is `dishon-2`'s recorded kinship out: father → hemdan, eshban, ithran, cheran. Both words point to the same man, the son of Anah.

**The two-occurrences-in-one-verse trap.** Ordinarily a verse with two occurrences could split between records, but here both occurrences are the same person, so the verse-level verdict is not ambiguous: 1 Chr 1:41 should be attested under `dishon-2`, and its presence under `dishon` is wrong for both words. BibleData's own distinguisher for `dishon-2` ("son of Anah") and its own kinship edges (the four sons) contradict its verse-list placement of 1:41 under `dishon`; I trust the distinguisher and kinship edges here, because they match Genesis 36:25–26 and Chronicles copies that passage.

**Could `dishon` (son of Seir) be defended?** Only by the critical conjecture that "sons of Anah: Dishon" is a textual slip and that Anah and Dishon were originally brothers only — but that is a source-critical hypothesis about the Genesis Vorlage, not a reading of the Masoretic text, and it would equally overturn Gen 36:25, which BibleData already assigns to `dishon-2`. Nothing in the MT supports it. (Incidentally, the undisputed word 6858458 in 1:42, "sons of דִישׁוֹן: Uz and Aran," is the Chronicler's spelling for Genesis's Dishan son of Seir — a third man again — and BibleData has nothing there; not part of this adjudication.)

The model's answer, `dishon-2`, is right for both words; BibleData's assignment is contradicted by its own record structure.

---ANSWER---
[
  {
    "word_id": 6858440,
    "verdict": "source-wrong",
    "referent": "dishon-2",
    "confidence": "high",
    "reason": "\"The sons of Anah: Dishon\" in 1 Chr 1:41 reproduces Gen 36:25, which BibleData itself assigns to dishon-2 (distinguisher 'son of Anah'); the son of Seir appears in the Chronicler's list at 1:38, not 1:41."
  },
  {
    "word_id": 6858443,
    "verdict": "source-wrong",
    "referent": "dishon-2",
    "confidence": "high",
    "reason": "The Dishon whose sons are Hamran/Hemdan, Eshban, Ithran, Cheran is exactly dishon-2 per BibleData's own kinship edges and Gen 36:26; both occurrences in the verse are the same son of Anah, so the verse-level placement under dishon is wrong for both."
  }
]

### H1835

Judges 18:29 contains the name twice. The first, וַיִּקְרְאוּ שֵׁם הָעִיר דָּן ("they called the name of the city Dan"), is the town, formerly Laish. The second — the marked word 6605787 — sits in the phrase בְּשֵׁם דָּן אֲבִיהֶם אֲשֶׁר יוּלַּד לְיִשְׂרָאֵל, "after the name of Dan their father, who was born to Israel." The appositive אֲבִיהֶם and the relative clause "who was born to Israel" leave no room for doubt: this is the patriarch, the son of Jacob (Genesis 30:6), the eponym after whom the city was named. It cannot be the city, since a city is not "their father" and was not "born to Israel."

BibleData's verse list assigns Judges 18:29 to `dan-2` only, which is correct for the first occurrence (word 6605784) but wrong for the second. This is the verse-level granularity trap: the verse list has one answer for a verse that carries two referents. The verse list is not contradicted by any distinguisher here (dan-2 has none), but it is contradicted by the plain syntax of the verse. BibleData's own record `dan` — "fifth-born son of Jacob," father of the tribe — is exactly the person named. The reading model's answer `dan` is right, and the two records are genuinely distinct entities (person versus place), so this is not a duplicate-records case.

Verdict: source-wrong for this word; the correct referent is `dan`. Ideally BibleData's verse list for Judges 18:29 should carry both `dan-2` (first occurrence) and `dan` (second occurrence), the way it already does for Joshua 19:47.

---ANSWER---
[
  {
    "word_id": 6605787,
    "verdict": "source-wrong",
    "referent": "dan",
    "confidence": "high",
    "reason": "The marked word is 'Dan their father, who was born to Israel' — the patriarch, son of Jacob, not the city. BibleData's verse-level list only captured the first occurrence in the verse (the city, dan-2) and cannot distinguish the second."
  }
]

### H1867

**Reasoning**

The Hebrew of Daniel 11:1 is unambiguous about which Darius is meant: לְדָרְיָוֶשׁ הַמָּדִי, "Darius the Mede." The gentilic is in the text itself, not inferred. That is the same man Daniel 9:1 introduces as דָּרְיָוֶשׁ בֶּן־אֲחַשְׁוֵרוֹשׁ מִזֶּרַע מָדָי, "Darius son of Ahasuerus, of the seed of the Medes, who was made king over the realm of the Chaldeans," and the same man Daniel 5:31 says "received the kingdom" at Belshazzar's death and who reigns through chapter 6. Within the book of Daniel there is only one Darius, and every marker attached to him (Mede, son of Ahasuerus, king over the Chaldeans, ~62 years old, followed by Cyrus in 6:28) points to a single figure. The "first year of Darius" in 11:1 is the same regnal year-one that 9:1 dates the prayer to; the angel is saying he stood by the Mede's side at the same moment Daniel was praying. There is no reading on which 9:1 and 11:1 name different men.

Now look at BibleData's two records. `darius-2` is distinguished as "son of Ahaseurus, a Mede (DAN 9:1)" with a kinship edge to Ahasuerus — that is precisely Darius the Mede. `darius` is distinguished as "King of Persia (EZR 4:5)" — that is Darius I Hystaspes, under whom the temple was finished (Ezra 6:15, Haggai, Zechariah), a man who came to the throne in 522 BC, decades after Belshazzar's fall, and who was Persian, not Median. BibleData's verse list for `darius` nevertheless swallows Daniel 5:31, 6:1–28, and 11:1 (and also Nehemiah 12:22's "Darius the Persian," who is a third king, Darius II or III). So the `darius` record's verse list contradicts its own distinguisher: a record defined as the Persian king of Ezra 4:5 cannot hold a verse whose Hebrew calls the man "the Mede." I trust the distinguisher over the verse list here, because the distinguisher is a deliberate identification while the verse list is evidently a name-string sweep that ignored the gentilic.

Is this duplicate-records? No. Duplicate-records requires that both answers be right about the man and the encyclopedia simply hold him twice. But `darius` as defined is Darius Hystaspes, and Darius Hystaspes is not Darius the Mede on any account — biblical, historical, or even the harmonizing views that equate Darius the Mede with Gubaru/Ugbaru or with Cyrus himself. The `darius` record isn't a second copy of the Mede; it's a different king whose verse list has been polluted. That makes this source-wrong, with the model's `darius-2` the correct referent. The model's stated reason (same "first year of Darius the Mede" framing as 9:1) is exactly the right evidence, and its medium confidence was too cautious — the gentilic settles it.

One further note for whoever cleans this up: the same correction applies to Daniel 5:31, 6:1, 6:6, 6:9, 6:25, 6:28, all of which belong under `darius-2`, and Nehemiah 12:22 ("Darius the Persian," reign spanning Jaddua's high priesthood) should probably be split off into its own record rather than sit under Darius I. Those verses are not in dispute here, but the `darius` record is a three-king conflation, and 11:1 is only the most glaring symptom.

---ANSWER---
[
  {
    "word_id": 6843688,
    "verdict": "source-wrong",
    "referent": "darius-2",
    "confidence": "high",
    "reason": "The Hebrew reads דָרְיָוֶשׁ הַמָּדִי, 'Darius the Mede,' the same man Dan 9:1 calls son of Ahasuerus of Median seed, whom BibleData itself holds as darius-2; the darius record is distinguished as the Persian king of Ezra 4:5 (Darius Hystaspes), so its verse list contradicts its own distinguisher by including this verse."
  }
]

### H2148

Both disputed words concern one man, and the evidence is unusually tight.

**The textual situation.** 1 Chronicles 26:1–2 opens the gatekeeper divisions with "Of the Korahites, Meshelemiah son of Kore, of the sons of Asaph [= Ebiasaph]. And Meshelemiah had sons: Zechariah the firstborn…" Verse 9 counts Meshelemiah's sons and brothers at eighteen; verse 14 then assigns the east gate by lot "to Shelemiah, and to Zechariah his son, a counsellor with insight … his lot came out northward." Shelemiah in v. 14 is the shortened form of Meshelemiah in vv. 1, 2, 9 — the same Korahite, the same list, the same chapter; nobody has ever seriously read them as two men. And 1 Chronicles 9:21 independently says "Zechariah son of Meshelemiah was gatekeeper at the entrance of the tent of meeting." So 9:21, 26:2 and 26:14 are three attestations of one person: Zechariah, firstborn of Meshelemiah/Shelemiah, Korahite gatekeeper of the north gate (Strong's no. 3).

**What BibleData holds.** Record `zechariah-4` is exactly this man: its distinguisher reads "son of Meshelemiah [Shallum] (1CH 9:21), gatekeeper (1CH 26:14)". Its own prose therefore claims 26:14 — yet its verse list contains only 9:21. Meanwhile the verse list files both 26:2 and 26:14 under `zechariah-5`, whose distinguisher is "second rank Levite, gatekeeper (1CH 15:18), a trumpeter (1CH 15:24)" and whose core verses are 15:18, 15:20, 15:24, 16:5 — the Levite musician who played psalteries "on Alamoth" and stood second to Asaph before the ark. That record says nothing about Meshelemiah.

This is the first trap in its clearest form: distinguisher and verse list contradict each other, and here I trust the distinguisher of `zechariah-4`, because the Hebrew text itself — the ben Meshelemiah/Shelemiah filiation stated in 9:21, 26:2 and 26:14 — decides the matter independently of anything BibleData says.

**Is this duplication rather than error?** Only if `zechariah-5` as a whole were the same man as `zechariah-4`. The text gives no warrant for that. The 15:18/16:5 Zechariah is a musician of the second rank in David's ark procession; the sons of Meshelemiah are gatekeepers, listed in a different chapter under a different office, and 15:18 names its porters explicitly (Obed-edom and Jeiel), not Zechariah. Strong's likewise separates them (nos. 3 and 5). Some commentators have speculated the two might coincide, but that is conjecture; `zechariah-5` has an independent identity and should keep its 15:18–16:5 verses. What has happened is simply that two verses belonging to the Korahite gatekeeper were misfiled under the musician's record — BibleData assigning verses to a record its own distinguisher for the other record contradicts. That is *source-wrong*, not duplicate-records.

The reading model's `zechariah-4` is correct for both words, and its reasoning (same family as 9:21; Shelemiah = Meshelemiah) is the right reasoning. The corrective action is to move 26:2 and 26:14 to `zechariah-4`'s verse list, matching its distinguisher.

---ANSWER---
[
  {
    "word_id": 6871069,
    "verdict": "source-wrong",
    "referent": "zechariah-4",
    "confidence": "high",
    "reason": "The word is 'Zechariah the firstborn' of Meshelemiah the Korahite (26:1-2), the same man 9:21 calls 'Zechariah son of Meshelemiah, gatekeeper' — BibleData's own zechariah-4 record; the verse list misfiles it under zechariah-5, the 15:18/16:5 musician, whom the text never links to Meshelemiah."
  },
  {
    "word_id": 6871262,
    "verdict": "source-wrong",
    "referent": "zechariah-4",
    "confidence": "high",
    "reason": "'Zechariah his son' after Shelemiah (= Meshelemiah of 26:1, 2, 9) is the firstborn gatekeeper of 26:2 and 9:21; zechariah-4's own distinguisher cites 1CH 26:14 while its verse list omits it, and I trust the distinguisher because the filiation in the Hebrew settles it."
  }
]

### H2174

**The verse.** 2 Kings 9:31: וַתֹּאמֶר הֲשָׁלוֹם זִמְרִי הֹרֵג אֲדֹנָיו. Jezebel, at the window, greets Jehu as he rides through the gate. The echo of the chapter is deliberate: הֲשָׁלוֹם is the very question the messengers and Joram put to Jehu in 9:17–22, and Jehu's reply there ("what peace, so long as the harlotries of Jezebel…") is what Jezebel is now throwing back at him.

**Who the historical Zimri is** is not in doubt. The only man of this name who "slew his master" is the chariot commander of 1 Kings 16:9–20, who murdered king Elah, reigned seven days, and died in the burning palace at Tirzah — BibleData's `zimri-2`. Zimri son of Salu (`zimri`), the Judahite Zimri of 1 Chronicles 2:6 (held under `zabdi`), and the Benjaminite Zimri son of Jehoaddah (`zimri-3`) are all irrelevant; the epithet הֹרֵג אֲדֹנָיו identifies the allusion unambiguously.

**What is actually disputed** is not which Zimri, but what the word is doing in the sentence, and the two answers encode two legitimate syntactic readings:

1. *Referential reading (KJV):* "Had Zimri peace, who slew his master?" — זִמְרִי is the subject of the nominal question, הֹרֵג אֲדֹנָיו an attributive participle. Jezebel asks a rhetorical question about the historical usurper: he lasted seven days, and so will you. On this reading the name denotes the man of 1 Kings 16, and the model's `zimri-2` is simply correct.

2. *Vocative reading (most modern commentators and translations — NRSV, NJPS, Cogan–Tadmor, Gray):* "Is it well, Zimri, murderer of his master?" — זִמְרִי is a vocative, הֹרֵג אֲדֹנָיו an appositional epithet in the third person, which is ordinary Hebrew for vocative apposition. Jezebel addresses Jehu by the regicide's name as a taunt (antonomasia). On this reading the person addressed is Jehu, which is what BibleData is trying to capture by attaching "Zimri" as a name label to `jehu-2`.

Both readings are grammatically available; the third-person suffix on אֲדֹנָיו does not decide it (it fits an attributive participle or a vocative appositive equally well), and הֲשָׁלוֹם as a stand-alone greeting slightly favours the vocative, while the KJV's construal is not ungrammatical. This is a genuine pragmatic ambiguity, and it is the kind that a verse-level entity gazetteer cannot resolve because the dispute is about *how* the name is used, not *whom* it names.

**How to weigh the two answers.** I do not think either is simply wrong. But I will register a lean, because the two answers are not symmetric in what they claim about the *name*. Even on the vocative reading, the lexical content of זִמְרִי is the usurper of Tirsah: the taunt works only because the hearer resolves the name to `zimri-2` and then transfers the predicate to Jehu. "Zimri" is never Jehu's name; it is an epithet borrowed from another man for one line. BibleData's encoding — "Zimri" as an alternative name label of Jehu with Strong H2174 — is a category error of the same kind as listing "Judas" as an alternative name of anyone called a Judas. So if a linker must choose one node for the token, `zimri-2` is the more defensible link (the name's referent), with the addressee (`jehu-2`) recorded as a relation of the utterance rather than as the name's referent. The model's answer is therefore the better of the two, and its confidence ("high") is overstated only in the sense that it did not acknowledge the vocative reading.

Because the evidence does not settle the syntactic question, and because reasonable schemes will legitimately encode antonomasia either way, I classify this as **contested**, with the undecided question stated precisely: is זִמְרִי the subject of a rhetorical question about the historical Zimri, or a vocative epithet applied to Jehu — and, if the latter, does the dataset link vocative epithets to the name's lexical referent or to the addressee? Neither of BibleData's traps applies here: the `jehu-2` distinguisher and verse list agree with each other, and there is one occurrence in the verse.

---ANSWER---
[
  {
    "word_id": 6667754,
    "verdict": "contested",
    "referent": "undecided",
    "confidence": "medium",
    "reason": "The only Zimri who 'slew his master' is the usurper of 1 Kings 16 (zimri-2), but the word is either the subject of a rhetorical question about him (KJV) or a vocative epithet flung at Jehu (most modern readers); the evidence does not settle the syntax, and how a dataset links antonomasia is a scheme decision. If forced to one node, zimri-2 is the more defensible link, since 'Zimri' is never Jehu's name and BibleData's label on jehu-2 treats an epithet as a name."
  }
]

### H2246

#### Reasoning

**What the marked word says.** Judges 4:11: וְחֶבֶר הַקֵּינִי נִפְרָד מִקַּיִן מִבְּנֵי **חֹבָב** חֹתֵן מֹשֶׁה — "Heber the Kenite had separated from Kain, from the sons of Hobab, *ḥōtēn* of Moses." Here חֹתֵן is unambiguously in apposition to Hobab; there is no other noun it could attach to. The Hobab of this verse is beyond reasonable doubt the same individual as the Hobab of Numbers 10:29 (same name, same epithet חֹתֵן מֹשֶׁה, same Kenite/Midianite clan whose descendants settle near Kedesh, cf. Judg 1:16 "the sons of the Kenite, Moses' ḥōtēn"). So whichever record is right for the marked word must also be right for Numbers 10:29 word 6543417 — and BibleData's own verse list already gives Numbers 10:29 to *both* `hobab` and `reuel-2`, which tells us the encyclopedia never decided the underlying question.

**The underlying question.** Is Hobab (a) Moses' father-in-law, i.e. the same man Exodus calls Jethro/Reuel, or (b) the *son* of Reuel and thus Moses' brother-in-law?

- For (b): Numbers 10:29 reads חֹבָב בֶּן־רְעוּאֵל הַמִּדְיָנִי חֹתֵן מֹשֶׁה. If Reuel is the father-in-law (Exod 2:18, where Reuel is the girls' father), then Hobab is his son, and חֹתֵן in Judg 4:11 / 1:16 must be read loosely as "relative by marriage." This is the reading of Strong's ("son of Reuel… brother-in-law of Moses"), of the KJV margin tradition, and of the `hobab` record's distinguisher.
- For (a): חֹתֵן occurs some twenty times, all of Jethro in Exodus 3–4, 18, and always means wife's father; a sense "brother-in-law" is not otherwise attested (the Hebrew for that is a different word). The phrase חֹתֵן מֹשֶׁה in Num 10:29 can grammatically modify the head noun Hobab rather than the nearer Reuel, and Judg 4:11 makes exactly that reading explicit. On this view Hobab = Jethro (a Midianite name and a title/second name), and Reuel in Exod 2:18 is grandfather or clan-eponym — a harmonization at least as old as the rabbis and defended by many moderns. That is what `reuel-2` ("Moses' father-in-law," with Hobab=H2246 among its labels and Judg 4:11 attested "as Hobab") represents.

**Assessing the two answers.** Sonnet's justification — "same figure as in Numbers 10:29" — is true but does not decide anything, because Numbers 10:29 is precisely where BibleData hedges between both records. It picked `hobab` by name-matching, not by resolving the crux. BibleData's `reuel-2` is the philologically stronger reading of חֹתֵן, but the record it points to is internally incoherent: it is simultaneously "father -> hobab" and carries Hobab as one of its own names, so it cannot both be Hobab's father and be Hobab. The distinguisher and the kinship edges contradict the name labels; I trust neither half over the other here, because the incoherence is not a data-entry slip but a faithful reflection of an unresolved source-critical tangle (Reuel in Exod 2, Jethro in Exod 3–18, Hobab in Num 10 and Judg 4).

**Is this duplicate-records?** No. `hobab` and `reuel-2` are defined as father and son, two distinct men; the question is which of the two the name Hobab denotes, not whether the encyclopedia has entered one man twice.

**Verdict.** The Hebrew evidence genuinely does not settle whether Hobab is Jethro or Jethro's son; scholars have split on it for two millennia, and the lexical weight of חֹתֵן (favoring `reuel-2`) is balanced by the explicit בֶּן־רְעוּאֵל of Num 10:29 (favoring `hobab`). I classify this as **contested**. The one thing I would insist on regardless of how the question is eventually resolved: Judges 4:11 and Numbers 10:29 name the same person and must be assigned to the same record(s); BibleData's giving Num 10:29 to both but Judg 4:11 to only `reuel-2` is an internal inconsistency that should be repaired in whichever direction the editors choose.

---ANSWER---
[
  {
    "word_id": 6596381,
    "verdict": "contested",
    "referent": "undecided",
    "confidence": "medium",
    "reason": "The Hobab of Judg 4:11 is certainly the Hobab of Num 10:29, but whether that man is Moses' father-in-law (Jethro/Reuel, record reuel-2 — favored by חֹתֵן always meaning wife's father) or Reuel's son and Moses' brother-in-law (record hobab — favored by בֶּן־רְעוּאֵל in Num 10:29) is an unresolved crux; BibleData itself hedges by assigning Num 10:29 to both. Whatever is decided, Judg 4:11 and Num 10:29 must go to the same record."
  }
]

### H2766

**The case.** Ezra 10 is organized by class. Verses 18–22 list the priests who had married foreign wives — sons of Jeshua, Immer, Harim (v. 21), Pashhur. Verses 23–24 list Levites, singers, gatekeepers. Verse 25 then opens a new section with וּמִיִּשְׂרָאֵל, "and of Israel," i.e. the laity, and runs through the lay clans in the same order as the Ezra 2 census: Parosh, Elam, Zattu, Bebai, Bani, Pahath-moab, **Harim (v. 31)**, Hashum, Bani, Binnui, Nebo. So the chapter contains two Harims, deliberately separated: the priestly Harim of Ezra 2:39 / Neh 7:42 (1,017 men, the third priestly course of 1 Chr 24:8) at 10:21, and the lay Harim of Ezra 2:32 / Neh 7:35 (320 men) at 10:31. The marked word in 10:31 sits inside the lay section, so it is the lay Harim.

**BibleData's own evidence settles it against its verse list.** Record `harim-2` (the Ezra 2:32 lay clan) carries kinship edges to `eliezer-8`, `isshijah`, `malchijah-6`, `shemaiah-16`, `shimeon`, `benjamin-3`, `malluch-3`, and `shemariah-3` — which is exactly the roster of Ezra 10:31–32 (Eliezer, Ishijah, Malchiah, Shemaiah, Shimeon, Benjamin, Malluch, Shemariah). BibleData therefore *knows* these men are sons of the lay Harim; it built the genealogy that way. Yet its verse list files Ezra 10:31 under `harim-3`, the priestly clan, whose only kinship edge is descent from Aaron and whose other verses (Ezra 2:39, 10:21, Neh 7:42, 10:5, 12:15) are all priestly contexts. That is the first trap in the brief — distinguisher/kinship and verse list contradicting each other — and here I trust the kinship edges plus the text's structure over the verse list. The verse list almost certainly just swept both Ezra 10 Harims into the same record without noticing the "of Israel" boundary at v. 25.

Cross-check: BibleData gets the analogous Nehemiah pair right — Neh 10:5 (priestly signatories) → `harim-3`, Neh 10:27 (lay signatories) → `harim-2`. Ezra 10:21 → `harim-3` and 10:31 → `harim-2` is the same split and should have been applied consistently.

The reading model's answer of `harim-2` is correct on both runs, and its reasoning (lay "of Israel" section, same clan as Ezra 2:32) is the right reasoning. The correct record exists in the list, so this is not both-wrong and not duplicate-records; it is BibleData's verse list assigning a verse to a record its own kinship data contradicts.

(Aside, not in dispute: `harim-4`, "father of Malchijah" at Neh 3:11, is very plausibly the same lay clan as `harim-2`, since a Malchiah appears among the sons of Harim in this very verse; that is a possible duplicate pair for a later pass.)

Both disputed entries are the same word (6850099), so the verdict is identical for each.

---ANSWER---
[
  {
    "word_id": 6850099,
    "verdict": "source-wrong",
    "referent": "harim-2",
    "confidence": "high",
    "reason": "Ezra 10:31 falls in the lay 'of Israel' section (vv. 25ff) in Ezra 2 census order, distinct from the priestly Harim at 10:21; BibleData's own kinship edges for harim-2 already name Eliezer, Ishijah, Malchiah, Shemaiah, Shimeon, Benjamin, Malluch and Shemariah as his descendants, contradicting its verse list's harim-3."
  },
  {
    "word_id": 6850099,
    "verdict": "source-wrong",
    "referent": "harim-2",
    "confidence": "high",
    "reason": "Same word as above: the lay Harim clan of Ezra 2:32, confirmed by the chapter's priest/laity structure and by BibleData's harim-2 kinship record listing exactly the sons of Ezra 10:31-32; the verse list's harim-3 is the priestly clan of 10:21."
  }
]

### H281

The disputed word is the single occurrence of אֲחִיָּה in 1 Kings 15:33: "Baasha the son of Ahijah" (בַּעְשָׁא בֶן־אֲחִיָּה). The construction is unambiguous — this is a patronymic, and Baasha's father is named identically at 1 Kings 15:27 ("Baasha the son of Ahijah, of the house of Issachar"), 1 Kings 21:22 ("the house of Baasha the son of Ahijah"), and 2 Kings 9:9. BibleData itself assigns all three of those verses to `ahijah-3`, whose distinguisher is exactly "of the house of Issachar, father of Baasha (1KI 15:27)" and whose kinship edges record `father -> baasha`.

The record BibleData's verse list gives for 15:33, `ahijah`, is the Shilonite prophet (and, by BibleData's conflation, the Shiloh priest of 1 Sam 14). Nothing in 1 Kings 15:33 concerns the prophet; the prophecy against Baasha's house is by Jehu son of Hanani (1 Kings 16:1–7), and the last mention of the Shilonite in the chapter is 15:29, where Baasha fulfils Ahijah's word against Jeroboam's house. The verse-list entry for 15:33 under `ahijah` is almost certainly an indexing slip caused by that nearby prophet reference — a case where the verse list contradicts the record's own distinguisher and the parallel patronymics that BibleData has elsewhere indexed correctly. I trust the distinguisher and kinship graph here, since they agree with the text at three other verses, over the lone verse-list entry.

There is no duplicate-record issue: `ahijah` and `ahijah-3` are distinct men (a prophet of Shiloh in Ephraim versus an Issacharite whose son seized the throne), and neither the text nor BibleData's own edges link them. The reading model's answer is correct.

---ANSWER---
[
  {
    "word_id": 6655793,
    "verdict": "source-wrong",
    "referent": "ahijah-3",
    "confidence": "high",
    "reason": "The word is the patronymic in 'Baasha the son of Ahijah', identical to 1 Kings 15:27, 21:22 and 2 Kings 9:9, which BibleData itself assigns to ahijah-3 (father of Baasha, kinship father->baasha). The verse-list entry under the Shilonite prophet contradicts BibleData's own distinguisher and edges and is an indexing slip."
  }
]

### H2811

#### Reasoning

**Neh 10:11 (MT 10:12), word 6855548.** The verse sits inside the list of Levites who sealed the covenant (10:9–13): Jeshua b. Azaniah, Binnui, Kadmiel, then "their brethren" Shebaniah, Hodijah, Kelita, Pelaiah, Hanan, Micha, Rehob, **Hashabiah**, Zaccur, **Sherebiah**, Shebaniah, Hodijah, Bani, Beninu. This is a roster of Levites alive in Nehemiah's day (c. 444 BC).

The model answered `hashabiah`, and its stated reason — "candidate's own citation names this verse" — shows exactly which trap it fell into. The record `hashabiah` carries the prose distinguisher "son of Amaziah (1CH 6:45), Levite during the return (NEH 10:11, NEH 12:24)", but its verse list is 1 Chr 6:45 and 1 Chr 9:14 only, and its kinship edges are son → amaziah-3, father → malluch — i.e. precisely the Hashabiah of 1 Chr 6:45, a link in the Merarite genealogy running from Merari down to Ethan, the singer appointed by David. That man lived some five centuries before the covenant of Nehemiah 10. Here the verse list and kinship edges are trustworthy and the distinguisher is not: the "(NEH 10:11, NEH 12:24)" tail is stray text that the record's own data does not support. The model read the prose and skipped the genealogy. Model-wrong.

BibleData's `hashabiah-4` (Ezra 8:19, 24) is the Merarite Levite whom Ezra summoned from Casiphia together with Sherebiah and whom he then entrusted with the temple silver (again with Sherebiah). Ezra's arrival (458 BC) is only about fourteen years before Nehemiah's covenant, and the Hashabiah–Sherebiah pairing recurs in the sealing list (10:11–12) and again in 12:24. That is the standard identification (Strong's own item 6 bundles Ezra 8, Neh 3:17, 10:11 and 12:24 into one man) and nothing in the text speaks against it. I therefore uphold `hashabiah-4`. One housekeeping note: `hashabiah-6`, "a Levite (NEH 10:9)" with an empty verse list, is almost certainly a stub created for this same covenant-sealer and then never populated; it should be absorbed into `hashabiah-4`. Whether `hashabiah-5` (Neh 3:17, ruler of half Keilah) is also the same man is a live question, but it is not what is in dispute here.

**Neh 12:24, word 6856743.** "And the chiefs of the Levites: Hashabiah, Sherebiah, and Jeshua son of Kadmiel, with their brethren opposite them, to praise and give thanks…" The verse is dated by 12:26 to "the days of Joiakim … and of Nehemiah the governor and Ezra the priest." The names are the Ezra-era company exactly: Hashabiah and Sherebiah (Ezra 8:18–19, 24; Neh 10:11–12) alongside Jeshua and Kadmiel (Ezra 2:40; Neh 9:4–5; 10:9). The model's reasoning — the Ezra 8:24 duo reappearing — is sound, and I would put it more strongly than its "medium": three of the four names are the Ezra 8 / Neh 10 group, in the same time frame, doing what Neh 9:4–5 shows that group doing (leading praise).

BibleData's answer `hashabiah-7` is "son of Mattaniah (NEH 11:22)". Neh 11:22 reads: "the overseer of the Levites at Jerusalem was Uzzi son of Bani son of Hashabiah son of Mattaniah son of Micha, of the sons of Asaph." The contemporary official there is Uzzi; Hashabiah is his great-grandfather. BibleData's own kinship edges say so (father → bani-9, whose son Uzzi is the living overseer). A man three generations above the current overseer cannot at the same time be a serving chief of the Levites in 12:24 — the record's genealogy contradicts the verse assignment. (One could try to rescue it by identifying Mattaniah of 11:22 with the contemporary Mattaniah of 11:17, but that only makes the 11:22 genealogy itself chronologically impossible; it does not turn the great-grandfather of an official into a colleague of Sherebiah.) The only thing that might tempt one toward the Asaphite line is that 12:24 concerns the praise "according to the commandment of David", and Asaphites were singers — but Neh 9:4–5 already shows the Hashabiah–Sherebiah–Jeshua–Kadmiel group leading exactly that kind of thanksgiving, so the Merarite Hashabiah-4 is not out of place. Source-wrong; the referent is `hashabiah-4`.

Note also that the garbled distinguisher on `hashabiah` claims Neh 12:24 as well; for the same reason as above it should be ignored.

---ANSWER---
[
  {
    "word_id": 6855548,
    "verdict": "model-wrong",
    "referent": "hashabiah-4",
    "confidence": "high",
    "reason": "The model followed the stray '(NEH 10:11, NEH 12:24)' tail in the distinguisher of `hashabiah`, whose verse list and kinship edges (son of Amaziah, 1 Chr 6:45) make him a David-era ancestor of Ethan, impossible among Nehemiah's covenant-sealers. The sealer paired with Sherebiah is the Levite who came up with Ezra (Ezra 8:19, 24), i.e. hashabiah-4; the empty stub hashabiah-6 should be folded into it."
  },
  {
    "word_id": 6856743,
    "verdict": "source-wrong",
    "referent": "hashabiah-4",
    "confidence": "high",
    "reason": "BibleData's hashabiah-7 is, by its own kinship edges, the great-grandfather of Uzzi the current overseer (Neh 11:22), so he cannot be a serving chief Levite in Nehemiah's day. The trio Hashabiah–Sherebiah–Jeshua b. Kadmiel is the Ezra 8 / Neh 9–10 company, making this the same man as Ezra 8:19, 24 and Neh 10:11."
  }
]

### H2828

**Reasoning**

The disputed word is the Hashum among the sealers of the covenant in Nehemiah 10. The structure of that list decides the case. Nehemiah 10:15–28 (Heb.) is headed רָאשֵׁי הָעָם, "the heads of the people," and what follows is not a roster of contemporary individuals but of clans named by their eponymous ancestors: Parosh, Pahath-moab, Elam, Zattu, Bani, Azgad, Bebai, Adonijah (= Adonikam), Bigvai, Adin, Ater, Hezekiah, ... Hashum, Bezai, Hariph (= Jorah), Anathoth, Nebai (= Nebo), Magpiash (= Magbish). This is, with minor variants, the same catalogue as the returnee families of Ezra 2:3–30 / Nehemiah 7:8–33. The immediate neighbours of the disputed word make the correspondence exact: Neh 10:17–20 runs Ater, Hezekiah, Azzur, Hodiah, **Hashum**, Bezai, Hariph, Anathoth, Nebai, and Ezra 2:16–19 runs "Ater of Hezekiah ... Bezai ... Jorah [Hariph] ... **Hashum**," followed at 2:29–30 by Nebo and Magbish. Hashum here is the clan of Ezra 2:19 / Neh 7:22, the same clan whose members appear in Ezra 10:33 — i.e., BibleData's record `hashum`. The reading model's reason ("covenant-sealer list order matches the Ezra 2 / Neh 7 family sequence") is precisely the right argument.

BibleData instead puts the verse under `hashum-2`, whose distinguisher is the man who "stood on Ezra's left hand" at Neh 8:4. There is no contradiction between that record's distinguisher and its verse list — both are internally consistent — so this is not the distinguisher/verse-list trap; it is a substantive misassignment. The encyclopedia has evidently reasoned that a lay leader prominent at the Law-reading in Neh 8 would naturally sign the covenant in Neh 10. That is a plausible historical guess about who physically set the seal on the clan's behalf, but it is not what the name in Neh 10:18 *refers to*: the text names the house, in a list of houses, and every other Ezra 2 family in that list is named the same way. Strong's own gloss reflects the same split — sense (1) "head of a family of 223 exiles," sense (2) "one who stood on Ezra's left" — and leaves 10:18 unassigned, which fits it belonging with sense (1).

Whether the Hashum of Neh 8:4 is himself an individual or is also the clan-name (several of the thirteen on the platform — Meshullam, Malchijah, Hashbaddanah — look like individuals, so `hashum-2` may legitimately be a distinct man) is not in dispute here and does not affect this verdict. Only Neh 10:18 is being judged, and there the referent is the clan-founder record.

---ANSWER---
[
  {
    "word_id": 6855573,
    "verdict": "source-wrong",
    "referent": "hashum",
    "confidence": "high",
    "reason": "Neh 10:15-28 is a list of 'heads of the people' by clan name paralleling Ezra 2 / Neh 7, and the sequence Ater, Hezekiah ... Hashum, Bezai, Hariph, Anathoth, Nebai matches Ezra 2:16-19, 29-30 exactly, so Hashum here is the returnee clan of Ezra 2:19 / Neh 7:22, not the individual of Neh 8:4."
  }
]

### H285

#### Reasoning

**The shape of the evidence.** The Chronicler's high-priestly line (1 Chr 6:4–15, MT 5:30–41) contains the pair Ahitub → Zadok twice: first at 6:7–8 (Amariah → Ahitub → Zadok → Ahimaaz), which is the Ahitub of 2 Sam 8:17 / 1 Chr 18:16, David's Zadok's father; and again at 6:11–12 (Amariah → Ahitub → Zadok → Shallum → Hilkiah → Azariah → Seraiah → Jehozadak), the Josianic/exilic end of the line. BibleData's `ahitub-2` verse list (2 Sam 8:17; 1 Chr 6:7, 6:8, 6:52; 1 Chr 18:16) is a coherent record of the *first* man; `ahitub-3` (1 Chr 6:11, 6:12, 9:11) is the *second*. The distinguishers are the muddled half: `ahitub-2`'s prose cites "1CH 9:11" and "son of Amariah (1CH 6:11)" — both of which belong to the second Ahitub — while its verse list attests neither verse. I trust the verse lists here, because they line up with the two distinct positions in the 1 Chr 6 chain; the prose has simply cross-wired the citations.

**Ezra 7:2 (word 6847733).** Ezra's pedigree runs Seraiah → Azariah → Hilkiah → Shallum → Zadok → **Ahitub** → Amariah → Azariah → Meraioth → … Aaron. The segment Seraiah–Azariah–Hilkiah–Shallum–Zadok–Ahitub is verbatim 1 Chr 6:11–14: only the *second* Zadok fathers Shallum, and only the *second* Ahitub is his father. Ezra's list then telescopes (after Amariah 6:11 it leaps via Azariah to Meraioth, skipping the whole Ahitub-1/Zadok-1/Ahimaaz stretch), but the telescoping happens *above* Ahitub, not below him. So the marked word is the 6:11–12 Ahitub, which BibleData holds under `ahitub-3`. The model is right; BibleData's verse list is wrong — probably seduced by its own mis-cited distinguisher ("father of Zadok the priest") without checking which Zadok. One caveat worth recording: some critics take the second Ahitub–Zadok pair in 1 Chr 6 as a dittograph of the first, in which case the two records would collapse; but the encyclopedia treats them as two men, and on that footing the assignment is not in doubt. **Source-wrong → `ahitub-3`.**

**Nehemiah 11:11 (word 6856184).** Seraiah son of Hilkiah, son of Meshullam, son of Zadok, son of Meraioth, son of **Ahitub**, נגיד בית האלהים. This is the same list as 1 Chr 9:11 (Azariah son of Hilkiah, son of Meshullam, son of Zadok, son of Meraioth, son of Ahitub, ruler of the house of God) — identical chain, name for name, differing only in the head (Seraiah/Azariah). BibleData assigns 1 Chr 9:11 to `ahitub-3` and Neh 11:11 to `ahitub-4`, i.e. it holds one man under two records; the parallel `meraioth-2`/`meraioth-3` kinship edges show the same doubling one generation down. `ahitub-4`'s distinguisher "leader of the house of God" also over-reads the Hebrew: נגיד בית האלהים is most naturally predicated of the head of the list (Seraiah/Azariah), not of the last ancestor named — but that is a label problem, not a referent problem. Both answers are right about the man; the fault is the duplicate. **Duplicate-records: `ahitub-3` ↔ `ahitub-4`.** `ahitub-3` should survive: it already holds the 1 Chr 9:11 parallel and is anchored in the 1 Chr 6 line (the 9:11/11:11 chain Ahitub–[Meraioth]–Zadok–Meshullam/Shallum–Hilkiah is the 6:11–13 chain with Meraioth interpolated). `ahitub-4` should be absorbed, and `meraioth-3` folded into `meraioth-2` alongside it. I note that `ahitub-3` is itself internally torn ("father of Zadok" from 6:12 vs "father of Meraioth, grandfather of Zadok" from 9:11), but that tension is inherited from the biblical text and is not what is being adjudicated here.

---ANSWER---
[
  {
    "word_id": 6847733,
    "verdict": "source-wrong",
    "referent": "ahitub-3",
    "confidence": "high",
    "reason": "Ezra's chain Seraiah–Azariah–Hilkiah–Shallum–Zadok–Ahitub reproduces 1 Chr 6:11–14 exactly; only the second Zadok fathers Shallum, so this Ahitub is the 1 Chr 6:11–12 man BibleData holds as ahitub-3, not David's Zadok's father (ahitub-2). BibleData's own verse list for ahitub-2 is otherwise coherent for the first Ahitub; its mis-cited distinguisher led the Ezra verse astray."
  },
  {
    "word_id": 6856184,
    "verdict": "duplicate-records",
    "referent": "ahitub-3",
    "confidence": "high",
    "reason": "Neh 11:11 and 1 Chr 9:11 give the identical chain Hilkiah–Meshullam–Zadok–Meraioth–Ahitub, so this is the same man BibleData already assigns to ahitub-3 at 1 Chr 9:11; ahitub-4 (with its twin meraioth-3) is a duplicate created from the Nehemiah parallel.",
    "survivor": "ahitub-3 survives, absorbing ahitub-4"
  }
]

### H3048

#### Reasoning

**The parallel structure of Nehemiah 12 is the key.** Neh 12:1–7 lists the priests who went up with Zerubbabel and Jeshua, and Neh 12:12–21 lists the heads of those same priestly houses in the days of Joiakim, *in the same order*. Laying the tail of each list side by side:

| 12:1–7 (fathers) | 12:12–21 (house heads under Joiakim) |
|---|---|
| Bilgah (12:5) | of Bilgah, Shammua (12:18) |
| Shemaiah (12:6) | of Shemaiah, Jehonathan (12:18) |
| Joiarib (12:6) | of Joiarib, Mattenai (12:19) |
| **Jedaiah (12:6)** | **of Jedaiah, Uzzi (12:19)** |
| Sallu (12:7) | of Sallai, Kallai (12:20) |
| Amok (12:7) | of Amok, Eber (12:20) |
| Hilkiah (12:7) | of Hilkiah, Hashabiah (12:21) |
| **Jedaiah (12:7)** | **of Jedaiah, Nethaneel (12:21)** |

So the text itself has two distinct Jedaiah houses: the first (12:6, paired with Joiarib) whose head is Uzzi (12:19); the second (12:7, following Hilkiah) whose head is Nethaneel (12:21). The 12:6 Jedaiah is the one that lines up with the Joiarib–Jedaiah pairing of 1 Chr 9:10, 1 Chr 24:7 and Neh 11:10, i.e. the famous priestly course of Ezra 2:36 / Neh 7:39. The 12:7 Jedaiah is otherwise unknown (some think it a doublet, but the parallel in 12:21 with a different head-name shows the compiler treated it as a separate house).

**Now BibleData's two records, tested against their own evidence.**

*jedaiah-2*: distinguisher says "NEH 12:6"; kinship says father → uzzi-6, and Uzzi is the head of the house in 12:19, which parallels the *12:6* Jedaiah. Verse list, however, says 12:7 and 12:19. The verse list is internally incoherent: 12:19 (Uzzi) belongs with 12:6, not 12:7.

*jedaiah-5*: distinguisher says "NEH 12:7"; kinship says father → nethanel-7, and Nethaneel is the head of the house in 12:21, which parallels the *12:7* Jedaiah. Verse list says 12:6 and 12:21 — again incoherent, since 12:21 (Nethaneel) belongs with 12:7, not 12:6.

Three of the four signals in each record (distinguisher, kinship edge, the 12:19/12:21 entry of the verse list) agree with each other and with the parallel structure; the single outlier in each record is the 12:6/12:7 entry, and the two outliers are mirror images of one another. This is a plain swap in the verse lists: jedaiah-2 ↔ jedaiah-5 for 12:6/12:7. I trust the distinguishers and kinship edges here because they are corroborated by the text's own parallelism; the verse-list entries are the half that contradicts everything else.

**Verdicts.**

Word 6856568 (Neh 12:6, "Joiarib, Jedaiah"): the Joiarib-paired Jedaiah, house-head Uzzi. That is jedaiah-2 by its distinguisher (NEH 12:6) and kinship (Uzzi). The model is right; BibleData's verse list is wrong against its own record. Source-wrong.

Word 6856572 (Neh 12:7, "Hilkiah, Jedaiah"): the second Jedaiah, house-head Nethaneel. That is jedaiah-5 by its distinguisher (NEH 12:7) and kinship (Nethanel). The model is right; BibleData's verse list is wrong. Source-wrong. (The model's stated reason, "son of Joiarib", is a poor justification — the 12:7 Jedaiah is the one *not* adjacent to Joiarib — but its answer matches the record's actual anchor, 12:7/Nethaneel.)

A side note outside the dispute: jedaiah-5's claim on Neh 11:10 ("Jedaiah the son of Joiarib") is itself doubtful. Neh 11:10 parallels 1 Chr 9:10 ("Jedaiah, and Jehoiarib, and Jachin"), where "son of" is widely taken as a dropped waw, and BibleData assigns 1 Chr 9:10 to jedaiah-2. So 11:10 probably belongs to jedaiah-2 as well, and jedaiah-5's "son of Joiarib" label is likely a mis-anchored distinguisher. That does not affect the two verdicts here, but it is worth flagging for whoever cleans up these records.

---ANSWER---
[
  {
    "word_id": 6856568,
    "verdict": "source-wrong",
    "referent": "jedaiah-2",
    "confidence": "high",
    "reason": "Neh 12:6's Jedaiah (paired with Joiarib) is the house headed by Uzzi in the parallel 12:19; jedaiah-2's own distinguisher cites NEH 12:6 and its kinship names Uzzi as son, so BibleData's verse-list entry contradicts its own record — the 12:6/12:7 entries are simply swapped between jedaiah-2 and jedaiah-5."
  },
  {
    "word_id": 6856572,
    "verdict": "source-wrong",
    "referent": "jedaiah-5",
    "confidence": "high",
    "reason": "Neh 12:7's Jedaiah (after Hilkiah) is the house headed by Nethaneel in the parallel 12:21; jedaiah-5's distinguisher cites NEH 12:7 and its kinship names Nethanel as son, so the verse list's assignment of 12:7 to jedaiah-2 is the swapped half of the same error."
  }
]

### H3059

**Analysis of word 6886221 (2 Chronicles 25:23)**

The Hebrew syntax settles this. The object phrase is וְאֵת אֲמַצְיָהוּ מֶלֶךְ יְהוּדָה בֶּן־יוֹאָשׁ בֶּן־יְהוֹאָחָז — "Amaziah king of Judah, son of Joash, son of Jehoahaz" — a single genealogical chain hanging off Amaziah, the object of תָּפַשׂ. The subject, Joash king of Israel, comes *after* the verb (תָּפַשׂ יוֹאָשׁ מֶלֶךְ יִשְׂרָאֵל) and carries no patronymic of its own. So the marked word is Amaziah's grandfather, not the Israelite king's father.

Amaziah of Judah was the son of Joash/Jehoash of Judah (2 Chr 24:27; 25:1), who was the son of Ahaziah of Judah (2 Kgs 12:1; 2 Chr 22:11; 24:1 via Athaliah's line). The Chronicler writes this Ahaziah's name with the theophoric element in front — יְהוֹאָחָז — a known variant; 2 Chr 21:17 does the same ("Jehoahaz the youngest of his sons"), and BibleData attaches both verses to `ahaziah-2` for exactly this reason. The synoptic parallel confirms it: 2 Kings 14:13 reads "Amaziah king of Judah, the son of Jehoash the son of *Ahaziah*" (אֲחַזְיָהוּ) for the identical clause.

The reading model's answer ("Joash king of Israel, son of Jehoahaz, in same account") is a real fact about the *other* Joash in this verse — the Israelite king was indeed son of Jehoahaz ben Jehu (2 Chr 25:17, 25) — but it attached the patronymic to the wrong Joash. Two kings named Joash face each other in this verse, and only the Judahite one carries the בֶּן chain. The model conflated them.

BibleData's distinguisher and verse list for `ahaziah-2` are consistent here (2 Chr 25:23 "as Jehoahaz" is explicitly in the list), so there is no source-internal contradiction to weigh. `jehoahaz` (the Israelite) is attested in 25:17 and 25:25 of this chapter but correctly *not* in 25:23.

Verdict: model-wrong; the referent is `ahaziah-2`, with high confidence.

---ANSWER---
[
  {
    "word_id": 6886221,
    "verdict": "model-wrong",
    "referent": "ahaziah-2",
    "confidence": "high",
    "reason": "The בֶּן chain 'son of Joash, son of Jehoahaz' modifies Amaziah king of Judah (the object), whose grandfather was Ahaziah of Judah, spelled Jehoahaz by the Chronicler as in 2 Chr 21:17; the parallel 2 Kings 14:13 reads 'son of Jehoash the son of Ahaziah'. The model attached the patronymic to the Israelite Joash, the verse's post-verbal subject, which carries no patronymic."
  }
]

### H3088

The verse 2 Kings 8:29 contains three occurrences of the name, but only one under this Strong number: the two spellings יֹורָם (H3141) refer to the king of Israel ("Joram the king", "Joram son of Ahab"), while the marked word יְהֹורָם (H3088) sits in the phrase וַאֲחַזְיָהוּ בֶן יְהֹורָם מֶלֶךְ יְהוּדָה — "Ahaziah son of Jehoram king of Judah." The syntax is unambiguous: this Jehoram is the father of Ahaziah and is explicitly titled king of Judah, which is the record `jehoram` (son of Jehoshaphat; kinship edge father -> ahaziah-2). The same verse then distinguishes him from יֹורָם בֶּן־אַחְאָב, the king of Israel, so the two men are deliberately set side by side and cannot be confused.

BibleData's verse list is verse-level, and here its entry for 2 Kings 8:29 lists only `jehoram-2` (as "King Joram"). That is correct for the H3141 occurrences in the verse, but the verse list has no entry for `jehoram` at 8:29 at all, even though the verse names him. So the mismatch is a coverage gap in the verse list: BibleData is not wrong that Joram of Israel appears in the verse, but the H3088 word specifically is the Judahite king, and the encyclopedia's own record for him (distinguisher "King of Judah and son of Jehoshaphat", father of ahaziah-2) is the right match. Since BibleData's answer for this word — the only thing under H3088 in the verse — is a record its own evidence contradicts (jehoram-2 is son of Ahab, king of Israel, not father of Ahaziah of Judah), this is source-wrong, not duplicate-records: the two records are genuinely two different men.

The reading model's answer `jehoram` is correct.

---ANSWER---
[
  {
    "word_id": 6666988,
    "verdict": "source-wrong",
    "referent": "jehoram",
    "confidence": "high",
    "reason": "The marked H3088 word is in the phrase 'Ahaziah son of Jehoram king of Judah', explicitly the Judahite king and father of Ahaziah, contrasted in the same verse with Joram (H3141) son of Ahab; BibleData's verse-level list only registers the Israelite king here and omits the Judahite one."
  }
]

### H3379

1 Chronicles 5:17 sits inside the Chronicler's Transjordanian register (Reuben, Gad, half-Manasseh), and the verse dates that register: "in the days of Jotham king of Judah and in the days of Jeroboam king of Israel." Jotham of Judah reigned c. 750–732 BCE (2 Kings 15:32–38), and the only Jeroboam whose reign overlaps or immediately precedes his is Jeroboam II son of Joash (c. 793–753 BCE; 2 Kings 14:23–29). Jeroboam I son of Nebat died around 910 BCE, roughly a century and a half before Jotham was born; there is no way a single census could be "in the days of" both Jotham and Jeroboam I. The pairing of a Judahite and an Israelite king in one dating formula is the same device used in 2 Kings 15:1 and Hosea 1:1/Amos 1:1, and there too the Israelite king is Jeroboam II.

The context reinforces this: 1 Chronicles 5:26 goes on to describe the deportation of these same tribes by Tiglath-pileser ("Pul... Tilgath-pilneser"), which followed the Jotham/Jeroboam II era by a generation. Jeroboam II's territorial recovery "from the entrance of Hamath to the sea of the Arabah" (2 Kings 14:25, 28) is exactly the period when Gilead and Bashan would have been securely Israelite and worth registering. Every standard commentary reads the verse this way.

BibleData's verse list assigns this verse to the `jeroboam` record (son of Nebat). Its own distinguisher for `jeroboam-2` ("King of Israel, son of Joash") and its kinship edges (son -> joash-4, father -> zechariah) describe precisely the man the Chronicler is dating by, so BibleData's verse list contradicts the referent its own records define. This looks like a mechanical default — every Chronicles occurrence of H3379 was swept into the first record, and 1 Chronicles 5:17 is the one Chronicles verse that does not belong to Jeroboam I. The reading model's answer, `jeroboam-2`, is correct; its confidence should be high rather than medium, since the chronology is decisive and not merely suggestive.

Verdict: source-wrong.

---ANSWER---
[
  {
    "word_id": 6860483,
    "verdict": "source-wrong",
    "referent": "jeroboam-2",
    "confidence": "high",
    "reason": "The verse dates the Transjordan register 'in the days of Jotham king of Judah' (c. 750–732 BCE); the only Jeroboam whose reign is contemporaneous is Jeroboam II son of Joash, while Jeroboam I died some 150 years earlier. BibleData's verse list put the verse under the son-of-Nebat record against its own definition of jeroboam-2."
  }
]

### H3406

**1 Chronicles 25:22 (word 6870953)**

The chapter is self-contained. 1 Chronicles 25:1–6 names the musician families — the sons of Asaph, Jeduthun, and Heman — and verses 9–31 then distribute the twenty-four lots among exactly those men. Heman's fourteen sons in verse 4 are Bukkiah, Mattaniah, Uzziel/Shubael, Shebuel, **Jerimoth**, Hananiah, Hanani, Eliathah, Giddalti, Romamti-ezer, Joshbekashah, Mallothi, Hothir, Mahazioth. The lot list assigns Bukkiah 6th, Mattaniah 9th, Shubael 13th, **Jeremoth 15th**, Hananiah 16th, Joshbekashah 17th, Hanani 18th, Mallothi 19th, Eliathah 20th, Hothir 21st, Giddalti 22nd, Mahazioth 23rd, Romamti-ezer 24th. Every name in verses 9–31 is drawn from verses 2–4; there is no room for an outsider. The vocalization differs between v.4 (יְרִימֹות) and v.22 (לִירֵמֹות), but the Strong entry itself lists both forms as one name, and the same kind of spelling wobble appears elsewhere in this very chapter (Uzziel/Shubael v.4 vs Shubael v.20, Izri/Zeri). The 15th lot is unambiguously Heman's son.

BibleData's record `jerimoth-3` says so in its own distinguisher — "son of Heman_3 (1CH 25:4), chosen fifteenth by lot (1CH 25:22)" — yet its verse list stops at 25:4, and 25:22 has been filed instead under `jeremoth-3`, the descendant of Mushi from 23:23 / 24:30. That is exactly the distinguisher-versus-verse-list contradiction the brief warns about. Here I trust the distinguisher: it is corroborated by the text, whereas the verse-list placement is not. Mushi's line is Merarite (23:21–23), organised by family divisions in chapters 23–24, whereas Heman is a Kohathite (6:33) and chapter 25 is a separate musician roster; nothing links the Merarite Jeremoth to a singers' lot.

This is not a duplicate-record case: `jeremoth-3` (Merarite, son/descendant of Mushi) and `jerimoth-3` (Kohathite, son of Heman) are genuinely different men, and both records are legitimate for their own verses. The error is confined to BibleData's verse-level assignment of 25:22. The model's answer is correct.

---ANSWER---
[
  {
    "word_id": 6870953,
    "verdict": "source-wrong",
    "referent": "jerimoth-3",
    "confidence": "high",
    "reason": "The lots of 1 Chronicles 25:9-31 are distributed exclusively among the sons of Asaph, Jeduthun, and Heman named in 25:2-4, and the fifteenth lot falls to Heman's son Jerimoth; BibleData's own distinguisher for jerimoth-3 cites 1CH 25:22, but its verse list misfiles the verse under the Merarite jeremoth-3 (descendant of Mushi), a different man from a different roster."
  }
]

### H3442

**Ezra 8:33 (word 6848942).** The verse labels Jozabad son of Jeshua and Noadiah son of Binnui as הַלְוִיִּם, Levites, set beside the two priests Meremoth and Eleazar. The Jeshua of record `jeshua-3` is the son of Jozadak, the Aaronide high priest whose sons are priests (Ezra 10:18 "the sons of Jeshua the son of Jozadak... and his brethren"); a Levite Jozabad cannot be his son. The Levitical Jeshua is the house of Ezra 2:40 / Neh 7:43 ("the Levites: the children of Jeshua and Kadmiel"), and a Levite Jozabad of that circle recurs in Ezra 10:23 and Neh 8:7. BibleData's own kinship edges already say this — `jeshua-5` carries "father -> jozabad-5" — so its verse list, which puts Ezra 8:33 under `jeshua-3`, contradicts its own kinship table. I trust the kinship edge and the word הַלְוִיִּם over the verse list. Whether "son of Jeshua" means a literal father or the ancestral house makes no difference to the record: `jeshua-5`. Source-wrong; the model is right.

**Nehemiah 12:8 (word 6856585).** Neh 12:1–9 is explicitly the roster of priests and Levites "that went up with Zerubbabel the son of Shealtiel, and Jeshua" — the return generation, the same generation Ezra 2:40 counts. The Levite list "Jeshua, Binnui, Kadmiel, Sherebiah, Judah, Mattaniah" matches the Ezra 2:40 / Neh 7:43 house names (Jeshua, Kadmiel, and Binnui/Hodaviah) and belongs with `jeshua-5`, whose record is precisely the returning Levite. The model reached for `jeshua-7`, the Levite who reads the law and stands on the platform in Neh 8:7 / 9:4–5, on the grounds that he is also grouped with Kadmiel and Sherebiah — but that is roughly ninety years later under Ezra and Nehemiah, and the same grouping appears in both eras precisely because these are persistent Levitical house names. BibleData's split between `jeshua-5` (Zerubbabel era, descendant of Hodaviah) and `jeshua-7` (Nehemiah era, "son of Kadmiel" per 12:24) is chronologically defensible, and 12:8 falls on the Zerubbabel side of that split. The genuine caveat is that if one reads all these Levite "Jeshuas" as one house rather than individuals, 5 and 7 collapse into a single referent — but even on that reading BibleData's assignment of 12:8 to the return-era record is the right side, and the model's stated reason (the Neh 8–9 grouping) is the wrong basis. Model-wrong, medium confidence.

**Nehemiah 12:10 (word 6856606).** "Jeshua begat Joiakim, Joiakim begat Eliashib, Eliashib Joiada, Joiada Jonathan, Jonathan Jaddua" is the high-priestly succession, nothing else: Eliashib is the high priest of Neh 3:1 and 13:4, and Neh 12:26 — which BibleData itself assigns to `jeshua-3` — spells out "Joiakim the son of Jeshua, the son of Jozadak." BibleData nonetheless files 12:10 under `jeshua-7`, the Levite of Neh 8–9, and has evidently manufactured a kinship edge "jeshua-7 father -> joiakim" from that misfiling, alongside the correct "jeshua-3 father -> joiakim-2." A Levite of the Ezra–Nehemiah generation cannot be the grandfather of Eliashib who was already high priest in Nehemiah's day. Source-wrong, referent `jeshua-3`, high confidence. A knock-on for the editors: the two Joiakim records (`joiakim` and `joiakim-2`) are almost certainly the same high priest, one of them created by this very error, and `jeshua-7`'s "father -> joiakim" edge should be deleted.

---ANSWER---
[
  {
    "word_id": 6848942,
    "verdict": "source-wrong",
    "referent": "jeshua-5",
    "confidence": "high",
    "reason": "Jozabad son of Jeshua is expressly a Levite, so his father cannot be the Aaronide high priest Jeshua son of Jozadak; he belongs to the Levitical house of Ezra 2:40, and BibleData's own kinship table already records jeshua-5 as father of jozabad-5, contradicting its verse list."
  },
  {
    "word_id": 6856585,
    "verdict": "model-wrong",
    "referent": "jeshua-5",
    "confidence": "medium",
    "reason": "Neh 12:1-9 is the roster of those who came up with Zerubbabel and Jeshua, i.e. the same return generation as Ezra 2:40, so the Levite Jeshua here is the returning house-head jeshua-5; the Neh 8-9 Levite (jeshua-7) is two or three generations later, and the recurrence of Kadmiel and Sherebiah reflects persistent house names, not identity of person."
  },
  {
    "word_id": 6856606,
    "verdict": "source-wrong",
    "referent": "jeshua-3",
    "confidence": "high",
    "reason": "'Jeshua begat Joiakim... Eliashib... Joiada... Jaddua' is the high-priestly succession, and Neh 12:26 (which BibleData itself assigns to jeshua-3) names Joiakim as son of Jeshua son of Jozadak; filing it under the Levite jeshua-7 contradicts BibleData's own evidence and spawned a spurious jeshua-7 -> joiakim edge."
  }
]

### H43

**The text.** 1 Chr 6:37 (MT 6:22) reads בֶּן תַּחַת בֶּן אַסִּיר בֶּן אֶבְיָסָף בֶּן קֹרַח — Tahath ← Assir ← Ebiasaph ← Korah. The marked word is unambiguous on its face: this Ebiasaph is the *son of Korah* and the *father of Assir*.

**Against the two records.** BibleData's `ebiasaph` record is distinguished as "son of Elkanah (1CH 6:23)", with kinship *son of elkanah-3, father of assir-2*. Its `abiasaph` record is "son of Korah (EXO 6:24)", *son of korah-3, father of kore*. The marked word in 6:37 fits neither record cleanly and straddles both: "father of Assir" matches `ebiasaph`, "son of Korah" matches `abiasaph`. So the verse BibleData assigns to `ebiasaph` directly contradicts that record's own distinguisher ("son of Elkanah") — the man in 6:37 has no Elkanah between him and Korah.

**Are they two men?** The two Chronicles lists disagree with each other. 1 Chr 6:22–23 (MT 6:7–8) runs Korah → Assir → Elkanah → Ebiasaph → Assir → Tahath; 6:37 runs Korah → Ebiasaph → Assir → Tahath. Exod 6:24 names Assir, Elkanah and Abiasaph as three *brothers*, sons of Korah, and 1 Chr 9:19 and 26:1 again make Ebiasaph the son of Korah (father of Kore). The standard explanation (Curtis–Madsen, Japhet, Knoppers) is that the "his son" chain of 6:22–23 has strung the three brothers of Exod 6:24 into a father-son sequence; the Ebiasaph of 6:23 and the Ebiasaph of 6:37 are the same ancestor of Heman, and the same as Abiasaph son of Korah. BibleData's second record exists only because it read 6:23 at face value and then, inconsistently, filed 6:37 under it even though 6:37 gives the *other* filiation. Four of the five occurrences of this man (Exod 6:24; 1 Chr 6:37; 9:19; 26:1) make him son of Korah; only 6:23 deviates, and that verse is the one whose chain is textually suspect.

**Verdict.** The reading model's `abiasaph` is right about the man and is the record whose distinguisher the marked word actually satisfies. BibleData's `ebiasaph` is the same man held under a second record, created from the anomalous 6:23 chain. I trust the verse list's *evidence* (the verses themselves) over the distinguisher string, and the verses say one Korahite Ebiasaph. This is duplicate-records; `abiasaph` should survive — it carries both name forms (H23 and H43), the Exodus anchor, and the majority filiation — and absorb `ebiasaph` together with its two verses (6:23, 6:37) and its father-of-Assir edge. Confidence is high that the model's answer is correct for this word; I mark medium rather than high only because a strictly face-value reader who keeps the 6:23 man separate would instead call this source-wrong — but on either view the word belongs to `abiasaph`.

---ANSWER---
[
  {
    "word_id": 6861066,
    "verdict": "duplicate-records",
    "referent": "abiasaph",
    "confidence": "medium",
    "reason": "1 Chr 6:37 explicitly makes this Ebiasaph the son of Korah, contradicting the 'son of Elkanah' distinguisher of the record BibleData files it under; with Exod 6:24, 1 Chr 9:19 and 26:1 all agreeing on son-of-Korah, the 6:23 'son of Elkanah' is the same man in a garbled chain, not a second person.",
    "survivor": "abiasaph survives; it absorbs ebiasaph (1 Chr 6:23, 6:37 and the father-of-Assir edge)"
  }
]

### H44

#### Reasoning

**The clan lists settle Joshua 17:2 outright.** Joshua 17:2 enumerates the non-Machirite clans of Manasseh: Abiezer, Helek, Asriel, Shechem, Hepher, Shemida. Numbers 26:30–32 gives the identical six clans in the identical order, except that the first is spelled אִיעֶזֶר (Iezer, H372) rather than אֲבִיעֶזֶר — the well-known aphaeresis of the first syllable (like Ahimelech/Melech, Abishalom/Absalom). Nobody disputes that Iezer of Num 26:30 and Abiezer of Josh 17:2 are the same clan eponym; BibleData itself encodes this by giving the `iezer` record both name labels (Iezer=H372, Abiezer=H44) and attesting it in Josh 17:2. So BibleData's answer `iezer` for word 6589163 is correct on its own evidence, and its distinguisher ("son of Gilead, NUM 26:30") and verse list agree — no internal contradiction here.

**Judges 8:2 is the same clan.** Gideon is "son of Joash the Abiezrite" (Judg 6:11, 6:24, 8:32), and in Judg 6:34 "Abiezer was gathered after him" — the clan mustering. "The vintage of Abiezer" in 8:2 is Gideon's self-deprecating reference to his own clan's achievement against Ephraim's "gleanings." The clan is the Manassite Abiezer of Josh 17:2 / Num 26:30 (Ophrah lies in Manasseh's territory west of Jordan). BibleData's `iezer` record already carries the `ancestor -> joash` edge, so its own kinship data confirms the assignment. Again `iezer` is right.

**So is the model's `abiezer-2` wrong, or is it the same man under a second record?** The `abiezer-2` record is "son of Hammolecheth (1 Chr 7:18)". In 1 Chr 7:14–19 the Chronicler gives a notoriously reorganized Manassite genealogy: Hammolecheth, sister of Gilead (or of Machir), bears Ishod, Abiezer and Mahlah. Two of those three names are clan names from the Numbers list — Abiezer (= Iezer) and Mahlah (a Zelophehad daughter/clan in Num 26:33) — attached to a different parent. Strong's lexicon itself treats this as a single figure: "a Manassite, called 'son' of Gilead, also son of Gilead's sister." That is the consensus reading: the Chronicler is relating clan eponyms, not introducing a second, otherwise unattested individual named Abiezer within the same tribe and generation. The only ground for keeping two records is the literal difference of parentage (son of Gilead vs. son of Gilead's sister), and for a clan eponym that is a divergence between two genealogical traditions about one clan, not evidence of two men.

Therefore both answers point to the same Manassite clan founder; BibleData merely holds him under two records (`iezer` and `abiezer-2`). The model's high confidence is defensible in substance but it picked the record BibleData does not use for these verses; BibleData's answer is the internally consistent one. This is **duplicate-records**, not model-wrong.

**Which record should survive.** `iezer` should survive and absorb `abiezer-2`: it already carries both Strong labels (H372 and H44), three of the four Manassite occurrences, and the kinship edges to Gilead and Joash that tie the Numbers clan to the Judges narrative. Merging in `abiezer-2` adds 1 Chr 7:18 and the Hammolecheth mother edge, which should be kept but flagged as the Chronicler's alternative genealogy. (The head name might better be "Abiezer," since that form appears in six of seven Hebrew occurrences, but that is a labelling choice, not a referent question.)

Confidence: high that both disputed words refer to the Manassite clan Abiezer/Iezer and that BibleData's `iezer` is correct; medium-high that the 1 Chr 7:18 record is the same eponym rather than a distinct person, since it rests on the near-universal scholarly reading of the Chronicler's genealogy rather than an explicit textual equation.

**A gap outside the dispute worth recording:** Judges 6:34 (word 6597998, "Abiezer was gathered after him") has no BibleData answer at all. It is the same clan and should be assigned to the surviving record.

---ANSWER---
[
  {
    "word_id": 6589163,
    "verdict": "duplicate-records",
    "referent": "iezer",
    "confidence": "high",
    "reason": "Josh 17:2's six-clan list is identical to Num 26:30-32 where the first clan is spelled Iezer, so this is the Manassite clan eponym BibleData holds as `iezer`; the model's `abiezer-2` (son of Hammolecheth, 1 Chr 7:18) is the Chronicler's re-parented version of the same clan founder, i.e. the same man under a second record.",
    "survivor": "iezer survives, absorbing abiezer-2 (carry over 1 Chr 7:18 and the Hammolecheth edge as an alternative genealogy)"
  },
  {
    "word_id": 6598942,
    "verdict": "duplicate-records",
    "referent": "iezer",
    "confidence": "high",
    "reason": "'The vintage of Abiezer' is Gideon's own clan (Joash the Abiezrite, Judg 6:11, 6:34, 8:32), the Manassite clan of Num 26:30/Josh 17:2 that BibleData's `iezer` record already links to Joash; the model's `abiezer-2` names the same clan eponym via 1 Chr 7:18.",
    "survivor": "iezer survives, absorbing abiezer-2"
  }
]

### H4441

The Hebrew settles the first-order question by itself. Jeremiah 21:1 reads פַּשְׁחוּר בֶּן־מַלְכִּיָּה sent by "the king Zedekiah"; Jeremiah 38:1 reads פַּשְׁחוּר בֶּן־מַלְכִּיָּה among the officials (שָׂרִים, 38:4) under the same Zedekiah, in the same siege. Same personal name, same patronymic, same reign, same milieu: there is no textual basis for making the father in 21:1 a different man from the father in 38:1, and I know of no commentator who separates them. Whichever record holds Jeremiah 38:1's Malchiah must therefore hold Jeremiah 21:1's. BibleData holds Jer 38:1 under `malchijah-10`, so its assignment of Jer 21:1 to `malchijah-2` contradicts its own placement of the twin verse.

The `malchijah-2` record is also internally impossible for this verse. Its verse list puts the man at Ezra's reading of the Law (Neh 8:4), among the sealers of the covenant (Neh 10:3) and at the wall dedication (Neh 12:42) — Nehemiah's generation, c. 445. A man whose son Pashhur was already a royal official in 588 cannot be that priest. The only part of `malchijah-2` that could legitimately touch Jeremiah is the 1 Chr 9:12 / Neh 11:12 component — Adaiah son of Jeroham son of Pashhur son of Malchijah, a priestly line whose "Pashhur son of Malchijah" stands three generations before a post-exilic priest and so could chronologically be the Pashhur of Jeremiah's day. Some older reference works float that identification, but it is speculative: Pashhur in Jer 38 is one of the שָׂרִים, not marked as a priest (the priestly label in 21:1 belongs to Zephaniah ben Maaseiah), and the Chronicler's genealogy gives no hint of the connection. So `malchijah-2` as constituted — a fusion of a pre-exilic ancestor with a Nehemiah-era priest — is a defective record whatever we do with Jeremiah, but that is a separate cleanup, not a reason to keep Jer 21:1 under it.

Two caveats that do not change the verdict. First, `malchijah-10` is itself a questionable fusion: it identifies Pashhur's father with the מַלְכִּיָּהוּ בֶן־הַמֶּלֶךְ of Jer 38:6 whose cistern Jeremiah was thrown into. Zedekiah was about 32 in 588, so "son of King Zedekiah" with an adult grandson Pashhur is impossible; בֶּן־הַמֶּלֶךְ is either a royal official's title or a son of an earlier king, and whether he is the same man as Pashhur's father is unprovable either way. Second, the residual open question — whether the Jer 21:1/38:1 Malchiah is also the 1 Chr 9:12 ancestor — would, if answered yes, call for splitting `malchijah-2` in two and merging its 1 Chr 9:12 half into `malchijah-10`. That would be a duplicate-records fix downstream; it does not make the present assignment right, because the record it points to is dominated by a man a century and a half too late.

The model's reasoning ("same father-son pair as Jer 38:1") is exactly the decisive point. BibleData's verse list is wrong here, against its own handling of the parallel verse.

---ANSWER---
[
  {
    "word_id": 6711709,
    "verdict": "source-wrong",
    "referent": "malchijah-10",
    "confidence": "high",
    "reason": "Jer 21:1 and Jer 38:1 name the identical pair 'Pashhur son of Malchiah' under the same king Zedekiah, and BibleData itself files 38:1 under malchijah-10; malchijah-2's verse list makes him a contemporary of Nehemiah (Neh 8:4, 10:3, 12:42), which is chronologically impossible for the father of a royal official of 588. Whether this Malchiah is also the 1 Chr 9:12 priestly ancestor remains open, but that would be a later split-and-merge of malchijah-2, not a reason to keep this verse under it."
  }
]

### H469

**The case.** 1 Chronicles 15:5–10 is a roster of Levitical houses summoned by David to carry the ark: Kohath (v.5), Merari (v.6), Gershom (v.7), Elizaphan (v.8), Hebron (v.9), Uzziel (v.10). Hebron and Uzziel are sons of Kohath (Exod 6:18), and Elizaphan is Uzziel's son (Exod 6:22; Num 3:30, where he is נְשִׂיא בֵית־אָב לְמִשְׁפְּחֹת הַקְּהָתִי). The list is therefore Kohathite sub-houses that had attained independent standing, and "the sons of Elizaphan" is exactly the same house that reappears in 2 Chronicles 29:13 ("of the sons of Elizaphan; Shimri and Jeiel") — a verse BibleData itself files under `elzaphan`. The Hebrew here is the long form אֱלִיצָפָן, the same form used in Num 3:30 for Uzziel's son, so the spelling is no help to the Zebulunite reading and no obstacle to the Kohathite one.

The `elizaphan` record is the Zebulunite son of Parnach appointed in Num 34:25 to apportion the land — a layman of a non-Levitical tribe, some four centuries earlier, who cannot head a Levitical clan in David's ark procession. Nothing in the verse or its neighbours points to Zebulun.

**The internal contradiction.** BibleData's verse list for `elzaphan` omits 1 Chr 15:8, yet its kinship edges for `elzaphan` record `ancestor -> shemaiah-7`, `ancestor -> jeiel-7`, `ancestor -> shimri-4` — Shemaiah being the chief of 1 Chr 15:8 and Shimri/Jeiel the men of 2 Chr 29:13. So BibleData's own genealogy already knows that the Shemaiah of this verse descends from Uzziel's son; only the verse list assigns the verse to the Zebulunite. I trust the kinship edges and the parallel in 2 Chr 29:13 over the verse list, which looks like a mechanical split on KJV spelling (Elizaphan vs Elzaphan) rather than a judgment about the man.

**The model.** Sonnet's answer `elzaphan` is right. Its stated reasoning is slightly muddled — the house of Elizaphan is not "distinct from Uzziel's line" but a branch of it that had grown large enough (200 men) to be counted separately from the remaining "sons of Uzziel" in v.10 — but the identification stands.

**Verdict.** Source-wrong: BibleData assigns the verse to a record its own kinship data and its own handling of 2 Chr 29:13 contradict. The two records are not duplicates; the Zebulunite of Num 34:25 is a genuinely different man and `elizaphan` should keep that one verse only.

---ANSWER---
[
  {
    "word_id": 6865923,
    "verdict": "source-wrong",
    "referent": "elzaphan",
    "confidence": "high",
    "reason": "1 Chr 15:8 lists Kohathite Levitical houses (Hebron, Uzziel, Elizaphan) and parallels 2 Chr 29:13, which BibleData itself files under elzaphan; BibleData's own kinship edges make shemaiah-7 of this verse a descendant of elzaphan. The Zebulunite of Num 34:25 cannot head a Levitical clan, so the verse-list assignment contradicts the record's own evidence."
  }
]

### H4983

**The two records are the same man; the disputed word belongs to him under either label.**

*What the texts say.* Nehemiah 12:8 lists the Levites who came up with Zerubbabel and Jeshua and singles out מַתַּנְיָה עַל־הֻיְּדוֹת, "Mattaniah, over the thanksgiving(s), he and his brethren," with Bakbukiah and Unni "over against them" in 12:9. Nehemiah 11:17 gives "Mattaniah son of Mica son of Zabdi son of Asaph, the chief to begin the thanksgiving in prayer (רֹאשׁ הַתְּחִלָּה יְהוֹדֶה), and Bakbukiah second among his brethren." Nehemiah 12:25 again pairs "Mattaniah and Bakbukiah" at the head of a list. 1 Chronicles 9:15, the Chronicler's parallel to the Nehemiah 11 census of Jerusalem, has "Mattaniah son of Mica son of Zichri son of Asaph" (Zichri/Zabdi being the ordinary variant). The thanksgiving office and the Mattaniah–Bakbukiah pairing tie 12:8 to 11:17 and 12:25 so tightly that the compiler plainly intends one figure; the only wrinkle is chronological — 12:8 sets him in Zerubbabel's generation, 11:17 in Nehemiah's, and 11:22 makes a Mattaniah son of Mica the great‑grandfather of Uzzi, an overseer in Nehemiah's day. That is a known difficulty (either the name is a house-name, or 11:17 draws on an older list), but it does not create a *second* record to choose from: the text gives the same patronymic in every place, and no candidate in the list distinguishes an earlier from a later Mattaniah son of Mica.

*The two records.* `mattaniah-2` is "son of Mica (1CH 9:15), Levite (NEH 12:8)"; `mattaniah-9` is "descendant of Mica (NEH 11:17), gatekeeper (NEH 12:25)". Since 1 Chr 9:15 and Neh 11:17 are the parallel Jerusalem lists, these are the same Asaphite Levite held under two slugs. The distinguisher of `mattaniah-2` cites Neh 12:8 while its verse list omits it — the contradiction the brief warns of — and the verse list of `mattaniah-9` holds 12:8 instead. Here I trust the verse lists over the prose only in the sense that they show where BibleData actually filed the verse; the prose shows that whoever wrote the `mattaniah-2` distinguisher also thought 12:8 belonged to the son of Mica. Both halves agree on the man; they disagree only on which duplicate to file him under.

*Which should survive.* `mattaniah-9` carries the coherent Nehemiah cluster (11:17, 11:22, 12:8, 12:25) and a sound kinship edge (descendant of Mica). `mattaniah-2` is contaminated: it also holds 2 Chronicles 20:14, the Mattaniah who is great‑great‑grandfather of Jahaziel in Jehoshaphat's reign — a man some three centuries earlier, Strong's sense 6, who needs a record of his own — and its kinship edges mix Jeiel (from 2 Chr 20:14) with Hashabiah (from Neh 11:22, a verse BibleData files under `mattaniah-9`). So `mattaniah-9` should survive and absorb 1 Chr 9:15 from `mattaniah-2`; 2 Chr 20:14 should be split out to a fresh record rather than travelling with either.

The Sonnet answer is not wrong about the man — it followed the distinguisher's own citation — and BibleData's verse list is not wrong about the man either. Verdict: duplicate-records.

---ANSWER---
[
  {
    "word_id": 6856590,
    "verdict": "duplicate-records",
    "referent": "mattaniah-9",
    "confidence": "high",
    "reason": "Neh 12:8's Mattaniah over the thanksgiving, paired with Bakbukiah, is the Mattaniah son of Mica of Neh 11:17 and its Chronicles parallel 1 Chr 9:15; BibleData holds that one Asaphite under both mattaniah-2 (1 Chr 9:15, whose distinguisher itself cites Neh 12:8) and mattaniah-9 (Neh 11:17). The chronological stretch from Zerubbabel to Nehemiah is a real wrinkle but does not yield a distinct candidate record.",
    "survivor": "mattaniah-9 survives and absorbs mattaniah-2 (taking 1 Chr 9:15); mattaniah-2's 2 Chr 20:14 attestation belongs to a different, Jehoshaphat-era Mattaniah and should be split into its own record"
  }
]

### H511

**The verse and its text-critical shape.** 1 Chronicles 6:26 (MT 6:11) reads אֶלְקָנָה בְּנוֹ אֶלְקָנָה צוֹפַי בְּנוֹ וְנַחַת בְּנוֹ, with a ketiv/qere on the second word: ketiv בְּנוֹ "his son", qere בְּנֵי "the sons of". The KJV follows the qere: "As for Elkanah: the sons of Elkanah; Zophai his son, and Nahath his son." The Hebrew has no "as for" — the first אֶלְקָנָה is either "Elkanah his son" (ketiv) or a bare casus pendens resumed by "the sons of Elkanah" (qere). A third common scholarly reading takes the second אֶלְקָנָה as dittography and reads simply "Elkanah his son, Zophai his son, Nahath his son."

**The parallel list settles who the father of Zophai is.** The descending Kohathite list of 6:22–27 has a mirror in Heman's ascending pedigree, 6:33–38: Heman ← Joel ← Samuel ← Elkanah ← Jeroham ← Eliel ← Toah ← Zuph ← Elkanah ← Mahath ← Amasai ← Elkanah ← Joel ← Azariah … Mapping the two: v.25 "sons of Elkanah: Amasai and Ahimoth" = the Elkanah (son of Joel) above Amasai and Mahath (Ahimoth = Mahath). v.26 "Zophai … Nahath" = Zuph and Toah; v.27 "Eliab, Jeroham, Elkanah" = Eliel, Jeroham, Elkanah father of Samuel. In the ascending list Zuph's father is explicitly "Elkanah, the son of Mahath, the son of Amasai, the son of Elkanah" (6:35–36). So there are two Elkanahs here three generations apart: Elkanah A (son of Joel, father of Amasai) and Elkanah B (son of Mahath, father of Zuph). BibleData's `elkanah-4` is A; `elkanah-5` is B.

**Which one is the marked word?** Under every reading of the verse the first אֶלְקָנָה of v.26 is Elkanah B:

- Ketiv: "Elkanah *his son*" — "his" is the last person named, Ahimoth/Mahath. Elkanah son of Mahath = B, exactly as 6:35 has it. This is the reading that harmonizes the two lists most cleanly.
- Qere (KJV): "Elkanah — the sons of Elkanah: Zophai his son." A casus pendens resumed by the same name refers to the same man, and that man is the father of Zophai — B per 6:35.
- Dittography: the first Elkanah *is* the father of Zophai — B.

The model's argument — that "As for Elkanah" resumes the Elkanah of v.25 — rests on the English "As for", which is a translator's gloss, and it would make Zophai a son of Elkanah A, flatly contradicting 6:35 where Zuph's father is the son of Mahath, i.e. Elkanah A's great-grandson. Even if one wanted to read v.26 as a heading that resumes A and then telescopes generations, the qere syntax makes the resumed Elkanah and the father of Zophai the same person, so the first word cannot be A while the second is B.

**BibleData's record.** `elkanah-5` says "father of Zophai [Zuph] (1CH 6:26) and son of Mahath (1CH 6:35)", its verse list includes 6:26 and 6:35, and its kinship edges (father → zuph, tohu) match. Distinguisher, verse list and kinship all agree; I trust it. (Side note not before me: `elkanah-4`'s kinship edge "son → mahath" is wrong for A and belongs to B, and its attestation at 15:23 is a different, Davidic-era doorkeeper — but that record's problems don't affect this word.) BibleData assigns both occurrences in 6:26 to `elkanah-5`, which is correct on every reading of the verse.

Verdict: the model is wrong; the source is right.

---ANSWER---
[
  {
    "word_id": 6860944,
    "verdict": "model-wrong",
    "referent": "elkanah-5",
    "confidence": "high",
    "reason": "The first Elkanah of 6:26 is the father of Zophai/Zuph on every reading (ketiv 'his son' = son of Ahimoth/Mahath; qere casus pendens resumed by the same name; or dittography), and 6:35 makes Zuph's father the Elkanah son of Mahath, three generations below the Elkanah of v.25. The model's 'as for' resumption rests on the KJV gloss and would contradict the parallel pedigree."
  }
]

### H5152

**Genesis 29:5 (word 6481805) — "Laban the son of Nahor"**

Laban's father is Bethuel: Gen 28:5 calls him "Laban son of Bethuel the Aramean," and Gen 24:29 makes him Rebekah's brother, Rebekah being "daughter of Bethuel son of Milcah, whom she bore to Nahor" (Gen 24:15, 24:24, 24:47). Bethuel himself is the last-named of Milcah's eight sons to Nahor in Gen 22:20–23. So בֶּן here is "grandson," as Hebrew בֵּן routinely is, and the Nahor meant is Abraham's brother — the head of the Aramean branch in Haran, "the city of Nahor" (Gen 24:10). The elder Nahor, Serug's son, would be Laban's great-great-grandfather and plays no role in the patriarchal narratives outside the Gen 11 / 1 Chr 1 / Luke 3 genealogical chains. Nothing in the Aramean family scenes ever reaches back past Terah.

BibleData's record `nahor` is distinguished "son of Serug (GEN 11:22)"; its verse list nonetheless carries Gen 29:5 and 31:53 alongside the four Gen 11 verses, 1 Chr 1:26 and Luke 3:34. The distinguisher is the trustworthy half — the Gen 11 chain, Chronicles and Luke are unambiguously the elder Nahor, and the two Jacob-cycle verses are the intruders. This is source-wrong; the model is right.

**Genesis 31:53 (word 6484158) — "the God of Abraham and the God of Nahor, the God of their father"**

Laban swears by the ancestral deities of both parties to the treaty: Jacob's line through Abraham and his own line through Nahor. The gloss אֱלֹהֵי אֲבִיהֶם, "the god of their father," settles the identification: Abraham and the brother Nahor share one father, Terah (Gen 11:26–27; Josh 24:2, "Terah, the father of Abraham and the father of Nahor," which BibleData itself correctly files under `nahor-2`). If the elder Nahor were meant, "their father" would have no referent — Abraham and Serug's son Nahor do not share a father, and the elder Nahor is Abraham's ancestor, not his peer. The plural verb יִשְׁפְּטוּ and the immediately following contrast — Jacob swears only "by the Fear of his father Isaac" — reinforce that two family lines are being paired, Abraham's and Nahor's, exactly the two brothers whose descendants Jacob and Laban are. Josh 24:2 adds that Terah and Nahor "served other gods," which is precisely the pantheon Laban is invoking.

Again the model is right and BibleData's verse list wrong; same record-level error as 29:5 — two Jacob-cycle verses misplaced from `nahor-2` into `nahor`. No duplicate-records issue: the two records are genuinely distinct men, and the encyclopedia's kinship edges for `nahor-2` (father of Bethuel, husband of Milcah, son of Terah) are exactly the facts that decide both verses.

---ANSWER---
[
  {
    "word_id": 6481805,
    "verdict": "source-wrong",
    "referent": "nahor-2",
    "confidence": "high",
    "reason": "Laban is son of Bethuel (Gen 28:5), and Bethuel is the son of Nahor and Milcah (Gen 22:22-23; 24:15, 24, 47), so 'son of Nahor' means grandson of Abraham's brother; BibleData's verse list contradicts its own 'son of Serug' distinguisher here."
  },
  {
    "word_id": 6484158,
    "verdict": "source-wrong",
    "referent": "nahor-2",
    "confidence": "high",
    "reason": "'The God of Abraham and the God of Nahor, the God of their father' pairs the two sons of Terah (Gen 11:26-27; Josh 24:2, which BibleData itself files under nahor-2); the elder Nahor shares no father with Abraham, so only Abraham's brother fits."
  }
]

### H564

**The underlying question.** "Immer" in the priestly material is one thing: the eponym of a priestly house. 1 Chronicles 24:14 gives the name as the sixteenth of David's twenty-four courses; from then on "the sons of Immer" (Ezra 2:37 // Neh 7:40, Ezra 10:20), "Pashhur the son of Immer the priest" (Jer 20:1), and the telescoped genealogies "…Meshillemith the son of Immer" (1 Chr 9:12 // Neh 11:13 with Meshillemoth) all point back to that house-founder. The 1,052 "children of Immer" who returned with Zerubbabel cannot be the literal sons of any one post-exilic man; they are the course of 1 Chr 24:14 counted by clan, exactly as Jedaiah, Pashhur and Harim around them are. Strong's "five Israelites" is a lexicon convention that counts contexts, not men; standard reference works treat Immer as a single family name (with Neh 3:29's father of Zadok a possible exception, which is not in dispute here).

**What BibleData actually holds.** Look at the two records side by side:

- `immer`: distinguisher says "father of Meshillemith (1CH 9:12), **priest (EZR 2:37, NEH 7:40)**"; kinship says **ancestor of hanani-3 and zebadiah-9** — the two men of Ezra 10:20; verse list holds only 1 Chr 9:12 and Neh 11:13.
- `immer-2`: distinguisher says the course-head of 1 Chr 24:14; verse list holds 1 Chr 24:14, **Ezra 2:37, Ezra 10:20, Neh 7:40**, Jer 20:1; kinship ancestor of pashhur-2 (Jer 20:1).

So all three disputed verses are claimed by *both* records — `immer` by its distinguisher (2:37, 7:40) and its kinship edges (10:20), `immer-2` by its verse list. That is the first trap in the brief, and it does not resolve by picking a half to trust: the two halves of `immer` disagree with each other, and the half that is consistent within `immer-2` (its verse list matches its distinguisher and kinship) describes the same clan eponym the `immer` prose describes. The encyclopedia has one man under two slugs and split his attestations between them by accident.

**Sonnet's reading.** Sonnet chose `immer` because the distinguisher cites the verse. That is a defensible reading of the record's own words, and its instinct that 2:37 = 7:40 = the clan of 10:20 is correct. But it is not that BibleData is *wrong* to put them under `immer-2` — the course-head is precisely who the clan is named for, and `immer-2`'s record is the internally coherent one. Neither answer is wrong about the man; they are pointing at two records for one referent.

**Which should survive.** `immer-2` should survive and absorb `immer`. Its founding reference (1 Chr 24:14) is the one that explains every other occurrence; its verse list, distinguisher and kinship agree with one another; and the material now under `immer` (1 Chr 9:12, Neh 11:13, father of Meshillemith, ancestor of Hanani and Zebadiah) folds in cleanly as further descendants of the same house. The slug name is cosmetic — if the base slug `immer` is preferred for URL stability, the merge can be done the other way with the same content; the substantive point is that one record, not two, should carry the whole clan.

**Per occurrence.**
- Ezra 2:37 — clan count of the course of Immer; both records claim it; duplicate. High confidence.
- Neh 7:40 — verbatim parallel of Ezra 2:37; same verdict. High confidence.
- Ezra 10:20 — "of the sons of Immer: Hanani and Zebadiah", priests of the same clan (the list runs Jeshua, Immer, Harim, Pashhur — the Ezra 2 priestly houses in order). `immer` holds the kinship edges to these two men while `immer-2` holds the verse; the clearest possible sign of one referent under two records. High confidence.

---ANSWER---
[
  {
    "word_id": 6845479,
    "verdict": "duplicate-records",
    "referent": "immer-2",
    "confidence": "high",
    "reason": "The 1,052 'children of Immer' are the priestly course founded in 1 Chr 24:14 counted by clan; BibleData's `immer` claims this verse in its distinguisher while `immer-2` holds it in its verse list, so the one clan eponym sits under two records.",
    "survivor": "immer-2 survives, absorbing immer"
  },
  {
    "word_id": 6849958,
    "verdict": "duplicate-records",
    "referent": "immer-2",
    "confidence": "high",
    "reason": "Hanani and Zebadiah are priests of the house of Immer (the Ezra 2 priestly clans in order); `immer` carries the kinship edges to hanani-3 and zebadiah-9 while `immer-2` carries the verse, the same man split across two records.",
    "survivor": "immer-2 survives, absorbing immer"
  },
  {
    "word_id": 6853521,
    "verdict": "duplicate-records",
    "referent": "immer-2",
    "confidence": "high",
    "reason": "Verbatim parallel of Ezra 2:37; both `immer` (distinguisher) and `immer-2` (verse list) claim it for the same clan eponym of 1 Chr 24:14.",
    "survivor": "immer-2 survives, absorbing immer"
  }
]

### H5662

#### Reasoning

**The verse.** 2 Chronicles 17:7 is dated to Jehoshaphat's third year (c. 870 BCE). The Obadiah here is one of the king's שָׂרִים — "his princes", lay officials of Judah — and the Chronicler is careful to keep the categories apart: v. 7 lists five princes, v. 8 lists the Levites and then the two priests separately. So the referent is a Judahite layman of Jehoshaphat's court. This is Strong's #10, "a prince of Judah in the time of king Jehoshaphat".

**BibleData's verse-list answer, `obadiah-5`.** This record is the son of Shemaiah in 1 Chronicles 9:16, a Levite (descendant of Jeduthun) living among the Netophathite villages, and BibleData equates him with Abda of Nehemiah 11:17. Both of those lists describe the post-exilic settlement of Jerusalem — roughly four centuries after Jehoshaphat. He is also a Levite, whereas the Chronicler puts the Levites of this mission in v. 8, not among the princes of v. 7. Chronology and class both exclude him. This is a plain error in the verse list; nothing in the record's own distinguisher ("son of Shemaiah (1CH 9:16)") claims 2 Chr 17:7.

**The model's answer, `obadiah`.** The model relied on the record's distinguisher, which does say "sent by Jehoshaphat to teach (2CH 17:7)". But the record's verse list is 1 Kings 18:3–16 only, and that man is Ahab's palace steward (אֲשֶׁר עַל־הַבָּיִת) in the northern kingdom, who hid the prophets from Jezebel. He is contemporary with Jehoshaphat, but he serves a different king in a different kingdom; there is no textual link between Samaria's steward and a prince of Judah dispatched to teach in Judah's cities, and Strong's rightly counts them as two men (#4 and #10). The distinguisher's clause is a conflation BibleData added, contradicted by its own verse list; here I trust the verse list (1 Kings 18 only) over the distinguisher, because the prose clause has no support in either passage.

**Result.** The verse list points to a post-exilic Levite; the model, following a faulty distinguisher, points to Ahab's steward. Neither fits. The prince of Jehoshaphat has no record in the BibleData list at all — a thirteenth Obadiah (Strong's #10) that the encyclopedia is missing. Both are wrong; the correct referent is a record that should exist but does not.

---ANSWER---
[
  {
    "word_id": 6881451,
    "verdict": "both-wrong",
    "referent": "none",
    "confidence": "high",
    "reason": "The Obadiah of 2 Chr 17:7 is one of Jehoshaphat's Judahite princes (c. 870 BCE), distinguished from the Levites of v. 8; obadiah-5 is a post-exilic Levite (1 Chr 9:16 / Neh 11:17) and obadiah is Ahab's steward in Israel (1 Kgs 18), whose distinguisher clause about 2 Chr 17:7 is an unsupported conflation. No BibleData record exists for this man (Strong's #10, prince of Judah under Jehoshaphat)."
  }
]

### H568

**Ezra 7:3, word 6847735 — the Amariah in Ezra's pedigree**

The full priestly line in 1 Chronicles 6:4-15 (MT 5:30-41) runs, from Aaron downward: Eleazar, Phinehas, Abishua, Bukki, Uzzi, Zerahiah, Meraioth, **Amariah (6:7)**, Ahitub, Zadok, Ahimaaz, Azariah, Johanan, Azariah (the one who served in Solomon's temple), **Amariah (6:11)**, Ahitub, Zadok, Shallum, Hilkiah, Azariah, Seraiah, Jehozadak. There are two Amariahs, and they are distinguished by their fathers: the first is *son of Meraioth*, the second is *son of Azariah*.

Ezra 7:1-5 gives Ezra's own line upward: Seraiah, Azariah, Hilkiah, Shallum, Zadok, Ahitub, **Amariah, Azariah, Meraioth**, Zerahiah, Uzzi, Bukki, Abishua, Phinehas, Eleazar, Aaron. Two things in the Hebrew of 7:3 settle the matter:

1. The marked word is followed by בֶן עֲזַרְיָה — this Amariah is explicitly *son of Azariah*. The only Amariah son of Azariah in the genealogies is the one of 1 Chronicles 6:11 (`amariah-2`). The first Amariah (`amariah`) is son of Meraioth directly, with no Azariah between them.
2. The segment above him — Shallum, Zadok, Ahitub — matches the *second* Ahitub–Zadok pair (6:11-12), whose Zadok fathers Shallum, not the first Zadok whose son is Ahimaaz (6:8).

So Ezra's list is the well-known abridgment: it names the Azariah of 6:10-11 and then jumps straight to Meraioth, dropping the six generations between (Johanan, Azariah, Ahimaaz, Zadok, Ahitub, Amariah), very plausibly by homoioteleuton on the recurring Amariah–Ahitub–Zadok / Azariah names. That omission is exactly why "Meraioth" appears in the same verse, and I suspect that is what misled BibleData: its verse-level list saw "Amariah … Meraioth" in Ezra 7:3 and attached the verse to the record distinguished as "son of Meraioth." But the intervening "son of Azariah" contradicts that assignment on BibleData's own terms — its `amariah-2` record is defined precisely as "son of Azariah and father of Ahitub (1CH 6:11)," and the Ezra verse repeats both of those edges (Ahitub above, Azariah below).

Is this a duplicate-records case? Some critics have suggested that the second Amariah–Ahitub–Zadok triad in 1 Chronicles 6:11-12 is a dittography of the first, but BibleData does not treat them as one man, and it has good reason not to: `amariah-2` also carries 2 Chronicles 19:11, Jehoshaphat's chief priest Amariah, which fits the ninth-century position of the second Amariah and cannot fit the pre-monarchic first one. The two records are chronologically distinct, and Ezra's Amariah belongs to the later one.

I do not think this is contested. The alternative — that Ezra meant the first Amariah and "son of Azariah" is a scribal intrusion — has no textual support; Ezra 7:3 reads the same in all witnesses, and the surrounding names (Shallum, Zadok, Ahitub) point to the later triad independently of the Azariah link. The model's answer stands; BibleData's verse list is wrong here.

---ANSWER---
[
  {
    "word_id": 6847735,
    "verdict": "source-wrong",
    "referent": "amariah-2",
    "confidence": "high",
    "reason": "Ezra 7:3 calls this Amariah 'son of Azariah' and places him under Ahitub–Zadok–Shallum, which matches the second Amariah of 1 Chr 6:11-12 (BibleData's own amariah-2, 'son of Azariah and father of Ahitub'), not the first Amariah son of Meraioth; the Meraioth in the same verse results from Ezra's abridgment that skips six generations."
  }
]

### H5711

The five disputed words are all one woman, and the question is only which of BibleData's two records she belongs under.

**The text.** Genesis 36:2 names her outright: עָדָה בַּת אֵילוֹן הַחִתִּי, "Adah daughter of Elon the Hittite," one of Esau's wives from the daughters of Canaan. 36:4 makes her the mother of Eliphaz; 36:10 calls her אֵשֶׁת עֵשָׂו, "wife of Esau," and again mother of Eliphaz; 36:12 closes the Eliphaz clan list with "these are the sons of Adah, Esau's wife"; 36:16 closes the Eliphaz chieftain list with "these are the sons of Adah." Nothing in any of these five verses can be Lamech's wife of Genesis 4 — that Adah is the mother of Jabal and Jubal, generations before the Flood, and the Genesis 4 occurrences (4:19, 20, 23) are undisputed.

**BibleData's own evidence.** The `adah` record's distinguisher is "wife of Lamech (GEN 4:19)" and its kinship edges are exclusively Lamech, Jabal, Jubal. Its verse list nonetheless claims Genesis 36:2, 4, 10, 12, 16. That is the distinguisher/verse-list contradiction the task warns about, and here the distinguisher and kinship are plainly the trustworthy half: they agree with each other and with the Hebrew, while the verse list conflates two women who share nothing but a name. Conversely, the `basemath` record's distinguisher is "daughter of Elon the Hittite, and wife of Esau," it carries the name label **Adah=H5711** alongside Basemath=H1315, and its kinship edges are daughter→elon, mother→eliphaz, wife→esau — exactly the woman of Genesis 36:2–16. Its verse list, however, holds only Genesis 26:34, where she is called Basemath; the Adah label it carries is attested nowhere in its own verse list. So BibleData clearly *intended* the Genesis 36 Adah to live under `basemath` (that is why the record carries the H5711 label and the Eliphaz edge) and simply filed the verses under the wrong homonym.

**Is the Adah=Basemath harmonisation itself sound?** The Genesis 36 list and Genesis 26:34/28:9 disagree (26:34 Basemath daughter of Elon the Hittite ≈ 36:2 Adah daughter of Elon the Hittite; 28:9 Mahalath daughter of Ishmael ≈ 36:3 Basemath daughter of Ishmael). One could argue for a separate record for the Genesis 36 Adah rather than folding her into the Genesis 26 Basemath, which would make this "both-wrong." But that is a choice about how to model a textual variant, not about who the referent is: both passages describe Esau's Hittite wife, daughter of Elon, and BibleData has already made the modelling decision by giving `basemath` the Adah label and the Eliphaz kinship. Within the record set as it exists, `basemath` is the only record that fits, and it fits perfectly.

**Verse-level ambiguity.** None here: each verse has exactly one H5711 occurrence, and in 36:4, 36:10 and 36:12 the neighbouring Basemath (H1315, בָּשְׂמַת) is a different lexeme and a different wife (mother of Reuel), so there is no risk of the marked word being confused with her.

**Verdict.** All five: source-wrong. The reading model's `basemath` is correct; BibleData's `adah` is contradicted by its own distinguisher and kinship. The fix is to move Genesis 36:2, 4, 10, 12, 16 from `adah`'s verse list to `basemath`'s (where the Adah=H5711 label is already waiting for them).

---ANSWER---
[
  {
    "word_id": 6486326,
    "verdict": "source-wrong",
    "referent": "basemath",
    "confidence": "high",
    "reason": "The Hebrew names her 'Adah daughter of Elon the Hittite', Esau's wife; BibleData's own `basemath` record carries the label Adah=H5711, the distinguisher 'daughter of Elon the Hittite, wife of Esau', and kinship daughter→elon/wife→esau, while `adah`'s distinguisher and kinship are Lamech's antediluvian wife. The `adah` verse list contradicts its own record."
  },
  {
    "word_id": 6486349,
    "verdict": "source-wrong",
    "referent": "basemath",
    "confidence": "high",
    "reason": "This Adah bears Eliphaz to Esau; `basemath` has the edge mother→eliphaz and wife→esau, whereas `adah` is mother of Jabal and Jubal and wife of Lamech. Only the verse list of `adah` claims this verse, against its distinguisher and kinship."
  },
  {
    "word_id": 6486461,
    "verdict": "source-wrong",
    "referent": "basemath",
    "confidence": "high",
    "reason": "'Eliphaz son of Adah the wife of Esau' matches `basemath`'s kinship (mother→eliphaz, wife→esau) exactly and cannot be Lamech's wife. The neighbouring Bashemath (H1315, mother of Reuel) is a different lexeme and a different wife, so there is no word-level ambiguity."
  },
  {
    "word_id": 6486496,
    "verdict": "source-wrong",
    "referent": "basemath",
    "confidence": "high",
    "reason": "'These were the sons of Adah, Esau's wife' closes the Eliphaz clan list; the woman is Esau's Hittite wife, held by BibleData under `basemath` with the Adah=H5711 label. Assigning it to Lamech's wife contradicts `adah`'s own distinguisher and kinship."
  },
  {
    "word_id": 6486568,
    "verdict": "source-wrong",
    "referent": "basemath",
    "confidence": "high",
    "reason": "'These were the sons of Adah' sums up the chieftains descended from Eliphaz, Esau's firstborn by Adah; the referent is Esau's wife, i.e. BibleData's `basemath` record, not the antediluvian Adah of Genesis 4 that the `adah` record's distinguisher and kinship describe."
  }
]

### H5838

#### Reasoning

A preliminary point that governs three of the four cases: the record `azariah-7` is badly described by its own distinguisher. Its verse list (1 Chr 6:13, 6:14, 9:11; Ezra 7:1) is internally coherent — every one of those verses is the son of Hilkiah, father of Seraiah, grandfather of Jehozadak "who went into captivity" (1 Chr 6:15), the priest of Josiah's generation. But the distinguisher tacks on "priest (EZR 2:2, NEH 7:7, NEH 12:1), explained the law to the people (NEH 8:7)" — verses that belong to the post‑exilic returnee (whom BibleData itself holds as `seraiah-6`) and to a Levite of 445 BC. A man whose son Seraiah was executed by Nebuchadnezzar (2 Ki 25:18‑21) cannot be doing anything in Ezra‑Nehemiah. I trust `azariah-7`'s verse list and reject its distinguisher; the reading model trusted the distinguisher, which is the root of its errors in cases 2–4.

**1. 2 Chr 31:13 (word 6889628).** Three verses earlier, 2 Chr 31:10 names "Azariah the chief priest of the house of Zadok" answering Hezekiah; 31:13 then closes the list of overseers with "at the commandment of Hezekiah the king and Azariah the ruler (נגיד) of the house of God." Same narrative, same king, same office — this is the man of 31:10, i.e. `azariah-15`. BibleData's `azariah-7` seems to have been triggered by the identical title "ruler of the house of God" in 1 Chr 9:11 (Azariah son of Hilkiah), but that Azariah belongs a century later, in Josiah's generation. BibleData's own kinship edges (father of Seraiah, whose son Jehozadak went into exile) contradict placing him under Hezekiah. Source‑wrong; the model is right.

**2. Neh 8:7 (word 6854130).** The Azariah here is among the thirteen who, with the Levites, taught the law under Ezra in 445 BC. The model's `azariah-7` is chronologically impossible for the reason above; its "high confidence" rests entirely on the untrustworthy distinguisher. BibleData's `azariah-16` (Azariah son of Maaseiah son of Ananiah, who repaired the wall beside his house, Neh 3:23‑24) is at least the right century, and nothing in the text contradicts it — but nothing supports it either: the wall‑builder is not said to be a Levite, and Strong's itself keeps the teacher (#13) and the wall‑builder (#15) as separate men. So the model is definitely wrong; BibleData's answer is the only listed record that fits but is an unproven identification. I classify it model‑wrong with medium confidence, noting that a stricter encyclopedia would give the Neh 8:7 teacher his own record.

**3. Neh 10:2 (word 6855504).** Neh 10:1‑8 is the list of *priests* who sealed the covenant: "Seraiah, Azariah, Jeremiah, Pashur, Amariah…" — largely the priestly houses of Neh 12:1‑7 (where the third name is Ezra, of which Azariah may be the full form). BibleData's `azariah-15` is Hezekiah's chief priest (2 Chr 31:10), ~250 years too early — source‑wrong by its own evidence. The model's `azariah-7` is the pre‑exilic son of Hilkiah — also impossible. Nor can he be `seraiah-6` (the returnee "called Azariah in Neh 7:7"), because Seraiah stands beside him in the same verse as a different signatory. No record in the list fits; this is Strong's #9, a distinct post‑exilic priest who needs his own record. Both wrong; referent none.

**4. Neh 12:33 (word 6856923).** In the dedication procession, after "Hoshaiah and half the princes of Judah" come "Azariah, Ezra, Meshullam, Judah, Benjamin, Shemaiah, Jeremiah," and only at v.35 do "the priests' sons with trumpets" begin. The model's `azariah-7` is again the pre‑exilic priest — wrong. BibleData's `seraiah-6` is the head who returned with Zerubbabel in 538; by BibleData's own kinship his son Meraiah was already head of the house of Seraiah "in the days of Joiakim… and of Nehemiah" (Neh 12:12, 26), so the father marching in 445 is implausible, and the Seraiah=Azariah equation itself rests only on the Ezra 2:2/Neh 7:7 variant. Both answers fail. Who he actually is remains open: the lay setting (princes of Judah) favours the wall‑builder `azariah-16`, whereas the run "Azariah, Ezra, Meshullam… Shemaiah, Jeremiah" echoes the priestly house‑names of Neh 10:2‑8 and 12:1‑13 and would make him the Neh 10:2 priest (Strong's #17 = #9, no record). I cannot settle that; the undecided question is whether the names in vv.33‑34 are lay princes or priestly houses.

---ANSWER---
[
  {
    "word_id": 6889628,
    "verdict": "source-wrong",
    "referent": "azariah-15",
    "confidence": "high",
    "reason": "The 'ruler of the house of God' acting with Hezekiah in 31:13 is the chief priest Azariah of 31:10 three verses earlier; azariah-7 (son of Hilkiah, grandfather of the exiled Jehozadak) belongs to Josiah's generation and BibleData's own kinship contradicts placing him under Hezekiah."
  },
  {
    "word_id": 6854130,
    "verdict": "model-wrong",
    "referent": "azariah-16",
    "confidence": "medium",
    "reason": "azariah-7 is the pre-exilic son of Hilkiah and cannot teach the law in 445 BC; the model trusted a distinguisher whose Ezra-Nehemiah citations are a conflation. azariah-16 is the only listed record of the right period, though its identification of the Levite teacher with the wall-builder son of Maaseiah is unproven and would better be a separate record."
  },
  {
    "word_id": 6855504,
    "verdict": "both-wrong",
    "referent": "none",
    "confidence": "high",
    "reason": "The sealing priests of Neh 10:2 are post-exilic; azariah-15 is Hezekiah's chief priest and azariah-7 is the pre-exilic son of Hilkiah, both centuries too early, and seraiah-6 is excluded because Seraiah is listed beside him in the same verse. This is a distinct post-exilic priest (Strong's #9) with no record in the list."
  },
  {
    "word_id": 6856923,
    "verdict": "both-wrong",
    "referent": "undecided",
    "confidence": "medium",
    "reason": "azariah-7 is pre-exilic; seraiah-6 returned in 538 and BibleData's own record makes his son Meraiah head of the house in Nehemiah's day, so the father marching in 445 is implausible. Whether this Azariah is the lay wall-builder azariah-16 (he walks with the princes of Judah) or the Neh 10:2 priest (the names echo priestly houses) the text does not settle."
  }
]

### H6034

**The evidence**

1 Chronicles 1:41a, בְּנֵי עֲנָה דִּישׁוֹן, is a verbatim abridgement of Genesis 36:25a, וְאֵלֶּה הָיוּ בְנֵי־עֲנָה דִּשֹׁן. The Chronicler is copying Genesis 36:20–28 in order: 1:38 gives the seven sons of Seir (Lotan, Shobal, Zibeon, Anah, Dishon, Ezer, Dishan), and 1:39–42 then gives each one's sons in that same order — Lotan (39), Shobal (40a), Zibeon (40b), **Anah (41a)**, Dishon (41b), Ezer (42a), Dishan (42b). So the Anah of 1:41 is, without any doubt, the same man as the Anah of Genesis 36:25, and both stand in the slot of the fourth son of Seir.

BibleData puts Genesis 36:25 under `beeri` and 1 Chronicles 1:41 under `anah`. Its kinship graph says `beeri` is father of `dishon-2`. Its verse list for 1 Chr 1:41 — "sons of Anah: Dishon" — says `anah`. Whichever record is right, the encyclopedia has split one sentence between two records; that contradiction is real regardless of how the referent question resolves.

**Are the two records two men?**

`anah` = Anah son of Seir (36:20, 36:29, 1 Chr 1:38). `beeri` (setting aside the Beeri-the-Hittite fusion) = Anah son of Zibeon (36:2, 36:14, 36:24), father of Aholibamah and Dishon. Strong's lexicon counts them as two Edomites, and that is the traditional split. But the text itself joins them: Genesis 36:25, sitting in the son-of-Seir slot, names Aholibamah as Anah's daughter, and 36:2 and 36:14 make that same Aholibamah daughter of Anah, (grand)daughter of Zibeon. The chiefs of 36:29 (Lotan, Shobal, Zibeon, Anah) are the sons of Seir of 36:20, and Anah's place there matches his place in 36:24 as Zibeon's son found in the wilderness. The Anah who fathered Dishon and Aholibamah is the Anah who is counted among Seir's "sons" — the list treats a grandson via Zibeon as a clan head in his own right, exactly as it does with Dishon (son of Seir in v.21, son of Anah in v.25). One man; the doubled genealogy is the text's, not two persons.

So the model's `beeri` (via Gen 36:25 and the kinship edge to Dishon) and BibleData's `anah` (via the 1:38 → 1:41 structure) are both pointing at the same Horite: Anah, father of Dishon and Aholibamah, listed among Seir's sons and chiefs. That is a duplicate-records case, not a source-wrong or model-wrong one.

**Which record should survive**

`anah` should survive: it is a clean Anah record. `beeri` is a harmonizing composite — Beeri the Hittite of Genesis 26:34 equated with Anah on the rabbinic identification of Judith with Aholibamah, which the Hebrew text nowhere makes (Beeri is a Hittite, Anah a Horite; Judith and Aholibamah are different names). The seven Anah-labelled attestations of `beeri` (Gen 36:2, 14, 18, 24, 25; 1 Chr 1:40) and its edges to `dishon-2`, `judith`-as-Aholibamah, and `zibeon-2` belong on `anah`; Genesis 26:34 should be left as a bare Beeri-the-Hittite record. I trust the verse lists and kinship edges over the `beeri` distinguisher, because the distinguisher's "also called Anah" is an inference, whereas the verse lists faithfully track where the name עֲנָה occurs.

My confidence is medium rather than high only because the one-Anah reading, though I think the text requires it, is contested in the reference tradition (Strong's counts two). What is not in doubt: Gen 36:25 and 1 Chr 1:41 name the same man, and BibleData must hold them under one record.

---ANSWER---
[
  {
    "word_id": 6858439,
    "verdict": "duplicate-records",
    "referent": "anah",
    "confidence": "medium",
    "reason": "1 Chr 1:41 'sons of Anah: Dishon' copies Gen 36:25 in the fourth-son-of-Seir slot of 1:38, and Gen 36:25/36:2/36:14 make this Dishon-and-Aholibamah-fathering Anah the son of Zibeon counted among Seir's sons — one man that BibleData splits between `anah` and the composite `beeri`.",
    "survivor": "anah survives, absorbing the Anah-labelled verses and Dishon/Aholibamah/Zibeon edges of beeri; Gen 26:34 should remain as a separate Beeri-the-Hittite record, since the Beeri=Anah equation is rabbinic harmonization, not the Hebrew text"
  }
]

### H6126

**Reasoning**

The three records under H6126 fall into clear buckets. `akkub` is the Davidic descendant of 1 Chr 3:24 — irrelevant here. `akkub-2` is the gatekeeper (1 Chr 9:17 // Neh 11:19; family "sons of Akkub" in Ezra 2:42 // Neh 7:45; individual porter in Neh 12:25). `akkub-3` is a head of a Nethinim family, "sons of Akkub", in Ezra 2:45.

**Why the model's `akkub-3` is wrong.** The model matched a string, not the evidence. The `akkub-3` record's distinguisher says "explained the law to the people (NEH 8:7)" but its verse list holds only Ezra 2:45 — this is exactly the distinguisher/verse-list contradiction I was warned about, and here the verse list is the half to trust. Ezra 2 separates the returnees by class: Levites (2:40), singers (2:41), gatekeepers (2:42), and then the Nethinim (2:43-54), among whom the "sons of Akkub" (2:45) stand. Nethinim are temple servants, not Levites. Neh 8:7 explicitly frames its thirteen named men as Levites (וְהַלְוִיִּם … מְבִינִים אֶת־הָעָם לַתּוֹרָה), and the companions in the list — Jeshua, Bani, Sherebiah, Hodijah, Kelita, Pelaiah, Hanan — reappear as Levites in Neh 9:4-5 and 10:9-13. A Nethinim ancestor whose descendants returned under Zerubbabel (c. 538) cannot be a living teacher in Nehemiah's day (c. 444), and a Nethinim family head has no business expounding Torah. Note too that the parallel list in Neh 7:48 drops this Akkub altogether. The "(NEH 8:7)" in the distinguisher looks like a conflation of Strong's entries 3 (Nethinim head) and 5 (Levite expounder), and it should be struck from the record.

**Is BibleData's `akkub-2` right?** This is the residual question. In favour: the gatekeeper Akkub is a contemporary of Nehemiah (Neh 11:19; 12:25), in the same city; Neh 12:25 places the porters among the Levites, and 1 Chr 9:17-26 reckons the gatekeepers as Korahite Levites; and several names in the Neh 8:7 list function as house-names rather than individuals (Jeshua, Bani, Sherebiah, Kelita are Levitical houses in Ezra 8:18-19; 10:23), so "Akkub" there could equally be the gatekeeper house of Ezra 2:42, which BibleData already files under `akkub-2`. It is the only record in the list that can fit at all. Against: the lexica (Strong's, BDB) treat the law-expounder as a distinct Levite (Strong's #5 vs #4/#6), gatekeepers are nowhere else shown teaching, and Akkub is absent from the Levitical covenant-sealers of Neh 10 where most of his Neh 8:7 colleagues appear. Nothing in the text positively identifies or positively distinguishes the two.

So: the model's answer is certainly wrong; BibleData's is the only viable record and the economical reading (same name, same class, same generation, same city), but a distinct Levite for whom BibleData holds no record remains possible. I give `akkub-2` at medium confidence and flag that alternative explicitly rather than upgrade it to "none".

---ANSWER---
[
  {
    "word_id": 6854125,
    "verdict": "model-wrong",
    "referent": "akkub-2",
    "confidence": "medium",
    "reason": "akkub-3 is a Nethinim family head of the 538 return (Ezra 2:45, a class distinct from Levites, and dropped in Neh 7:48), so it cannot be a Levite teaching Torah in Nehemiah's day; the model followed the record's erroneous '(NEH 8:7)' distinguisher, which its own verse list contradicts. The gatekeeper akkub-2 is a Levite contemporary of Nehemiah (Neh 11:19; 12:25) and the only record that fits, though the lexica's view that Neh 8:7 names a separate Levite (Strong's #5, no BibleData record) cannot be excluded."
  }
]

### H623

#### Reasoning

**Is `asaph-3` a real second Levite Asaph?** That is the question underneath all three disputes, so I take it first.

BibleData's `asaph-3` is built from 1 Chr 9:15 ("Mattaniah the son of Micah, the son of Zichri, the son of Asaph"), Ezra 2:41 / Neh 7:44 ("the singers: the children of Asaph, 128 [148]"), and Neh 11:17, 22. Strong's, by contrast, knows only three Asaphs, and the second of them is "a son of Berechiah, chief Levite musician under David" with "the family of the first" folded in. The text supports Strong's, not the split:

1. **The Mattaniah line.** 1 Chr 9:15 and Neh 11:17 are the same pedigree (Zichri/זכרי ≈ Zabdi/זבדי, a common graphic variant), and Zichri is almost certainly Zaccur/זכור, whom 1 Chr 25:2 names as a literal son of the Davidic Asaph. So the "Asaph" at the head of Mattaniah's line is the son of Berechiah.
2. **BibleData contradicts itself here.** Its own kinship for `asaph-2` records `ancestor -> mattaniah-4` and `ancestor -> zechariah-14` — yet the very verse that makes Mattaniah a descendant of Asaph (Neh 11:17) sits in `asaph-3`'s verse list, and 2 Chr 29:13 ("of the sons of Asaph, Zechariah and Mattaniah") sits in `asaph-2`'s. The encyclopedia has recognised the Mattaniah line as Davidic-Asaph descent and *also* filed it under a separate Asaph. That is the signature of a duplicate record, not of two men.
3. **"Sons of Asaph" is a guild term.** Every post-exilic occurrence (Ezra 2:41; 3:10; Neh 7:44; 11:22) uses בני אסף for the temple singers, whose office is explicitly grounded "after the ordinance of David" (Ezra 3:10) and "according to the commandment of David, and Asaph, and Heman, and Jeduthun" (2 Chr 35:15). The guild is named for its founder; there is no verse anywhere introducing a later Asaph as a fresh head of the singers.

So I trust neither `asaph-3`'s distinguisher nor its verse list as evidence of a second person; the distinguisher merely describes the guild-ancestor role of the same Berechiah's son. `asaph-3` should be absorbed into `asaph-2`.

**Word 6892513 — 2 Chr 35:15, "commandment of David, and <<Asaph>>, and Heman, and Jeduthun the king's seer."** This is the tightest of the three. The name stands in the fixed Davidic triad Asaph–Heman–Jeduthun (1 Chr 25:1, 6; 2 Chr 5:12; 29:13–14), David's own contemporary and appointee. Even inside BibleData's own split this word belongs to `asaph-2`, whose distinguisher is "son of Berechiah, chosen first by lot (1 Chr 25:9)". BibleData's `asaph-3` here is most likely an artefact of verse-level granularity — it probably meant `asaph-3` for בני אסף earlier in the verse and had no way to mark the second occurrence differently. The reading model is right; BibleData's answer is right about the man only because `asaph-3` is the same man under another slug. Duplicate-records, high confidence.

**Word 6892506 — 2 Chr 35:15, "the singers the sons of <<Asaph>>."** Guild usage, and the same verse immediately grounds the guild in the Davidic Asaph's own commandment. This is exactly the kind of occurrence BibleData filed under `asaph-3`, and exactly why `asaph-3` is redundant. Referent: `asaph-2` as guild eponym. Duplicate-records, high confidence.

**Word 6846158 — Ezra 3:10, "the Levites the sons of <<Asaph>> with cymbals … after the ordinance of David king of Israel."** Same pattern: בני אסף as the singers' guild, with the Davidic ordinance named in the same breath. The model's medium confidence undersells it; the only way this is not the Davidic Asaph is if a second singer-Asaph existed, and no verse attests one. Duplicate-records, high confidence.

**Housekeeping, not in dispute:** 1 Chr 26:1 ("Meshelemiah the son of Kore, of the sons of Asaph" — a Korahite gatekeeper line, where Asaph is probably Ebiasaph of 1 Chr 9:19, a different man) and 2 Chr 20:14 ("Jahaziel … of the sons of Asaph", the singer guild → `asaph-2`) are currently unassigned in BibleData. If records are merged, 2 Chr 20:14 should go to `asaph-2` and 1 Chr 26:1 needs its own consideration rather than falling into the Asaph merge.

---ANSWER---
[
  {
    "word_id": 6892506,
    "verdict": "duplicate-records",
    "referent": "asaph-2",
    "confidence": "high",
    "reason": "\"The singers the sons of Asaph\" is the guild named for the Davidic Asaph, and the same verse grounds that guild in his own commandment; BibleData's asaph-3 is the same man filed a second time as guild-eponym, as its own kinship (asaph-2 ancestor of mattaniah-4, whose verse Neh 11:17 sits under asaph-3) shows.",
    "survivor": "asaph-2 survives and absorbs asaph-3"
  },
  {
    "word_id": 6892513,
    "verdict": "duplicate-records",
    "referent": "asaph-2",
    "confidence": "high",
    "reason": "The name stands in the fixed Davidic triad Asaph–Heman–Jeduthun (1 Chr 25:1; 2 Chr 5:12; 29:13), David's contemporary, which even BibleData's own asaph-2 distinguisher describes; the asaph-3 label here is a verse-level artefact of the duplicate record.",
    "survivor": "asaph-2 survives and absorbs asaph-3"
  },
  {
    "word_id": 6846158,
    "verdict": "duplicate-records",
    "referent": "asaph-2",
    "confidence": "high",
    "reason": "\"The Levites the sons of Asaph\" acting \"after the ordinance of David\" is the singers' guild of the Davidic Asaph; no verse anywhere introduces a second singer-Asaph, so asaph-3 is a redundant record for the same man.",
    "survivor": "asaph-2 survives and absorbs asaph-3"
  }
]

### H6659

**Preliminaries: what the three candidate records actually are.**

The priestly line in 1 Chr 6:4-15 (MT 5:30-41) contains two Zadoks: Ahitub → **Zadok** → Ahimaaz (6:8), and, six generations later, a second Ahitub → **Zadok** → Shallum → Hilkiah → Azariah → Seraiah → Jehozadak (6:12-14). Ezra 7:2 repeats the second sequence (Seraiah – Azariah – Hilkiah – Shallum – Zadok – Ahitub). 1 Chr 9:11 and Neh 11:11 give the same later line with Meshullam for Shallum (both from שׁלם) and Meraioth inserted between Ahitub and Zadok: Azariah/Seraiah – Hilkiah – Meshullam – Zadok – Meraioth – Ahitub. So:

- `zadok` = David's high priest, son of the first Ahitub, father of Ahimaaz — the man of Samuel–Kings and 1 Chr 6:8, 12:28, 15–29. BibleData's own record also carries 2 Chr 31:10 "the house of Zadok", i.e. it already treats the *line* as belonging to this man.
- `zadok-3` = the later genealogical Zadok of 1 Chr 6:12 / Ezra 7:2 / 1 Chr 9:11, ancestor of Hilkiah and the last pre-exilic high priests. Its distinguisher ("(grand)son of Ahitub and father of Shallum, 1 Chr 6:12") and kinship (son of meraioth-2, grandson of ahitub-3, father of shallum-6) are coherent with that; they are the reliable half of the record.
- `zadok-6` = "son of Meraioth (Neh 11:11)", father of meshullam-15. Neh 11:11 is the verbatim parallel of 1 Chr 9:11. It is the same man as `zadok-3`, held under a second record (with even Meraioth duplicated as meraioth-2 / meraioth-3). Its other verse, Neh 13:13 (Zadok the scribe made treasurer), is a different Nehemiah-era man altogether and does not belong to either genealogical record, but that verse is not before me.

`zadok-3`'s verse list, however, contradicts its distinguisher: it sweeps in 1 Chr 15:11, 16:39 and the four Ezekiel "sons/seed of Zadok" texts. A Zadok who is grandfather-line to Hilkiah cannot be the priest David summons, and Ezekiel's Zadokite eponym is not this obscure genealogical name. I trust the distinguisher and kinship over the verse list for `zadok-3`.

**1 Chr 9:11 (word 6862917).** "Azariah son of Hilkiah son of Meshullam son of Zadok son of Meraioth son of Ahitub." This is exactly Neh 11:11's chain, and (allowing Shallum = Meshullam and the added Meraioth) 1 Chr 6:12's and Ezra 7:2's. BibleData says `zadok-3`; the model says `zadok-6`. Both name the same man; the encyclopedia simply holds him twice. `zadok-3` should survive: it is anchored to 1 Chr 6:12 and Ezra 7:2 and already holds 9:11; `zadok-6` contributes only Neh 11:11 (to be merged) and Neh 13:13 (which should be split off to a new record for the scribe-treasurer, Strong's no. 7).

**1 Chr 15:11 (word 6865955).** "David called for Zadok and Abiathar the priests." Zadok paired with Abiathar under David is the high priest of 2 Sam 8:17, 15:24ff., without any possible ambiguity. BibleData's `zadok-3` is wrong by its own distinguisher; the model is right.

**1 Chr 16:39 (word 6866885).** "Zadok the priest and his brethren the priests before the tabernacle of the LORD at Gibeon" — David's appointment after bringing the ark to Jerusalem. Same man, same verdict: `zadok`.

**Ezekiel 40:46, 43:19, 44:15, 48:11 (words 6752759, 6754325, 6754843, 6757161).** "Sons of Zadok" / "seed of Zadok" is Ezekiel's name for the legitimate Jerusalem priesthood — the line that replaced Abiathar's house under Solomon (1 Kgs 2:27, 35) and "kept the charge of my sanctuary when Israel went astray" (44:15). The eponym is the high priest of David and Solomon, exactly as 2 Chr 31:10 "the house of Zadok" (which BibleData itself files under `zadok`). One could in principle ask whether a priest-prophet of the exile might mean the nearer Zadok of 1 Chr 6:12, but nothing in the text or the tradition supports that; the Zadokite priesthood is universally traced to the Davidic high priest, and `zadok-3` is a bare genealogical name with no founding role. Source-wrong in all four; referent `zadok`.

---ANSWER---
[
  {
    "word_id": 6862917,
    "verdict": "duplicate-records",
    "referent": "zadok-3",
    "confidence": "high",
    "reason": "1 Chr 9:11's chain (Hilkiah–Meshullam–Zadok–Meraioth–Ahitub) is verbatim Neh 11:11 and matches 1 Chr 6:12 / Ezra 7:2 with Shallum=Meshullam; zadok-3 and zadok-6 are the same later Zadok held twice.",
    "survivor": "zadok-3 survives and absorbs zadok-6 (Neh 11:11 merges in; zadok-6's Neh 13:13, the scribe-treasurer, is a different man and should get its own record)"
  },
  {
    "word_id": 6865955,
    "verdict": "source-wrong",
    "referent": "zadok",
    "confidence": "high",
    "reason": "David summons 'Zadok and Abiathar the priests'; this is the Davidic high priest of 2 Sam 8:17 and 15:24, not the post-Davidic genealogical Zadok that zadok-3's own distinguisher describes."
  },
  {
    "word_id": 6866885,
    "verdict": "source-wrong",
    "referent": "zadok",
    "confidence": "high",
    "reason": "Zadok the priest stationed by David before the tabernacle at Gibeon is David's high priest; zadok-3 (ancestor of Hilkiah) lived generations later."
  },
  {
    "word_id": 6752759,
    "verdict": "source-wrong",
    "referent": "zadok",
    "confidence": "high",
    "reason": "'Sons of Zadok' is the Zadokite priestly line founded by Solomon's high priest (1 Kgs 2:35), the same 'house of Zadok' BibleData itself files under zadok at 2 Chr 31:10."
  },
  {
    "word_id": 6754325,
    "verdict": "source-wrong",
    "referent": "zadok",
    "confidence": "high",
    "reason": "'Seed of Zadok' names the legitimate priesthood descended from David's high priest; zadok-3 is a bare genealogical name with no founding role."
  },
  {
    "word_id": 6754843,
    "verdict": "source-wrong",
    "referent": "zadok",
    "confidence": "high",
    "reason": "The priests 'the sons of Zadok that kept the charge of my sanctuary' are the Jerusalem line established under Solomon in place of Abiathar's house; the eponym is the Davidic high priest."
  },
  {
    "word_id": 6757161,
    "verdict": "source-wrong",
    "referent": "zadok",
    "confidence": "high",
    "reason": "Same Zadokite formula as 40:46, 43:19, 44:15; the eponym is David's and Solomon's high priest, not the later Zadok of 1 Chr 6:12."
  }
]

### H672

**The verse-level trap is the whole case here.** 1 Chronicles 4:4 contains two H-numbers that BibleData folds into one record: אֶפְרָתָה (H672) and בֵּית לָחֶם (H1035), and the `bethlehem` record carries *both* labels. So "BibleData says bethlehem for 1 Chr 4:4" is almost certainly nothing more than the attestation of the word בֵּית לָחֶם at the end of the verse. Its verse list cannot say which of the two words it means, and the pipeline has read the attestation as if it answered for the marked word. It doesn't.

Judging the marked word itself, בְּכֹור אֶפְרָתָה, "firstborn of Ephrathah":

- 1 Chr 2:19 — "Caleb took to him Ephrath, who bore him Hur." BibleData's own `ephrath` record (wife of Chelubai/Caleb) is anchored on this verse, and its kinship edge is exactly that marriage.
- 1 Chr 2:50 — "the sons of Caleb the son of Hur, the firstborn of Ephrathah" — the identical phrase, which BibleData has left untagged altogether.
- 1 Chr 4:4 — "the sons of Hur, the firstborn of Ephrathah, the father of Bethlehem."

"Firstborn of X" names a parent, and 2:19 has already told us who Hur's mother is. The three passages are one genealogical thread: Caleb + Ephrath → Hur (firstborn) → the Bethlehem/Kiriath-jearim/Beth-gader clans. The following apposition "father of Bethlehem" belongs to Hur (as Salma is "father of Bethlehem" in 2:51), not to the feminine Ephrathah. Strong's lexicon itself lists sense 3, "wife of Caleb," for precisely this usage. The BHSA "topo,pers" tag records the real ambiguity that Ephrathah is also the old name of the Bethlehem district and that the Chronicler may be blending clan-eponym and mother; but that is a question about *what the mother's name signifies*, not about *which referent the word names in this verse*. Given the two records on offer, the woman of 2:19 is the referent, and the model's answer is right. There is no plausible reading on which the word means the town in a "firstborn of" construction.

So: BibleData's verse-level attestation of `bethlehem` for this verse is contradicted by its own `ephrath` record and by the parallel in 2:19/2:50 — source-wrong, with the right referent `ephrath`. Two collateral notes for whoever fixes the data: 1 Chr 2:50 (word 6859232) should likewise be tagged `ephrath`, and the `ephrath` record's verse list should grow to 2:19, 2:50, 4:4.

---ANSWER---
[
  {
    "word_id": 6859629,
    "verdict": "source-wrong",
    "referent": "ephrath",
    "confidence": "high",
    "reason": "BibleData's 'bethlehem' for this verse is the verse-level attestation of the other word, בֵּית לָחֶם (H1035), not of אֶפְרָתָה; 'Hur the firstborn of Ephrathah' repeats 1 Chr 2:19/2:50, where Ephrath is Caleb's wife and Hur's mother, i.e. BibleData's own 'ephrath' record."
  }
]

### H6934

**The case.** Nehemiah 12:8 sits inside the list that 12:1 opens with "the priests and the Levites that went up with Zerubbabel the son of Shealtiel, and Jeshua," and that 12:7 closes with "these were the chief of the priests and of their brethren *in the days of Jeshua*." Verse 8 simply continues that same list for the Levites: יֵשׁוּעַ בִּנּוּי קַדְמִיאֵל שֵׁרֵבְיָה יְהוּדָה מַתַּנְיָה. The chronicler then deliberately moves forward in time at 12:12 ("in the days of Joiakim") and again at 12:22–26 ("in the days of Eliashib … Nehemiah the governor, and of Ezra the priest the scribe"). So the Kadmiel of 12:8 is placed, by the text's own framing, in the first return under Zerubbabel and Jeshua the high priest, roughly 538–520 BC.

That is exactly the man BibleData's record `kadmiel` describes: the head of the Levitical house of "Jeshua and Kadmiel, of the sons of Hodaviah" who came up in Ezra 2:40 / Nehemiah 7:43 and who oversaw the temple work with Jeshua in Ezra 3:9. The pairing "Jeshua … Kadmiel" in 12:8 mirrors "the children of Jeshua and Kadmiel" in Ezra 2:40 and "Jeshua with his sons … Kadmiel and his sons" in Ezra 3:9. BibleData even assigns Nehemiah 12:24 ("Jeshua the son of Kadmiel," in the Joiakim/Nehemiah-era section) to `kadmiel`, which is coherent: the returnee's son is a Levite chief a generation later.

BibleData's second record, `kadmiel-2`, is defined by Nehemiah 9:4–5 (the platform and the public confession) and 10:9 (the covenant sealing) — events of Nehemiah's governorship, ca. 445 BC, some ninety years after the return. Whatever one thinks of splitting the name into two persons, the split BibleData actually made is an era-based one: Zerubbabel-era Kadmiel versus Nehemiah-era Kadmiel. On that scheme, a verse the text explicitly dates to "the days of Jeshua" cannot belong to the Nehemiah-era record. BibleData's verse list for `kadmiel-2` therefore contradicts its own logic; the kadmiel-2 distinguisher, tellingly, only cites 9:4–5 and says nothing of 12:8. I trust the distinguisher over the verse list here — the verse list looks like a mechanical "all Nehemiah occurrences after chapter 7" sweep.

**The residual doubt** is not about which record 12:8 belongs to but whether two records should exist at all. The same cluster of names — Jeshua, Binnui/Bani, Kadmiel, Sherebiah — recurs in 9:4, 10:9, and 12:8, and many commentators read these as Levitical house-names rather than individuals; on that reading `kadmiel` and `kadmiel-2` are one house and would be duplicate records. But that is a question about the whole Strong number, not about this word, and BibleData's era split is defensible given the ninety-year gap. Judged within the records as they stand, 12:8 goes with `kadmiel`, and the model's reading is the right one.

---ANSWER---
[
  {
    "word_id": 6856587,
    "verdict": "source-wrong",
    "referent": "kadmiel",
    "confidence": "high",
    "reason": "Nehemiah 12:8 continues the list of Levites who came up with Zerubbabel and Jeshua (12:1, 12:7 'in the days of Jeshua'), i.e. the returnee Kadmiel of Ezra 2:40 / 3:9, whereas kadmiel-2 is by BibleData's own distinguisher the Nehemiah-era Levite of 9:4–5 and 10:9; the verse list contradicts the record's own era-based definition. Residual doubt is only whether the two records are really one Levitical house, which is a Strong-number-wide question and does not change the answer for this word."
  }
]

### H7619

**Reasoning**

1 Chronicles 25 is a self-contained unit: vv. 1–6 name the singers set apart by David — four sons of Asaph, six of Jeduthun, and fourteen of Heman — and vv. 8–31 then distribute exactly those twenty-four men by lot. Heman's fourteen sons in v. 4 are Bukkiah, Mattaniah, Uzziel, **Shebuel** (שְׁבוּאֵל), Jerimoth, Hananiah, Hanani, Eliathah, Giddalti, Romamti-ezer, Joshbekashah, Mallothi, Hothir, Mahazioth. Every one of those fourteen reappears in the lot list of vv. 9–31, and the thirteenth lot (v. 20) falls to שׁוּבָאֵל — the same man under the alternative vocalisation, exactly as the Gershonite Shebuel of 23:16 becomes Shubael in 24:20. The Shubael of 25:20 is therefore Heman's son, the singer, and nobody else: the lot list of ch. 25 draws only on the singers of vv. 2–4.

The Gershonite Shebuel (record `shebuel`) is a different man in a different role — grandson of Moses through Gershom (23:16), head of a Levitical course (24:20), and ruler of the treasuries (26:24). He is never counted among the musicians, and there is no textual route by which a Mosaic Gershonite could take the thirteenth lot among Heman's sons.

Here the trap the instructions warn about is fully in play. BibleData's own distinguisher for `shebuel-2` reads "son of Heman_3 (1CH 25:4), chosen thirteenth by lot (1CH 25:20)" — it names 25:20 explicitly and correctly — yet its verse list for `shebuel-2` contains only 25:4, and 25:20 has been filed under `shebuel`. The distinguisher is the half to trust: it is a prose judgement about the man, and it agrees with the plain structure of the chapter; the verse list looks like a mechanical assignment of the "Shubael" spelling to the record that carries the "Shubael" label. The verse contains only one occurrence of the name, so there is no word-level ambiguity to hide behind.

The reading model's answer (`shebuel-2`) is correct, and its stated reason — the thirteenth lot matching the distinguisher — is the right reason. This is not a duplicate-records case: the two records describe two genuinely different men, and both should remain; the fix is moving 1 Chronicles 25:20 from `shebuel`'s verse list to `shebuel-2`'s.

---ANSWER---
[
  {
    "word_id": 6870934,
    "verdict": "source-wrong",
    "referent": "shebuel-2",
    "confidence": "high",
    "reason": "The lot list of 1 Chr 25:9–31 distributes exactly the singers named in 25:2–4, and the thirteenth lot goes to Shubael = Shebuel son of Heman (25:4); the Gershonite treasurer of 23:16/24:20/26:24 is not among the musicians. BibleData's own distinguisher for shebuel-2 already cites 1CH 25:20, contradicting its verse list, and the distinguisher is the half that matches the text."
  }
]

### H7645

**The lists.** Nehemiah 10 (KJV numbering, which BibleData uses here — the priest Shebaniah is at KJV 10:4 = MT 10:5) has three men called שְׁבַנְיָה sealing the covenant: a priest at 10:4, and two Levites, at 10:10 (שְׁבַנְיָה הֹודִיָּה קְלִיטָא — "Shebaniah, Hodijah, Kelita") and at 10:12 (זַכּוּר שֵׁרֵבְיָה שְׁבַנְיָה — "Zaccur, Sherebiah, Shebaniah"). The disputed word is the 10:12 one, the second Levite occurrence in reading order. BibleData also has a Levite Shebaniah in the platform scene of 9:4–5 (with Jeshua, Bani, Kadmiel, Bunni, Sherebiah, Chenani; and again with Hashabneiah, Sherebiah, Hodijah, Pethahiah), and the sealers list of 10:9–13 draws on the same Levitical roster (Jeshua, Kadmiel, Bani, Sherebiah, Hodijah, Hashabiah, Shebaniah all recur). So the 9:4 man is almost certainly one of the two Levite sealers; which one, the text does not say — 10:10's Shebaniah sits beside Hodijah (as in 9:5) and 10:12's beside Sherebiah (as in 9:4 and 9:5). Neither adjacency is decisive.

**What BibleData actually holds.** Its structured verse lists give a clean, non-overlapping partition of the four occurrences: 10:4 → `shebaniah-3` (priest, also 12:14), 10:10 → `shebaniah-4`, 10:12 → `shebaniah-2` (with 9:4, 9:5). That is: `shebaniah-2` is defined as "the 9:4–5 platform Levite, identified with the 10:12 sealer," and `shebaniah-4` as "the other Levite sealer, at 10:10." Two men, two records, each verse assigned once.

**The trap.** The prose distinguisher of `shebaniah-4` — "second Levite named Shebaniah in the sealed document of NEH 10:1-27 (v10 and v12)" — is badly written and is the source of the dispute. Sonnet read the parenthetical as claiming that `shebaniah-4` occurs at both v10 and v12. But that reading collides with everything else in the record set: it would put `shebaniah-4` and `shebaniah-2` on the same word at 10:12, and the verse list of `shebaniah-4` attests only 10:10. The parenthetical is far more naturally read as "the two Levite Shebaniahs are at v10 and v12; this record is the second (i.e. the other) one" — a note distinguishing him from `shebaniah-2`, not a claim of two attestations. Even the word "second" cannot mean "second in reading order," since v10 comes first; it means "the additional one." Between an ambiguous prose gloss and an internally consistent verse list, I trust the verse list. The verse-level ambiguity that the prompt warns about does not bite here: 10:12 contains only one Shebaniah, so the verse assignment does identify the word.

**Verdict.** Sonnet's answer rests on a misreading of the distinguisher and yields a record structure that cannot be right (two records on one word, one record with a verse list contradicting its own claim). Within BibleData's scheme the 10:12 word is `shebaniah-2`. I hold back from "high" only because of a residual question that the Hebrew genuinely does not settle: whether the 9:4–5 platform Levite is the 10:12 sealer (as BibleData has it) or the 10:10 one. If a future editor decided it was the 10:10 man, the labels would simply swap — but that would be a re-partition of both records, not a warrant for putting `shebaniah-4` on 10:12 as things stand. The records are not duplicates: two distinct Levites bear the name in the one sealed list, and both records should survive.

---ANSWER---
[
  {
    "word_id": 6855551,
    "verdict": "model-wrong",
    "referent": "shebaniah-2",
    "confidence": "medium",
    "reason": "The Levite sealers list has two Shebaniahs (10:10 and 10:12); BibleData's verse lists partition them cleanly as shebaniah-4 at 10:10 and shebaniah-2 (the 9:4-5 platform Levite) at 10:12, and Sonnet's answer rests on misreading the ambiguous parenthetical '(v10 and v12)' in shebaniah-4's distinguisher as a double attestation, which would put two records on one word. Confidence is medium only because the text itself cannot show which of the two sealers is the 9:4 man; the assignment is BibleData's coherent convention, not a demonstrable identification."
  }
]

### H7895

#### Reasoning

**What the two records are.** BibleData has one record, `pharaoh-5`, whose distinguisher is "Pharaoh, father of Solomon's wife (1KI 3:1, 9:16)", carrying the kinship edges *father-in-law → solomon*, *husband → tahpenes*, *brother-in-law → hadad-3*. Onto that record it has bolted the label "Shishak = H7895" and every Shishak verse. It also holds a second record, `shishak`, distinguisher "King of Egypt (1KI 14:25)", with the correct Strong number and an empty verse list. So BibleData has effectively decided that Shishak *is* Solomon's father-in-law, and then left the dedicated Shishak record hollow.

**Does the text support that identification?** No. The pharaoh of 1 Kings 3:1, 9:16, 9:24, 11:18–22 and 2 Chronicles 8:11 is never named; he is simply פַּרְעֹה. He is Solomon's ally: gives his daughter, burns Gezer and gives it as her dowry, harbours Hadad the Edomite *during David's reign* (11:18–21 — Hadad hears in Egypt that David and Joab are dead and asks to leave). Then, at 1 Kings 11:40, the narrator introduces a king of Egypt *by name* — שִׁישַׁק מֶלֶךְ מִצְרַיִם — who shelters Solomon's enemy Jeroboam and later, five years after Solomon's death, plunders the temple (14:25–26; 2 Chr 12). The naming itself is the tell: the writer who has said "Pharaoh" unnamed six times switches to a proper name precisely when a different king is on the throne. The posture is reversed too (ally of the house of David → despoiler of it), and the chronology strains the conflation: the unnamed Pharaoh was already reigning before David died, whereas Shishak is active in Rehoboam's fifth year, roughly forty-five years later. Historically the father-in-law is usually taken as Siamun of the 21st dynasty and Shishak as Shoshenq I, founder of the 22nd — Strong's own gloss says as much. The equation is a later harmonising conjecture, not something the Masoretic text states.

**BibleData's own evidence contradicts its verse list.** Its `pharaoh-5` distinguisher defines the man by 1 Kings 3:1 and 9:16 — the father-in-law — and its `shishak` distinguisher defines a separate man by 1 Kings 14:25. Both prose strings agree with the text; only the verse list disagrees, by dumping the H7895 occurrences onto the wrong record. I trust the distinguishers here, because they match the text's own distinction between an unnamed allied Pharaoh and a named hostile Shishak.

**Not duplicate-records.** That verdict would require the two records to be the same man. They are not: `pharaoh-5` is (correctly, by its distinguisher and kinship edges) the father-in-law, and `shishak` is Shoshenq. The right fix is not to merge but to move: all seven H7895 occurrences, plus the "King of Egypt" mentions in 1 Kings 11:40, 14:25, 2 Chr 12:2 and 12:9, belong to `shishak`; the "Shishak" name label should be stripped from `pharaoh-5`; the kinship edges stay with `pharaoh-5`.

**The individual occurrences.** All seven are the same figure and nothing in any verse separates them:
- 1 Ki 11:40 — "Shishak king of Egypt," to whom Jeroboam flees; explicitly named, explicitly the hostile king.
- 1 Ki 14:25 — Ketiv שׁושׁק / Qere שִׁישַׁק (hence Strong's variant שׁוּשַׁק); the temple raid in Rehoboam's fifth year.
- 2 Chr 12:2, 12:5 (both words), 12:7, 12:9 — the Chronicler's parallel account of the same raid; 12:5 has two occurrences of the name in one verse, and both are Shishak, so the verse-level ambiguity does no harm here.

The reading model is right on every one. BibleData is wrong on every one, and wrong in a way its own second record already refutes.

---ANSWER---
[
  {"word_id": 6652648, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "The text names Shishak precisely to distinguish him from the unnamed Pharaoh who was Solomon's father-in-law; BibleData's own `shishak` record (distinguisher 1KI 14:25) and its `pharaoh-5` distinguisher (father of Solomon's wife) both contradict placing this verse under pharaoh-5."},
  {"word_id": 6655000, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "Ketiv שׁושׁק / Qere שִׁישַׁק is the king who plundered Jerusalem in Rehoboam's fifth year — the very verse BibleData's `shishak` distinguisher cites — not Solomon's allied father-in-law of 1KI 3:1."},
  {"word_id": 6879410, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "Chronicles' parallel to 1KI 14:25; same named hostile king of Egypt, whom BibleData's own separate `shishak` record was created for."},
  {"word_id": 6879473, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "First of two Shishak occurrences in 2CH 12:5, both referring to the invader of Rehoboam's day; identifying him with the father-in-law Pharaoh is an unattested harmonisation that BibleData's own distinguishers do not make."},
  {"word_id": 6879490, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "Second Shishak occurrence in 2CH 12:5 ('into the hand of Shishak'); same invader, same misassignment to the father-in-law record."},
  {"word_id": 6879533, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "'By the hand of Shishak' continues the 2CH 12 invasion narrative; the referent is the `shishak` record, not the unnamed Pharaoh of Solomon's marriage alliance."},
  {"word_id": 6879549, "verdict": "source-wrong", "referent": "shishak", "confidence": "high", "reason": "The temple raid itself, parallel to 1KI 14:25–26; BibleData's verse list contradicts both of its own distinguishers by filing it under Solomon's father-in-law."}
]

### H8018

**The case.** Jeremiah 37:3 names Zedekiah's envoy יְהוּכַל בֶּן שֶׁלֶמְיָה. The marked word is his father. Two records are on the table:

- `shelemiah-6`: BibleData's distinguisher is "son of Adbeel [sic], sent to seize Baruch and Jeremiah (JER 36:26)". Its verse list adds Jer 37:3, and its kinship edges add `father -> jehucal`. So BibleData has silently identified Jehoiakim's officer of Jer 36:26 with the father of Zedekiah's envoy.
- `shelemiah-8`: "father of Jucal (JER 38:1)", kinship `father -> jucal`.

**Jehucal = Jucal.** This is not in doubt. Jer 37:3 has יְהוּכַל בֶּן שֶׁלֶמְיָה; Jer 38:1 has יוּכַל בֶּן שֶׁלֶמְיָהוּ — the same theophoric name in its full and contracted form (as Jehoash/Joash, Jehoram/Joram), the same patronymic, the same reign (Zedekiah), and the same role: a royal official who deals with Jeremiah, sent to him in 37:3 and among the princes who denounce him in 38:1. Every serious reference work treats them as one man; Strong's own entry 5 does ("father of Jehucal or Jucal"). So the father in 37:3 is necessarily the father in 38:1.

**Is he also Abdeel's son?** Jer 36:26 is dated to Jehoiakim's fifth year (~604 BC); Jer 37–38 belong to the siege of 588–587. A Shelemiah who was a royal enforcer in 604 having a son among Zedekiah's princes sixteen years later is chronologically possible, but the text never links them — Abdeel's son is given his own patronymic and Jehucal's father none. The name was common in this period (nine bearers in Strong's, five in Jeremiah alone). The identification is a guess, and Strong's, like most handbooks, keeps them apart (entries 5 and 8).

**BibleData's own inconsistency.** If BibleData believed the father of Jehucal was Abdeel's son, then by its own logic the father of Jucal is too — yet it keeps `shelemiah-8` as a separate record, and keeps `jehucal` and `jucal` as separate persons. The verse list for 37:3 thus rests on an identification (Jehucal's father = Abdeel's son) that the text does not make, while failing the identification (Jehucal = Jucal) that the text does compel. I trust neither half of `shelemiah-6` for this verse: the distinguisher says "son of Abdeel (36:26)" and is correct for 36:26 only; the extra verse and the `jehucal` edge are the error.

**Verdict.** The model's answer is the one the evidence supports: the marked word is the father of Jehucal/Jucal, i.e. the man of Jer 38:1, `shelemiah-8`. The correct repair is to move Jer 37:3 and the `jehucal` edge from `shelemiah-6` to `shelemiah-8` (and to merge `jehucal` into `jucal`), leaving `shelemiah-6` as the son of Abdeel at Jer 36:26 alone. I do not call this duplicate-records, because I do not accept that Abdeel's son and Jehucal's father are the same man — merging the two records would enshrine the very speculation that produced the error. Confidence is medium rather than high only because the identification with Abdeel's son, though unsupported, cannot be positively excluded.

---ANSWER---
[
  {
    "word_id": 6721910,
    "verdict": "source-wrong",
    "referent": "shelemiah-8",
    "confidence": "medium",
    "reason": "Jehucal (37:3) and Jucal (38:1) are certainly the same son of Shelemiah under Zedekiah, so the father here is the man of Jer 38:1, shelemiah-8; BibleData's placement under shelemiah-6 rests on an unsupported identification of Jehucal's father with Abdeel's son of 604 BC, which BibleData itself contradicts by keeping shelemiah-8 separate."
  }
]

### H8050

Both disputed words sit in Chronicles, and both are about the prophet, not the Simeonite prince.

**1 Chronicles 9:22.** The Hebrew is דָּוִיד וּשְׁמוּאֵל הָרֹאֶה — "David and Samuel *the seer*." הָרֹאֶה is the very title attached to Samuel son of Elkanah in 1 Samuel 9:9, 9:11, 9:18–19 ("I am the seer"), and 1 Chronicles 26:28 ("Samuel the seer" among those who dedicated treasures, which BibleData itself assigns to `samuel-2`) and 29:29 ("the words of Samuel the seer"). The pairing with David also fixes the era: the Simeonite Shemuel son of Ammihud belongs to the wilderness allotment of Numbers 34, some four centuries before David. BibleData's record `samuel` is defined by its own distinguisher as the Numbers 34:20 Simeonite, and its kinship edge to `ammihud-2` confirms that identity; that record's verse list is then wrong to include 1 Chronicles 9:22. I trust the distinguisher plus kinship here — they cohere with each other and with Numbers 34 — and reject the verse list's stray Chronicles entries. There is no competing referent; the prophet is `samuel-2`.

**1 Chronicles 11:3.** "According to the word of the LORD by the hand of Samuel" (כִּדְבַר יְהוָה בְּיַד שְׁמוּאֵל) refers to the anointing of David that Samuel performed at Bethlehem (1 Samuel 16:1–13) and the prophetic word of transferring the kingdom from Saul (1 Samuel 15:28, 28:17). This is the Chronicler's addition to the parallel 2 Samuel 5:3, which has no Samuel clause; the addition can only be pointing at the prophet. Again `samuel`'s record identity (Simeonite prince, son of Ammihud) makes it impossible, and the record's verse list is simply misfiled at this verse.

In both cases the assignment failure is the same: BibleData's record `samuel` has a verse list that swept in two Chronicles verses that its own distinguisher and kinship rule out. This is not a duplicate-records situation — `samuel` is a legitimately distinct person for Numbers 34:20 — it is a wrong-verse attribution on that record. The reading model is correct.

(Incidentally, 1 Samuel 28:20 word 6625745 is unassigned in BibleData's verse list; it is plainly `samuel-2` too, though it is not in dispute here.)

---ANSWER---
[
  {
    "word_id": 6863113,
    "verdict": "source-wrong",
    "referent": "samuel-2",
    "confidence": "high",
    "reason": "The word is 'Samuel the seer' paired with David — the prophet's title (1 Sam 9:9, 1 Chr 26:28, 29:29) and era; BibleData's `samuel` record is by its own distinguisher and kinship (son of Ammihud) the Simeonite prince of Numbers 34:20, so its verse list is misfiled here."
  },
  {
    "word_id": 6863840,
    "verdict": "source-wrong",
    "referent": "samuel-2",
    "confidence": "high",
    "reason": "'The word of the LORD by the hand of Samuel' concerning David's kingship points to the prophet's anointing of David (1 Sam 16) and his oracle against Saul (1 Sam 15:28); the Simeonite Shemuel of Numbers 34 cannot be meant, so BibleData's `samuel` verse list is wrong at this verse."
  }
]

### H8304

#### 1 Chronicles 6:14 (MT 5:40), both words — 6860842 and 6860844

The verse sits inside the Chronicler's high-priestly pedigree (1 Chr 6:3–15 / MT 5:29–41): Aaron … Zadok … Shallum → Hilkiah → Azariah → Seraiah → Jehozadak, ending with "Jehozadak went into captivity when YHWH exiled Judah and Jerusalem by the hand of Nebuchadnezzar" (v. 15). That closing note fixes the identity beyond doubt: the Seraiah who fathered Jehozadak is the chief priest (כֹּהֵן הָרֹאשׁ) seized at the fall of Jerusalem and executed at Riblah (2 Kgs 25:18–21; Jer 52:24–27), and Ezra 7:1 traces Ezra's descent through the same Seraiah son of Azariah son of Hilkiah. Jehozadak's son Jeshua is the first post-exilic high priest (Hag 1:1; Ezra 3:2).

BibleData's `seraiah-2` is exactly this man, and its own kinship edges prove it: `son -> azariah-7`, `father -> jehozadak`, `ancestor -> ezra`. The father-of-Jehozadak edge can only have come from 1 Chr 6:14 — no other verse states it. Yet the verse list hands 1 Chr 6:14 to `seraiah-5`, the Simeonite son of Asiel of 1 Chr 4:35, a clan chief in the days of Hezekiah with no priestly connection whatever. That is a clerical slip (probably the two "1 Chronicles" occurrences being sorted under the wrong Seraiah), and it contradicts both `seraiah-5`'s distinguisher ("son of Asiel, Simeonite") and `seraiah-2`'s kinship. I trust the kinship edges and distinguishers over the verse list here.

Both Hebrew words in the verse denote the same man — "Azariah begat Seraiah, and Seraiah begat Jehozadak" is one chain link stated twice — so the same verdict applies to both. The reading model is right; BibleData's verse list is wrong. Referent: `seraiah-2`, high confidence.

#### Nehemiah 12:12 — word 6856639

The structure of Nehemiah 12 decides this. Verses 1–7 list "the priests and Levites who went up with Zerubbabel … and Jeshua": Seraiah, Jeremiah, Ezra, Amariah, Malluch, Shecaniah, Rehum, Meremoth, Iddo, Ginnethoi, Abijah, Mijamin, Maadiah, Bilgah, Shemaiah, Joiarib, Jedaiah, Sallu, Amok, Hilkiah, Jedaiah. Verses 12–21 then say "in the days of Joiakim [Jeshua's son] the priests, heads of fathers' houses, were: of Seraiah, Meraiah; of Jeremiah, Hananiah; of Ezra, Meshullam; of Amariah, Jehohanan; of Melicu …; of Shebaniah …; of Harim …; of Meraioth …; of Iddo …; of Ginnethon …; of Abijah …; of Miniamin/Moadiah …; of Bilgah …; of Shemaiah …; of Joiarib …; of Jedaiah …; of Sallai …; of Amok …; of Hilkiah …; of Jedaiah …" — the same twenty-odd names in the same order, now functioning as house names, with the *next generation's* head named against each. The לְ prefix on לִשְׂרָיָה marks "belonging to [the house of] Seraiah". So the Seraiah of 12:12 is, by construction, the eponym of 12:1 — whatever record 12:1 belongs to, 12:12 belongs to the same one.

BibleData assigns 12:1 to `seraiah-6`, and — tellingly — gives `seraiah-6` the kinship `father -> meraiah`, an edge that can only have been derived from 12:12 ("of Seraiah, Meraiah"). So BibleData's own kinship contradicts its own verse list, which instead puts 12:12 under `seraiah-8`, "Seraiah son of Hilkiah, ruler of the house of God" (Neh 11:11). Again I trust the kinship edge over the verse list.

One caveat should be recorded rather than hidden. Neh 11:11 (Seraiah son of Hilkiah son of Meshullam son of Zadok …) is a doublet of 1 Chr 9:11, where the same man is called *Azariah* son of Hilkiah — and `seraiah-6` is likewise called Azariah in Neh 7:7. Together with 1 Chr 6:13–14 (Hilkiah → Azariah → Seraiah), this suggests that the "house of Seraiah" heading the priestly roster is the old high-priestly family itself, and that `seraiah-6` and `seraiah-8` may ultimately be genealogical labels for one house rather than two independent individuals. That is a real question, but it is a question about whether two records should be merged, not about which of them 12:12 attaches to: within BibleData's own framework, 12:12 must follow 12:1, i.e. `seraiah-6`. The reading model is right; the verse list is wrong. I would not classify this as duplicate-records because the two records are not demonstrably the same man on the evidence given, and the case can be settled without deciding that.

---ANSWER---
[
  {
    "word_id": 6860842,
    "verdict": "source-wrong",
    "referent": "seraiah-2",
    "confidence": "high",
    "reason": "1 Chr 6:14 is the high-priestly line (Hilkiah → Azariah → Seraiah → Jehozadak, who went into exile, v. 15), i.e. the chief priest of 2 Kgs 25:18 / Jer 52:24 / Ezra 7:1; BibleData's own seraiah-2 kinship (son of azariah-7, father of jehozadak) is drawn from this very verse, while seraiah-5 is the unrelated Simeonite son of Asiel."
  },
  {
    "word_id": 6860844,
    "verdict": "source-wrong",
    "referent": "seraiah-2",
    "confidence": "high",
    "reason": "Second mention of the same man in one chain link ('Azariah begat Seraiah, and Seraiah begat Jehozadak'); the father-of-Jehozadak edge on seraiah-2 comes only from here, and the Simeonite seraiah-5 cannot be meant."
  },
  {
    "word_id": 6856639,
    "verdict": "source-wrong",
    "referent": "seraiah-6",
    "confidence": "high",
    "reason": "Neh 12:12–21 repeats the priestly house names of 12:1–7 in the same order with the next generation's heads ('of Seraiah, Meraiah'), so the eponym here is the Seraiah of 12:1, which BibleData assigns to seraiah-6 and whose 'father -> meraiah' edge is itself derived from this verse; the verse list's seraiah-8 (son of Hilkiah, Neh 11:11) contradicts BibleData's own kinship, though whether seraiah-6 and seraiah-8 are ultimately one priestly house remains an open, separate question."
  }
]

