"""Fetch the pinned fastText file at the cache location used by this upstream."""
import json
from pathlib import Path
import download_models as download

if __name__ == "__main__":
    base = Path(__file__).resolve().parent
    lock = json.loads((base.parent / "recovery/upstream-lock.json").read_text(encoding="utf-8"))
    item = lock["fasttext"]
    download.ROOT = base / "GPT-SoVITS/GPT_SoVITS/pretrained_models/fast_langdetect"
    download.download(dict(path="lid.176.bin", size=item["bytes"], sha256=item["sha256"], url=item["url"]))
