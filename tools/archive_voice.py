"""Package selected voices and recovery records; never upload or delete anything."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

from restore_voice import ROOT, configurations, digest, matches, read, safe_path

OUTPUT = ROOT / 'voice-training/archive-20261114'
RUN = ROOT / 'voice-training/trained-20261014-v2'
UPSTREAM = ROOT / 'voice-training/runtime/GPT-SoVITS'
TEXT = {'.json', '.yaml', '.yml', '.txt', '.list', '.log', '.md', '.html', '.py', '.ps1'}
WEIGHTS = {'.ckpt', '.pth', '.onnx', '.bin', '.safetensors'}


def portable(data):
    text = data.decode('utf-8-sig')
    for value in (str(ROOT).replace('\\', '\\\\'), str(ROOT), ROOT.as_posix()):
        text = re.sub(re.escape(value), lambda _: '${PROJECT_ROOT}', text, flags=re.I)
    text = re.sub(r'[A-Za-z]:(?:\\+|/)Users(?:\\+|/)[^\\/\s"<>]+',
                  lambda _: '${USER_HOME}', text, flags=re.I)
    return text.encode('utf-8')


def package(output, name, entries, description):
    target = output / name
    if target.exists():
        raise FileExistsError(f'Archive already exists: {target}')
    manifest = dict(description=description, files=[])
    seen = set()
    with zipfile.ZipFile(target, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for path, entry, override in sorted(entries, key=lambda x: x[1]):
            if entry in seen or Path(entry).is_absolute() or '..' in Path(entry).parts:
                raise ValueError(f'Invalid/duplicate entry: {entry}')
            seen.add(entry)
            data = override
            if data is None and path.suffix.lower() in TEXT:
                data = portable(path.read_bytes())
            if data is None:
                archive.write(path, entry)
                size, sha = path.stat().st_size, digest(path)
            else:
                archive.writestr(entry, data)
                size, sha = len(data), hashlib.sha256(data).hexdigest()
            manifest['files'].append(dict(file=entry, bytes=size, sha256=sha))
        archive.writestr('archive-manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2))
    with zipfile.ZipFile(target) as archive:
        assert archive.testzip() is None, name
        for item in manifest['files']:
            h = hashlib.sha256()
            with archive.open(item['file']) as stream:
                for block in iter(lambda: stream.read(8 * 1024 * 1024), b''): h.update(block)
            assert h.hexdigest() == item['sha256'], item['file']
    result = dict(name=name, bytes=target.stat().st_size, sha256=digest(target), files=len(entries))
    assert result['bytes'] < 2 * 1024**3, 'GitHub Release per-asset limit'
    print(json.dumps(result), flush=True)
    return result


def build(output, models_only=False):
    output.mkdir(parents=True, exist_ok=True)
    assets = read('assets.json')['files']
    for item in assets:
        if models_only and item['group'] != 'inference':
            continue
        if not matches(safe_path(ROOT, item['path']), item):
            raise ValueError(f'Asset differs: {item["path"]}')
    templates = {p: portable(data) for p, data in configurations(ROOT).items()}
    models = [(safe_path(ROOT, a['path']), a['entry'], None) for a in assets if a['group'] == 'inference']
    for role in ('yier', 'bubu'):
        models.append((RUN / role / 'reference.txt', f'{role}/reference.txt', None))
    models.append((None, 'selected-models.json', templates['voice-training/trained-20261014-v2/selected-models.json']))
    results = [package(output, 'selected-models.zip', models,
        'Yier GPT10 + base SoVITS; Bubu GPT10 + SoVITS2. No unused training weights.')]
    if not models_only:
        approved_root = ROOT / 'voice-training/approved-20261014-04205443'
        approved = [(p, p.relative_to(approved_root).as_posix(), templates.get(p.relative_to(ROOT).as_posix()))
                    for p in approved_root.rglob('*') if p.is_file() and '__pycache__' not in p.parts]
        results.append(package(output, 'approved-recordings.zip', approved, 'Reviewed clips, labels and original review.'))
        runtime = []
        for a in read('pretrained-models.json'):
            if a.get('trainingOnly') or a['path'].endswith('/s2G2333k.pth'):
                continue
            p = UPSTREAM / 'GPT_SoVITS/pretrained_models' / a['path']
            assert matches(p, dict(bytes=a['size'], sha256=a['sha256']))
            runtime.append((p, p.relative_to(ROOT).as_posix(), None))
        lock = read('upstream-lock.json')
        for a in lock['frontendAssets']:
            p = safe_path(UPSTREAM, a['path'])
            assert matches(p, a)
            runtime.append((p, p.relative_to(ROOT).as_posix(), None))
        p = UPSTREAM / 'runtime/ffmpeg.exe'
        assert digest(p) == lock['ffmpeg']['sha256']
        runtime.append((p, p.relative_to(ROOT).as_posix(), None))
        results.append(package(output, 'inference-runtime-assets.zip', runtime,
            'Required BERT, HuBERT, G2PW, fastText and FFmpeg; paths relative to project root.'))
        records = [(safe_path(ROOT, a['path']), a['path'], None) for a in assets if a['group'] == 'sources']
        for folder in (ROOT / 'voice-training/2026-10-14-six-videos', RUN,
                       ROOT / 'voice-training/clarity-review-20261014', UPSTREAM / 'logs'):
            for p in folder.rglob('*'):
                if not p.is_file() or '__pycache__' in p.parts or p.suffix.lower() in WEIGHTS:
                    continue
                entry = p.relative_to(ROOT).as_posix()
                records.append((p, entry, templates.get(entry)))
        for p in (ROOT / 'voice-training').iterdir():
            if p.is_file() and p.suffix in {'.json', '.log'}:
                records.append((p, p.relative_to(ROOT).as_posix(), None))
        results.append(package(output, 'sources-and-records.zip', records,
            'Original videos, cuts, comparison audio, curves, logs and features. Extract separately for reference; historical success markers are not a new training run. Model/optimizer weights excluded; personal paths replaced.'))
    (output / 'SHA256SUMS.txt').write_text(''.join(f'{a["sha256"]}  {a["name"]}\n' for a in results), encoding='utf-8')
    (output / 'archives.json').write_text(json.dumps(results, indent=2) + '\n', encoding='utf-8')
    return results


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=OUTPUT)
    parser.add_argument('--models-only', action='store_true')
    args = parser.parse_args()
    build(args.output.resolve(), args.models_only)
