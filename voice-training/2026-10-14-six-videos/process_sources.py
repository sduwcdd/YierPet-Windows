from pathlib import Path
import os, json, hashlib, subprocess, argparse
os.environ['HF_HUB_DISABLE_XET'] = '1'
import av, imageio_ffmpeg, numpy as np
from faster_whisper import WhisperModel

OUT = Path(__file__).resolve().parent
ROOT = OUT.parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--model', choices=['small', 'turbo'], default='small')
args = parser.parse_args()
asr_dir = OUT / ('asr' if args.model == 'small' else 'asr-turbo')
(OUT / 'audio-original').mkdir(exist_ok=True)
asr_dir.mkdir(exist_ok=True)
rows = []
for source in sorted(ROOT.glob('*.mp4')):
    target = OUT / 'audio-original' / (source.stem + '.wav')
    with av.open(str(source)) as container:
        audio = container.streams.audio[0]
        row = dict(file=source.name, sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                   duration=float(container.duration/av.time_base), audio_codec=audio.codec_context.name,
                   sample_rate=audio.codec_context.sample_rate, channels=audio.codec_context.channels)
    if not target.exists():
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error',
                        '-n', '-i', str(source), '-map', '0:a:0', '-vn', '-c:a', 'pcm_s16le', str(target)], check=True)
    row['audio_sha256'] = hashlib.sha256(target.read_bytes()).hexdigest()
    rows.append(row)
    print('EXTRACTED', json.dumps(row), flush=True)
(OUT / 'sources.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
print('LOADING_ASR_MODEL', args.model, '/ CPU int8', flush=True)
model_cache = 'models--Systran--faster-whisper-small/snapshots/536b0662742c02347bc0e980a01041f333bce120' if args.model == 'small' else 'models--mobiuslabsgmbh--faster-whisper-large-v3-turbo/snapshots/0a363e9161cbc7ed1431c9597a8ceaf0c4f78fcf'
model = WhisperModel(str(Path.home()/'.cache/huggingface/hub'/model_cache),
                     device='cpu', compute_type='int8', cpu_threads=6, local_files_only=True)
for row in rows:
    source = OUT / 'audio-original' / (Path(row['file']).stem + '.wav')
    result_path = asr_dir / (source.stem + '.json')
    if result_path.exists(): continue
    decoded = subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error',
        '-i', str(source), '-f', 'f32le', '-ac', '1', '-ar', '16000', 'pipe:1'], check=True, capture_output=True).stdout
    samples = np.frombuffer(decoded, dtype='<f4').copy()
    segments, info = model.transcribe(samples, language='zh', beam_size=5,
        word_timestamps=True, vad_filter=False, condition_on_previous_text=False)
    records=[]
    for segment in segments:
        item=dict(start=segment.start, end=segment.end, text=segment.text,
                  avg_logprob=segment.avg_logprob, no_speech_prob=segment.no_speech_prob,
                  words=[dict(start=w.start,end=w.end,text=w.word,probability=w.probability) for w in segment.words])
        records.append(item)
        print(source.stem, json.dumps({k:item[k] for k in ('start','end','text')}, ensure_ascii=True), flush=True)
    result_path.write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
print('ASR_COMPLETE',flush=True)
