"""Choose a provisional configuration on development phrases, preserving all trials."""
import json,sys
import torch,yaml
from train_pipeline import RUN,REPO,digest,write

sys.path[:0]=[str(REPO),str(REPO/'GPT_SoVITS')]
scores=[]
for name in ('asr-evaluation.json','asr-checkpoint-comparison.json'):
    scores+=json.loads((RUN/name).read_text(encoding='utf-8'))['aggregate']
selected={}
for role in ('yier','bubu'):
    candidates=[s for s in scores if s['role']==role and s['mode']!='base']
    score=min(candidates,key=lambda s:(s['character_edits'],s['mode']!='gpt5-sovits2'))
    result=json.loads((RUN/role/f'samples/{score["mode"]}-results.json').read_text(encoding='utf-8'))
    info={}
    for key in ('gpt','sovits'):
        path=result[key]
        assert digest(path)==result[key+'_sha256']
        model=torch.load(path,map_location='cpu',weights_only=False)
        assert all(torch.isfinite(t).all() for t in model['weight'].values())
        info[key]=dict(all_finite=True,checkpoint_info=model.get('info'))
        del model
    selected[role]=dict(mode=score['mode'],gpt=result['gpt'],sovits=result['sovits'],
        gpt_sha256=result['gpt_sha256'],sovits_sha256=result['sovits_sha256'],
        validation=info,development_asr=score,
        selection_basis='Lowest ASR character edits among tested combinations containing a role-trained component on three development phrases; not an independent test or voice similarity rating.',
        subjective_voice_quality='awaiting user listening')
    cfg=yaml.safe_load((RUN/role/'inference.yaml').read_text(encoding='utf-8'))
    cfg['custom']['t2s_weights_path']=result['gpt']
    cfg['custom']['vits_weights_path']=result['sovits']
    (RUN/role/'inference.yaml').write_text(yaml.safe_dump(cfg),encoding='utf-8')
write(RUN/'selected-models.json',selected)
for role,s in selected.items():
    print(role,s['mode'],s['development_asr']['character_edits'],'/',s['development_asr']['reference_characters'])
