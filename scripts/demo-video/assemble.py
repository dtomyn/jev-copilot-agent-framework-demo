"""Turns the frames written by record.py into an H.264 MP4, compressing the fast-forward stretches.

Usage: assemble.py <frames-dir> <output.mp4> [crf]

The default CRF keeps a roughly three-minute 1440x900 recording under GitHub's 10 MB attachment limit;
the script raises the CRF until it fits.
"""
import json
import subprocess
import sys
from pathlib import Path

import imageio_ffmpeg

SPEEDUP = 10
LIMIT_BYTES = 10 * 1024 * 1024 - 256 * 1024  # leave headroom under GitHub's 10 MB
END_CARD_SECONDS = 5.0  # the screencast emits nothing while the page is static


def build_list(frames_dir: Path, frames: list) -> float:
    lines = []
    total = 0.0
    for i, f in enumerate(frames):
        nxt = frames[i + 1]["t"] if i + 1 < len(frames) else f["t"] + END_CARD_SECONDS
        d = max(nxt - f["t"], 0.001)
        if f["fast"]:
            d /= SPEEDUP
        total += d
        lines.append(f"file '{(frames_dir / f['file']).as_posix()}'\nduration {d:.4f}")
    lines.append(f"file '{(frames_dir / frames[-1]['file']).as_posix()}'")
    (frames_dir / "list.txt").write_text("\n".join(lines))
    return total


def encode(frames_dir: Path, out: Path, crf: int):
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error",
                    "-f", "concat", "-safe", "0", "-i", str(frames_dir / "list.txt"),
                    "-vf", "fps=20,format=yuv420p",
                    "-c:v", "libx264", "-preset", "veryslow", "-crf", str(crf), "-tune", "stillimage",
                    "-movflags", "+faststart", str(out)], check=True)


def main():
    if len(sys.argv) not in (3, 4):
        sys.exit(__doc__)
    frames_dir, out = Path(sys.argv[1]), Path(sys.argv[2])
    crf = int(sys.argv[3]) if len(sys.argv) == 4 else 28

    frames = json.loads((frames_dir / "index.json").read_text())
    print(f"video length {build_list(frames_dir, frames):.1f}s")
    out.parent.mkdir(parents=True, exist_ok=True)

    while True:
        encode(frames_dir, out, crf)
        size = out.stat().st_size
        print(f"crf {crf}: {size / 1e6:.2f} MB")
        if size <= LIMIT_BYTES or crf >= 40:
            break
        crf += 2

    if size > LIMIT_BYTES:
        sys.exit(f"{out} is still over 10 MB at crf {crf}.")
    print(out)


if __name__ == "__main__":
    main()
