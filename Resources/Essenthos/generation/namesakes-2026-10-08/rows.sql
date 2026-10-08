-- PRB-0490: a BibleData person row contradicted by the person split (batch R's definition), with the split's man.
select json_agg(x) from (
 select d.canonical_book b, d.canonical_chapter c, d.canonical_verse v, d.label,
        de.slug dataset_man, se.slug split_man
 from entity_verse d join entity de on de.id=d.entity_id and de.kind='person'
 join entity_verse s on s.canonical_book=d.canonical_book and s.canonical_chapter=d.canonical_chapter and s.canonical_verse=d.canonical_verse
      and s.source like 'Essenthos, from the person split%' and s.entity_id<>d.entity_id
 join entity se on se.id=s.entity_id and se.kind='person' and lower(se.name)=lower(de.name)
 where d.source like 'BibleData%'
   and not exists (select 1 from entity_verse s2 where s2.entity_id=d.entity_id and s2.canonical_book=d.canonical_book
        and s2.canonical_chapter=d.canonical_chapter and s2.canonical_verse=d.canonical_verse and s2.source like 'Essenthos, from the person split%')
 order by 1,2,3) x;
