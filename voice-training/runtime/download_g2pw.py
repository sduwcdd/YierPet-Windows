from pathlib import Path
import zipfile, json
import download_models as d


def main():
    d.ROOT=Path(__file__).parent/'GPT-SoVITS/GPT_SoVITS/text'
    recovery=Path(__file__).resolve().parents[1]/'recovery'
    item=json.loads((recovery/'g2pw.json').read_text(encoding='utf-8'))
    target=d.ROOT/'G2PWModel'
    if target.exists():
        lock=json.loads((recovery/'upstream-lock.json').read_text(encoding='utf-8'))
        required=[a for a in lock['frontendAssets'] if '/G2PWModel/' in a['path']]
        for asset in required:
            path=target/Path(asset['path']).name
            if not path.is_file() or path.stat().st_size!=asset['bytes'] or d.sha(path)!=asset['sha256']:
                raise ValueError(f'Existing G2PW file differs; preserve it before repairing: {path}')
        print('G2PW_EXISTING_VERIFIED',flush=True)
        return
    d.download(item)
    with zipfile.ZipFile(d.ROOT/item['path']) as z:
        assert z.testzip() is None
        for n in z.namelist():
            assert (d.ROOT/n).resolve().is_relative_to(d.ROOT.resolve())
        z.extractall(d.ROOT)
    (d.ROOT/'G2PWModel_1.1').rename(target)
    (d.ROOT/'g2pw-download.json').write_text(json.dumps({**item,'sha256':d.sha(d.ROOT/item['path']),'zip_crc_verified':True},indent=2),encoding='utf-8')
    print('G2PW_READY',flush=True)


if __name__=='__main__':
    main()
