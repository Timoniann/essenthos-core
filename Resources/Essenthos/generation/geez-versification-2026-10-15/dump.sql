-- What derive.py reads, read from the corpus one statement at a time (docker exec ... psql < one of these):
-- each \copy's output goes to the file named above it; words.csv is the second and the fourth together.
SET statement_timeout = '120s';

-- verses.csv
\copy (SELECT t.slug, b.canonical_ordinal, v.chapter_number, v.number, v.label, v.id, r.canonical_book, r.canonical_chapter, r.canonical_verse, (SELECT count(*) FROM word w WHERE w.verse_id = v.id) FROM text t JOIN book b ON b.text_id = t.id JOIN verse v ON v.book_id = b.id LEFT JOIN verse_reference r ON r.verse_id = v.id AND r.is_primary WHERE t.slug IN ('GEEZ81', 'GRCBRENT', 'SWETE', 'NESTLE1904', 'RP2018', 'BHSA')) TO STDOUT WITH CSV

-- words.csv
\copy (SELECT t.slug, w.id, w.verse_id, w.position, w.text, w.lemma, w.strong_number FROM word w JOIN text t ON t.id = w.text_id WHERE t.slug IN ('GEEZ81', 'GRCBRENT', 'SWETE', 'NESTLE1904')) TO STDOUT WITH CSV

-- links.csv
\copy (SELECT tt.slug, l.id, f.word_id, t.word_id, l.confidence FROM link l JOIN text g ON g.id = l.from_text_id AND g.slug = 'GEEZ81' JOIN text tt ON tt.id = l.to_text_id JOIN link_word f ON f.link_id = l.id AND f.side = 'from' JOIN link_word t ON t.link_id = l.id AND t.side = 'to' WHERE tt.slug IN ('GRCBRENT', 'SWETE', 'NESTLE1904', 'RP2018')) TO STDOUT WITH CSV

-- words.csv, appended
\copy (SELECT t.slug, w.id, w.verse_id, w.position, w.text, w.lemma, w.strong_number FROM word w JOIN text t ON t.id = w.text_id WHERE t.slug = 'RP2018') TO STDOUT WITH CSV

-- Then: python -I derive.py; GEEZ_PARAMS=params.json python -I runall.py fin; python -I runs.py beads-fin.pkl;
-- python -I final.py; python -I write.py geez.tsv; python -I leftout.py left-out.tsv.
-- evalmap.py measures the alignment against Essenthos.Forge/BetaMasaheft/GeezVerseMap.tsv (copied beside it);
-- damage.py measures the word links of the verses the map moves.
