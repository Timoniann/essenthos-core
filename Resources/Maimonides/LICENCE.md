# Maimonides' count of the commandments

`commandments.tsv` is the 613 commandments of the Torah as Moses Maimonides (1138–1204) counted them
in his *Sefer HaMitzvot*, 248 positive and 365 negative, each with a short English title and the
verses it rests on. `scripts/maimonides.py` writes it from the downloads under `sefaria/`, which are
not carried in the repository; `python scripts/maimonides.py --fetch` puts them back.

## What is whose

| Part | Whose | Terms |
|---|---|---|
| The count and its numbers | Maimonides, *Sefer HaMitzvot*, read in the Hebrew of the Warsaw 1883 edition | Public domain by age |
| The titles and the verse references | Moses Hyamson's English of the list of commandments that opens the *Mishneh Torah* (Bloch, New York, 1937–1949) | Public domain (see below) |
| Which of Hyamson's numbers answers to which of the *Sefer HaMitzvot*'s, the verses moved to the English numbering, and the five references marked *ours* | This project | CC BY 4.0, as the rest of what Essenthos makes |

Both texts were taken from Sefaria's API on **2026-09-23**:

- *Mishneh Torah, Positive Mitzvot* and *Negative Mitzvot*, version "The Mishneh Torah by Maimonides.
  trans. by Moses Hyamson, 1937-1949", source given as <https://www.nli.org.il/he/books/NNL_ALEPH002108865>.
- *Sefer HaMitzvot, Positive Commandments* and *Negative Commandments*, version "Sefer HaMitzvot,
  Warsaw 1883", source given as <https://www.nli.org.il/he/books/NNL_ALEPH001769510>.

Sefaria states **Public Domain** for all four, and the script refuses to download a version for
which it says anything else. That statement is what this rests on:

- *Sefer HaMitzvot* is twelfth-century, and its Hebrew is a medieval translation printed in 1883.
  Nothing about it is in copyright anywhere.
- Hyamson died in 1949, so his English is out of copyright wherever the term is the author's life
  and seventy years. For the United States, where a book of 1937 stayed in copyright only if it was
  renewed, this project has not searched the renewal records itself and relies on Sefaria's
  statement.

Sefaria is credited as the route on the sources page and here, whether or not anything requires it.

## What this project did to them

Recorded row by row in the `note` column, so a reader can tell a reference Hyamson printed from one
this project moved or supplied.

- **The numbering is the Sefer HaMitzvot's.** The *Mishneh Torah*'s list is Maimonides' own summary
  of the same count and keeps its order except in three places, read commandment by commandment in
  both works: the king's Torah scroll and every man's (positive 17 and 18), the two ransoms
  (negative 295 and 296, which Hyamson's own footnote notes), and the days of rest (negative 327 to
  329). `mishneh_torah` is the number Hyamson's list gives the same commandment.
- **The verses are in the English numbering the corpus is addressed in.** Hyamson prints the Hebrew
  numbering for most verses where the two differ and the English for a few. Every reference was
  checked against the words he quotes from it, in the King James text, and moved only where the
  Hebrew address is what he meant; 61 rows say so. Where the Decalogue's four short commands are
  one verse in his count, the one he quotes is chosen.
- **Five references are ours**, each saying why: two commandments for which the list cites no verse,
  and three where the verse printed does not hold the words quoted.
- **The titles are Hyamson's words, shortened**: the clause before *as it is said*, without the
  verse in brackets and without the explanation that follows it. Nothing is reworded.
- The titles are English only. No translation into the other languages the reader offers is in the
  public domain, and none has been made here.
