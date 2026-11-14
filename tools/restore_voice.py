"""Restore portable voice configuration and verified local assets, never AppData.

The repository contains code/metadata, not the user's recordings or trained weights.
Use audit first, then prepare in a clean clone with preserved local files or ZIPs.
"""
import argparse
from contextlib import ExitStack
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RECOVERY = ROOT / "voice-training/recovery"


def read(name):
    return json.loads((RECOVERY / name).read_text(encoding="utf-8"))


def safe_path(root, relative):
    root = root.resolve()
    path = (root / relative).resolve()
    if not path.is_relative_to(root) or path == root:
        raise ValueError(f"Path escapes project: {relative}")
    return path


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            value.update(chunk)
    return value.hexdigest()


def matches(path, item):
    if not path.is_file():
        return False
    variants = [item] + ([item["portableCopy"]] if "portableCopy" in item else [])
    expected = [v["sha256"] for v in variants if path.stat().st_size == v["bytes"]]
    return bool(expected) and digest(path) in expected


def selected(group):
    groups = {"inference"} if group == "inference" else {"inference", "training"}
    if group == "all":
        groups.add("sources")
    return [item for item in read("assets.json")["files"] if item["group"] in groups]


def configurations(root):
    result = {name: text.replace("${PROJECT_ROOT}", root.as_posix()).encode("utf-8")
              for name, text in read("templates.json")["files"].items()}
    run = "voice-training/trained-20261014-v2"
    chosen = json.loads(result[f"{run}/selected-models.json"])
    runtime = root / "voice-training/runtime"
    preset = dict(Endpoint="http://127.0.0.1:9880", LoadOnDemand=True, ClarityEnabled=True,
                  ReminderSpeechEnabled=True, RuntimePythonPath=str(runtime / "venv/Scripts/python.exe"),
                  RuntimeRepositoryPath=str(runtime / "GPT-SoVITS"),
                  RuntimeConfigPath=str(root / run / "yier/inference.yaml"),
                  FfmpegPath=str(runtime / "GPT-SoVITS/runtime/ffmpeg.exe"))
    for role in ("yier", "bubu"):
        preset[role.title()] = dict(GptPath=chosen[role]["gpt"], SovitsPath=chosen[role]["sovits"],
            ReferencePath=str(root / run / role / "reference.wav"),
            ReferenceText=result[f"{run}/{role}/reference.txt"].decode("utf-8").strip(),
            ModelVersion="v2", Text="你好呀，今天也要开开心心！")
    result["voice-training/restored-voices.json"] = (json.dumps(preset, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    return result


def prepare(root, source, group, archives):
    items = selected(group)
    configs = configurations(root)
    # Check all conflicts before changing anything; a restored clone never replaces user edits.
    for name, data in configs.items():
        dest = safe_path(root, name)
        if dest.exists() and dest.read_bytes() != data:
            raise ValueError(f"Existing configuration differs; use a clean clone: {name}")
    with ExitStack() as stack:
        opened = {key: stack.enter_context(zipfile.ZipFile(path)) for key, path in archives.items() if path}
        transfers = []
        for item in items:
            dest = safe_path(root, item["path"])
            if dest.exists():
                if not matches(dest, item):
                    raise ValueError(f"Existing asset differs: {item['path']}")
                continue
            local = safe_path(source, item["path"])
            archive = opened.get(item.get("archive"))
            if local.is_file():
                if not matches(local, item):
                    raise ValueError(f"Source hash mismatch: {item['path']}")
                transfers.append((item, local, None))
            elif archive is not None:
                names = archive.namelist()
                sizes = {item["bytes"], item.get("portableCopy", item)["bytes"]}
                if names.count(item["entry"]) != 1 or archive.getinfo(item["entry"]).file_size not in sizes:
                    raise ValueError(f"Missing/ambiguous ZIP entry: {item['entry']}")
                transfers.append((item, None, archive))
            else:
                raise FileNotFoundError(f"Local backup required: {item['path']}")
        for item, local, archive in transfers:
            dest = safe_path(root, item["path"])
            dest.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(dir=dest.parent, delete=False, suffix=".partial") as temp:
                temporary = Path(temp.name)
            try:
                with (local.open("rb") if local else archive.open(item["entry"])) as src, temporary.open("wb") as out:
                    shutil.copyfileobj(src, out, length=1024 * 1024)
                if not matches(temporary, item):
                    raise ValueError(f"Copied asset hash mismatch: {item['path']}")
                # Windows rename fails if another writer created the destination in the meantime.
                temporary.rename(dest)
            finally:
                temporary.unlink(missing_ok=True)
    for name, data in configs.items():
        dest = safe_path(root, name)
        dest.parent.mkdir(parents=True, exist_ok=True)
        if not dest.exists():
            with dest.open("xb") as stream:
                stream.write(data)
    print(f"Prepared {len(items)} verified assets and {len(configs)} portable files; AppData unchanged.")


def audit_runtime(root, training=False):
    lock = read("upstream-lock.json")
    repo = root / "voice-training/runtime/GPT-SoVITS"
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    if commit != lock["commit"]:
        raise ValueError("GPT-SoVITS commit differs from upstream-lock.json")
    for name, expected in lock["patchedFilesLfSha256"].items():
        actual = hashlib.sha256(safe_path(repo, name).read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        if actual != expected:
            raise ValueError(f"Missing/different upstream patch: {name}")
    for item in read("pretrained-models.json"):
        if item.get("trainingOnly") and not training:
            continue
        path = safe_path(repo / "GPT_SoVITS/pretrained_models", item["path"])
        if not matches(path, dict(bytes=item["size"], sha256=item["sha256"])):
            raise ValueError(f"Missing/different pretrained asset: {item['path']}")
    if digest(repo / "runtime/ffmpeg.exe") != lock["ffmpeg"]["sha256"]:
        raise ValueError("FFmpeg differs from the recorded version")
    for item in lock["frontendAssets"]:
        if not matches(safe_path(repo, item["path"]), item):
            raise ValueError(f"Missing/different text frontend asset: {item['path']}")
    python = root / "voice-training/runtime/venv/Scripts/python.exe"
    code = "import json,sys,importlib.metadata as m;print(json.dumps({'python':'.'.join(map(str,sys.version_info[:3])),'torch':m.version('torch'),'torchaudio':m.version('torchaudio')}))"
    versions = json.loads(subprocess.check_output([str(python), "-c", code], text=True))
    if any(versions[name] != lock[name] for name in versions):
        raise ValueError(f"Runtime versions differ: {versions}")
    print("Pinned upstream, patches, base/frontend assets, FFmpeg and Python/torch versions verified.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["audit", "prepare"])
    parser.add_argument("--group", choices=["inference", "training", "all"], default="inference")
    parser.add_argument("--from-root", type=Path, help="Preserved YierPet root; read only")
    parser.add_argument("--models-zip", type=Path)
    parser.add_argument("--approved-zip", type=Path)
    parser.add_argument("--runtime", action="store_true", help="Also audit existing upstream/runtime; does not load models")
    args = parser.parse_args()
    source = (args.from_root or ROOT).resolve()
    if args.action == "prepare":
        prepare(ROOT, source, args.group, {"models": args.models_zip, "approved": args.approved_zip})
        source = ROOT
    errors = [item["path"] for item in selected(args.group) if not matches(safe_path(source, item["path"]), item)]
    if errors:
        raise ValueError(f"{len(errors)} missing/different assets (first eight): " + ", ".join(errors[:8]))
    print(f"Verified {len(selected(args.group))} {args.group} assets by SHA256.")
    if args.runtime:
        audit_runtime(source, training=args.group != "inference")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        raise SystemExit(f"Voice recovery failed: {error}")
