-- Reader-facing text that tells how a record was settled. Read-only; every count must be 0 and no row listed.
SET statement_timeout = '120s';
\pset format unaligned
WITH said(place, slug, text) AS (
    SELECT 'entity.notes', slug, notes FROM entity
    UNION ALL SELECT 'entity.distinguisher', slug, distinguisher FROM entity
    UNION ALL SELECT 'entity_note_translation', e.slug, t.text FROM entity_note_translation t JOIN entity e ON e.id = t.entity_id
    UNION ALL SELECT 'entity_distinguisher', e.slug, d.text FROM entity_distinguisher d JOIN entity e ON e.id = d.entity_id
    UNION ALL SELECT 'entity_known_for', e.slug, k.english FROM entity_known_for k JOIN entity e ON e.id = k.entity_id
    UNION ALL SELECT 'entity_known_for_text', e.slug, t.text FROM entity_known_for_text t JOIN entity_known_for k ON k.id = t.known_for_id JOIN entity e ON e.id = k.entity_id
    UNION ALL SELECT 'entity_alternative.reason', e.slug, a.reason FROM entity_alternative a JOIN entity e ON e.id = a.entity_id
    UNION ALL SELECT 'entity_alternative.describes', e.slug, a.describes FROM entity_alternative a JOIN entity e ON e.id = a.entity_id
    UNION ALL SELECT 'title_bearer.note', e.slug, b.note FROM title_bearer b JOIN entity e ON e.id = b.title_entity_id
    UNION ALL SELECT 'observance_time.note', e.slug, o.note FROM observance_time o JOIN entity e ON e.id = o.entity_id
    UNION ALL SELECT 'entity_passage.note', e.slug, p.note FROM entity_passage p JOIN entity e ON e.id = p.entity_id
    UNION ALL SELECT 'entity_image_caption', '', caption FROM entity_image_caption
)
SELECT place, count(*) AS settling
FROM said
WHERE text ~* '(\mencyclopedia(?!/)|\mdataset|\mcorpus\M|this record|the record\M|own record|for review|\maudit|under prompt|\mlinked\M|Named here as|is held as|are held\M|joined here|the entry is held|project owner|the owner (ruled|decided)|\magent\M|review page|in chat|енциклопед|набір даних|корпус|цей запис|цього запису|цим записом|залишено (на|для)|Enzyklopädie|Datensatz|Korpus|dieser Eintrag|diesem Eintrag|dieses Eintrags|Prüfung|enciclopedia|conjunto de datos|este registro|esta entrada|revisión|\mPRB-|\mTSK-)'
GROUP BY place ORDER BY place;
SELECT 'period', count(*) FROM period WHERE notes ~ '(The dataset marks one end|The parent of a band|loading failure|this axis)';
SELECT 'event', count(*) FROM event WHERE notes ~ '(One event under two datasets|this row holds)';
