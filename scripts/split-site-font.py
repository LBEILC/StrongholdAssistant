"""生成网站 MiSans 切片：python scripts/split-site-font.py。

需要 fonttools[woff]==4.64.0 和 brotli；不安装依赖、不下载字体。
基础符号独立成片，GB2312 一级汉字按编码顺序均分三片，其余按 Unicode 区段切片。
保留原字体全部 Unicode 字符和可变轴，生成结果提交后无需构建时运行。
"""

from io import BytesIO
from math import ceil
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src/ui/src/assets/fonts/MiSans-VF.woff2"
SITE = ROOT / "src/plaza/site/src"
OUTPUT = SITE / "assets/fonts/misans"
MAX_BYTES = 150 * 1024
TARGET_BYTES = 125 * 1024
MIN_BYTES = 100 * 1024


def encode(points):
    font = TTFont(SOURCE, recalcTimestamp=False)
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    cutter = subset.Subsetter(options=options)
    cutter.populate(unicodes=points)
    cutter.subset(font)
    stream = BytesIO()
    font.save(stream)
    font.close()
    return stream.getvalue()


def unicode_ranges(points):
    runs = []
    for point in sorted(points):
        if runs and point == runs[-1][1] + 1:
            runs[-1][1] = point
        else:
            runs.append([point, point])
    return ",".join(f"U+{a:X}" if a == b else f"U+{a:X}-{b:X}" for a, b in runs)


def main():
    with TTFont(SOURCE) as original:
        points = set(original.getBestCmap())
        weight = next(axis for axis in original["fvar"].axes if axis.axisTag == "wght")
        weight_range = f"{weight.minValue:g} {weight.maxValue:g}"

    # ASCII、拉丁补充和常用中西文标点独立成片。
    basic = {p for p in points if p <= 0xFF or 0x2000 <= p <= 0x206F
             or 0x3000 <= p <= 0x303F or 0xFF00 <= p <= 0xFFEF}
    level_one = []
    for point in points - basic:
        encoded = chr(point).encode("gb2312", errors="ignore")
        if len(encoded) == 2 and 0xB0 <= encoded[0] <= 0xD7:
            level_one.append(point)
    level_one.sort(key=lambda p: chr(p).encode("gb2312"))
    groups = [("basic", sorted(basic))]
    for index in range(3):
        groups.append((f"gb2312-{index + 1:02}",
                       level_one[index * len(level_one) // 3:(index + 1) * len(level_one) // 3]))

    # 其余字符按码点连续分区，按实际压缩体积估算片数后均分。
    remaining = sorted(points - basic - set(level_one))
    if remaining:
        count = ceil(len(encode(remaining)) / TARGET_BYTES)
        while True:
            chunks = [remaining[i * len(remaining) // count:(i + 1) * len(remaining) // count]
                      for i in range(count)]
            encoded_chunks = [encode(chunk) for chunk in chunks]
            if max(map(len, encoded_chunks)) <= MAX_BYTES:
                break
            count += 1
        # 符号比汉字小，合并相邻小片，同时保持码点区段和体积上限。
        index = 0
        while index + 1 < len(chunks):
            if len(encoded_chunks[index]) < MIN_BYTES:
                merged = chunks[index] + chunks[index + 1]
                data = encode(merged)
                if len(data) <= MAX_BYTES:
                    chunks[index:index + 2] = [merged]
                    encoded_chunks[index:index + 2] = [data]
                    continue
                # 整片合并超限时，借用后一片的前半段，余下字符继续参与合并。
                middle = len(chunks[index + 1]) // 2
                merged = chunks[index] + chunks[index + 1][:middle]
                data = encode(merged)
                if len(data) <= MAX_BYTES:
                    chunks[index] = merged
                    encoded_chunks[index] = data
                    chunks[index + 1] = chunks[index + 1][middle:]
                    encoded_chunks[index + 1] = encode(chunks[index + 1])
                    continue
            index += 1
    else:
        chunks, encoded_chunks = [], []

    OUTPUT.mkdir(parents=True, exist_ok=True)
    css = ["/* 由 scripts/split-site-font.py 生成；基础符号、GB2312 一级三片、其余 Unicode 区段。 */"]
    written = set()

    def write(name, members, data):
        filename = f"misans-{name}.woff2"
        (OUTPUT / filename).write_bytes(data)
        written.add(filename)
        css.append("@font-face {\n"
                   "  font-family: 'MiSans';\n"
                   f"  src: url('./{filename}') format('woff2');\n"
                   f"  font-weight: {weight_range};\n"
                   "  font-display: swap;\n"
                   f"  unicode-range: {unicode_ranges(members)};\n"
                   "}")
        print(f"{filename}: {len(members)} chars, {len(data) / 1024:.1f} KiB")

    for name, members in groups:
        write(name, members, encode(members))
    for members, data in zip(chunks, encoded_chunks):
        write(f"unicode-{members[0]:04x}-{members[-1]:04x}", members, data)
    for old in OUTPUT.glob("misans-*.woff2"):
        if old.name not in written:
            old.unlink()
    (OUTPUT / "index.css").write_text("\n\n".join(css) + "\n", encoding="utf-8")
    print(f"{len(written)} subsets; {len(points)} Unicode characters; wght {weight_range}")


if __name__ == "__main__":
    main()
