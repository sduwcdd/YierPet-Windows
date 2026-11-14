"""Verify finished epochs, finite tensors, and actual weight changes vs base."""
import json,torch,sys
from pathlib import Path
from train_pipeline import RUN,PRE,REPO,digest,write
sys.path[:0]=[str(REPO),str(REPO/'GPT_SoVITS')]

provenance=json.loads((RUN/'provenance.json').read_text(encoding='utf-8'))
checks={}
for role in ('yier','bubu'):
    checks[role]={}
    for stage in ('s1','s2'):
        receipt=json.loads((RUN/role/f'train-{stage}.result.json').read_text())
        assert receipt['exit_code']==0
        if stage=='s1':
            path=RUN/role/f'{role}-e{provenance["s1_epochs"]}.ckpt'
            base_path=PRE/'gsv-v2final-pretrained/s1bert25hz-5kh-longer-epoch=12-step=369668.ckpt'
        else:
            path=next((RUN/role).glob(f'{role}_e{provenance["s2_epochs"]}_s*.pth'))
            base_path=PRE/'gsv-v2final-pretrained/s2G2333k.pth'
        trained=torch.load(path,map_location='cpu',weights_only=False)
        base=torch.load(base_path,map_location='cpu',weights_only=False)
        common=0;changed=0
        for name,tensor in trained['weight'].items():
            assert torch.isfinite(tensor).all(),(role,stage,name)
            if name in base['weight'] and base['weight'][name].shape==tensor.shape:
                common+=1
                changed+=not torch.equal(tensor,base['weight'][name].to(tensor.dtype))
        assert changed>0,(role,stage,'no changed tensors')
        checks[role][stage]=dict(file=str(path),bytes=path.stat().st_size,sha256=digest(path),info=trained.get('info'),common_tensors=common,changed_tensors=changed,all_finite=True,training_seconds=receipt['seconds'])
        del trained,base
    s1_checkpoints=REPO/f'logs/{role}-approved-v2/logs_s1_v2/ckpt'
    checkpoint_path=max(s1_checkpoints.glob('*.ckpt'),key=lambda p:p.stat().st_mtime)
    checkpoint=torch.load(checkpoint_path,map_location='cpu',weights_only=False)
    assert checkpoint['epoch']+1==provenance['s1_epochs']
    assert checkpoint['global_step']>0
    checks[role]['gpt_final_epoch']=checkpoint['epoch']+1
    checks[role]['gpt_optimizer_steps']=checkpoint['global_step']
    del checkpoint
write(RUN/'model-verification.json',checks)
print(json.dumps(checks,ensure_ascii=False,indent=2))
