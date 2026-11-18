"""从整局录屏和本地采集包生成 ScanReplayTests 的离线输入。需要 FFmpeg 和 Pillow。"""
import argparse
import json
import subprocess
import zipfile
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", type=Path, help="采集包目录")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--output", type=Path, default=Path("out/344"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    recording = root / "docs/research/refs/recordings/rec-2026-11-15-hard-full.mp4"
    samples = root / "src/StrongholdAssistant.Tests/TestData"
    clips = []

    def clip(phase, paths, theme=0, adb=False):
        clips.append(dict(Phase=phase, Theme=theme, Paths=list(map(str, paths)), Adb=adb))

    for phase, start, interval in [("menu", 0, .8), ("battle", 310, .8),
                                   ("shop", 115, .4), ("board", 279, .4)]:
        paths = []
        for i in range(4):
            path = output / f"{phase}-{i}.png"
            subprocess.run([args.ffmpeg, "-loglevel", "error", "-ss", str(start + i * interval),
                            "-i", str(recording), "-frames:v", "1", "-vf", "scale=1280:720",
                            "-y", str(path)], check=True)
            paths.append(path)
        clip(phase, paths)
    clip("closed", [samples / "board-boss-right-collapsed-r14.jpg"] * 4)
    clip("opening", [samples / "solo-opening-level1-1920x1080.png"] * 4)
    # 宿主连续两轮读到同一回合后，才会把面板名单记入该回合。
    clip("panel", [output / "shop-0.png"] * 2 + [samples / "board-enemy-panel.png"] * 3)

    sources = set()
    native = []
    for package in sorted(args.packages.glob("*.zip")):
        with zipfile.ZipFile(package) as archive:
            context = json.loads(archive.read("meta.json"))["context"]
            env = context["environment"]
            key = env["source"], env.get("rawWidth"), env.get("rawHeight")
            frames = [name for name in archive.namelist() if name.startswith("frames/")
                      and name.lower().endswith((".jpg", ".png", ".webp"))]
            if key in sources or not frames:
                continue
            sources.add(key)
            paths = []
            for i, name in enumerate(frames[:3]):
                path = output / f"pack-{package.stem[-4:]}-{i}.png"
                with archive.open(name) as entry, Image.open(entry) as image:
                    image.convert("RGB").save(path)
                paths.append(path)
            clip(str(key), paths, {"terminal": 0, "ursus": 1, "closure": 2}[context["theme"]],
                 key[0] == "mumu")
            raw = next((name for name in archive.namelist() if name.startswith("raw/")
                        and name.endswith(".png")), None)
            if raw and (key[1:] == (2560, 1600) or key[0] == "mumu"):
                path = output / f"native-{package.stem[-4:]}.png"
                path.write_bytes(archive.read(raw))
                native.append((f"native-{key[0]}", [path], clips[-1]["Theme"], key[0] == "mumu"))
    for args_for_clip in native:
        clip(*args_for_clip)
    (output / "clips.json").write_text(json.dumps(clips, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
