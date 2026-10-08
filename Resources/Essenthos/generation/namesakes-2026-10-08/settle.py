"""PRB-0490: Codex's reading against the blind Opus judge. agreed.json = verses both say name the same one of the two
men (to apply as word rulings); same.json = verses the judge says hold one man twice (candidate folds, to be read,
not applied blind); disputed.json = the rest. Out: those three files and a README line on stdout."""
import json
from pathlib import Path

HERE = Path(__file__).parent
v = {}
for i in (1, 2):
    for x in json.loads((HERE / f"judge-verdict-{i}.json").read_text(encoding="utf-8")):
        v[x["key"]] = x
agreed, same, disputed = [], [], []
for k, x in sorted(v.items()):
    a = json.loads((HERE / "out" / f"{k}.json").read_text(encoding="utf-8"))
    entry = {"verse": a["case"]["verse"], "name": a["label"], "dataset_man": a["dataset_man"], "split_man": a["split_man"],
             "codex": a["names_slug"], "codex_why": a["why"], "codex_confidence": a["confidence"],
             "judge": {"A": a["dataset_man"], "B": a["split_man"]}.get(x["verdict"], x["verdict"]), "judge_why": x["why"]}
    if x["verdict"] in ("A", "B") and x["verdict"] == a["names"]:
        agreed.append(entry)
    elif x["verdict"] == "same":
        same.append(entry)
    else:
        disputed.append(entry)
for name, data in (("agreed", agreed), ("same", same), ("disputed", disputed)):
    (HERE / f"{name}.json").write_text(json.dumps(data, ensure_ascii=False, indent=1), encoding="utf-8")
pairs = sorted({tuple(sorted((e["dataset_man"], e["split_man"]))) for e in same})
print(f"agreed {len(agreed)}, same {len(same)} ({len(pairs)} pairs), disputed {len(disputed)}")
