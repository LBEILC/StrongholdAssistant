"""生成小人比对包；需要 numpy、opencv-python，输入为 sprite-render 的 manifest.json 与 PNG。

用法：python scripts/generate-sprite-templates.py --rendered out/134/rendered
包为 gzip：SPRT0004、处理分辨率(uint16)、像素/骨骼单位(float32)、记录数(uint32)，随后为姓名长度(uint16)/UTF-8、皮肤ID长度(uint16)/UTF-8、
阶数/宽/高(3个uint16)、原点X/Y(2个float32)、BGR像素、单通道蒙版。数值均为小端。
原点为像素边界坐标，相对裁紧模板左上角，允许位于蒙版外。
没有 pixelsPerUnit 的旧清单仍输出按最长边归一的 SPRT0003。
"""
import argparse
import gzip
import json
import struct
from pathlib import Path

import cv2
import numpy as np


def generate(rendered, operators, output, size=60, default_only=False, allow_partial=False, skin_frames=4, append=None):
    records = []
    covered = set()
    metadata = json.loads((rendered / "manifest.json").read_text(encoding="utf-8"))
    pixels_per_unit = metadata.get("pixelsPerUnit")
    manifest = metadata["operators"]
    catalog = json.loads(operators.read_text(encoding="utf-8-sig"))
    entries = catalog.get("operators")
    if entries is None:
        entries = [{"name": s["charId"], "tier": 0} for s in catalog["summons"]]
    for op in entries:
        name = op["name"].strip("“”")
        seen = set()
        # 清单只包含已完整渲染的干员，失败的半套图不打包。
        paths = [(rendered / file, record["skinId"], record["origins"][file])
                 for record in manifest.get(name, {}).get("records", [])
                 if not default_only or record["skinId"] == "default"
                 for file in (record["files"] if record["skinId"] == "default"
                              else [record["files"][i * 4 // skin_frames] for i in range(skin_frames)])]
        if not paths:
            if allow_partial:
                continue
            raise ValueError(f"缺少默认模板：{name}")
        for path, skin_id, origin in paths:
            image = cv2.imdecode(np.fromfile(path, dtype=np.uint8), cv2.IMREAD_UNCHANGED)
            mask = (image[:, :, 3] > 100).astype(np.uint8) * 255
            x, y, w, h = cv2.boundingRect(mask)
            # 新清单整体缩放，保留不同干员、皮肤和帧之间的真实大小。
            scale = size / 120 if pixels_per_unit is not None else size / max(w, h)
            ww, hh = max(1, round(w * scale)), max(1, round(h * scale))
            a = np.zeros((hh + 4, ww + 4, 3), np.uint8)
            b = np.zeros((hh + 4, ww + 4), np.uint8)
            xx, yy = 2, 2
            ox = (origin["x"] - x) * ww / w + xx
            oy = (origin["y"] - y) * hh / h + yy
            a[yy:yy+hh, xx:xx+ww] = cv2.resize(image[y:y+h, x:x+w, :3], (ww, hh))
            b[yy:yy+hh, xx:xx+ww] = cv2.resize(mask[y:y+h, x:x+w], (ww, hh), interpolation=cv2.INTER_NEAREST)
            # 尺度与镜像由比对器缓存；同图但不同皮肤仍保留身份。
            x, y, w, h = cv2.boundingRect(b)
            payload = struct.pack("<3H2f", op["tier"], w, h, ox - x, oy - y) + a[y:y+h, x:x+w].tobytes() + b[y:y+h, x:x+w].tobytes()
            if (skin_id, payload) not in seen:
                seen.add((skin_id, payload))
                encoded = op.get("templateName", name).encode("utf-8")
                skin = skin_id.encode("utf-8")
                records.append(struct.pack("<H", len(encoded)) + encoded + struct.pack("<H", len(skin)) + skin + payload)
            covered.add(name)
    previous = b""
    previous_count = 0
    if append:
        previous = gzip.decompress(append.read_bytes())
        expected = b"SPRT0004" + struct.pack("<Hf", size, pixels_per_unit * size / 120)
        if previous[:14] != expected:
            raise ValueError("追加包必须使用相同的真实比例和分辨率")
        previous_count = struct.unpack_from("<I", previous, 14)[0]
        previous = previous[18:]
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as dest:
        if pixels_per_unit is not None:
            dest.write(b"SPRT0004" + struct.pack("<HfI", size, pixels_per_unit * size / 120, previous_count + len(records)))
        else:
            dest.write(b"SPRT0003" + struct.pack("<HI", size, len(records)))
        dest.write(previous)
        for record in records:
            dest.write(record)
    print(json.dumps(dict(operators=len(covered), templates=len(records), bytes=output.stat().st_size)))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rendered", type=Path, required=True)
    parser.add_argument("--operators", type=Path, default=Path("data/operators.json"))
    parser.add_argument("--output", type=Path, default=Path("data/sprite-templates.bin.gz"))
    parser.add_argument("--size", type=int, choices=[60, 120], default=60)
    parser.add_argument("--default-only", action="store_true")
    parser.add_argument("--skin-frames", type=int, choices=[1, 2, 4], default=4,
                        help="每套非默认皮肤每个朝向均匀取帧；默认服装保留四帧")
    parser.add_argument("--allow-partial", action="store_true", help="仅离线评测使用，产品包必须覆盖全部干员")
    parser.add_argument("--append", type=Path, help="保留已有真实比例模板的原始记录，追加独立外观")
    args = parser.parse_args()
    generate(args.rendered, args.operators, args.output, args.size, args.default_only, args.allow_partial, args.skin_frames, args.append)
