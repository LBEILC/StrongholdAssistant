"""比较两次 ScanReplayTests 的 JSONL；识别输出或输入顺序变化时以非零状态退出。"""
import argparse
import json
from pathlib import Path
from statistics import mean, median


def read(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8-sig").splitlines()]


def summary(rows):
    return (sum(r["cpu"] for r in rows) * 1000 / sum(r["period"] for r in rows),
            mean(r["elapsed"] for r in rows), max(r["memory"] for r in rows) / 2**20)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    args = parser.parse_args()
    runs = [read(path) for path in (args.before, args.after)]
    before, after = ([r for r in rows if "result" in r] for rows in runs)
    def keys(rows):
        return [(r["Phase"], r["frame"]) for r in rows]

    if not before or keys(before) != keys(after):
        raise SystemExit("回放为空，或帧清单和顺序不一致")
    differences = []
    for i, (old, new) in enumerate(zip(before, after)):
        a, b = old["result"], new["result"]
        if a != b:
            fields = [k for k in a.keys() | b.keys() if k not in a or k not in b or a[k] != b[k]]
            differences.append(f'{i + 1}. {new["Phase"]} / {new["frame"]}：{", ".join(sorted(fields))}')
    print("| 阶段 | CPU ms/s | 每轮均值 ms | 最大工作集 MiB |")
    print("|---|---:|---:|---:|")
    for phase in dict.fromkeys(r["Phase"] for r in before):
        metrics = [summary([r for r in rows if r["Phase"] == phase]) for rows in (before, after)]
        cells = [f"{x:.1f} → {y:.1f}" for x, y in zip(*metrics)]
        print("| " + " | ".join([phase, *cells]) + " |")
    for key in ("publish", "remove", "refresh", "scanWait"):
        shop_event = key in ("publish", "remove")
        samples = [[r[key] for r in rows if r.get(key) is not None
                    and (not shop_event or r["Phase"] in ("shop", "board"))] for rows in runs]
        print(key, " → ".join(f"{median(s):.2f} ms（{len(s)} 次）" if s else "无事件" for s in samples))
    print(f"{len(before)} 帧，{len(differences)} 帧输出变化")
    if differences:
        raise SystemExit("\n".join(differences))


if __name__ == "__main__":
    main()
