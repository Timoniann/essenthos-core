"""Gate the DP moves into the applied map and write the reviewable TSV body."""
import pickle, collections, sys
exec(open('derive_load.py', encoding='utf-8').read())
out = pickle.load(open('beads-fin.pkl', 'rb'))
ev = pickle.load(open('runs.pkl', 'rb'))
applied_ids = {o[0].id for e, r in ev if e >= 8 or (len(r) >= 3 and e >= 4) for o in r}
gated_out = sum(len(r) for e, r in ev if not (e >= 8 or (len(r) >= 3 and e >= 4)))
# 'none' verses directly between or beside applied verses of the same chapter
seq = [o for o in out if o[3] != 'no-greek']
lines = []
for k, o in enumerate(seq):
    v = o[0]
    if v.id in applied_ids:
        lines.append(o)
    elif o[3] == 'none':
        near = [seq[x] for x in (k - 1, k + 1) if 0 <= x < len(seq)]
        if any(n[0].id in applied_ids and (n[0].book, n[0].chapter) == (v.book, v.chapter) for n in near):
            lines.append(o)
# a 'none' next to another 'none' that is listed: extend once more
listed = {o[0].id for o in lines}
for k, o in enumerate(seq):
    if o[3] == 'none' and o[0].id not in listed:
        near = [seq[x] for x in (k - 1, k + 1) if 0 <= x < len(seq)]
        if any(n[0].id in listed and n[3] == 'none' and (n[0].book, n[0].chapter) == (o[0].book, o[0].chapter) for n in near):
            lines.append(o); listed.add(o[0].id)
lines.sort(key=lambda o: [x.id for x in order_in_text['GEEZ81']].index(o[0].id) if False else (o[0].book, o[0].chapter, o[0].number, o[0].label))
no_greek_books = sorted({o[0].book for o in out if o[3] == 'no-greek'})
print('lines', len(lines), 'moves', sum(o[3] != 'none' for o in lines), 'none', sum(o[3] == 'none' for o in lines), 'gated out', gated_out)
print('chapters', len({(o[0].book, o[0].chapter) for o in lines}), 'books', len({o[0].book for o in lines}))
print('no-greek books', no_greek_books)
pickle.dump({'lines': lines, 'gated_out': gated_out, 'no_greek': no_greek_books}, open('final.pkl', 'wb'))
