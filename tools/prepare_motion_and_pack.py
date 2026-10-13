"""Review supplemental originals, then reapply the current original-only movement rules."""
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'content/wechat'

def save(name,obj):
    (OUT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2),encoding='utf8')

def main():
    path=OUT/'catalog.getstickerpack.json'
    catalog=json.loads(path.read_text(encoding='utf8'))
    pairs={9,10,11,12,13,16,20,21,22,25}
    yier={5,26,27,28}
    left_bubu={10,11,12,16,20,22}
    names=['布布柴犬装','布布鸭子装','布布小熊装','布布熊猫装','一二奶牛装','布布背影','布布晃晃','布布鹅鹅',
           '背后贴贴','亲亲脸颊','一起摇摆','背后抱抱','一起躺下','布布晚安','布布工作','贴贴依偎','布布委屈',
           '布布害羞','布布哭哭','枕头互动','轻轻推推','哄布布','布布好运','布布送花','抱抱安慰','一二说不对',
           '一二大笑','一二拜托','布布戴帽子','布布躲猫猫']
    rules=[]
    for n,a in enumerate(catalog['assets'],1):
        a.update(name=names[n-1],role='pair' if n in pairs else 'yier' if n in yier else 'bubu',reviewed=True,
                 reviewScope='visually checked every original PNG; all 30 are static')
        if n in pairs:
            rules.append({'id':f'gsp-pair-{n:04}','name':names[n-1]+' · 原图','mode':'composite','assetIds':[a['id']],
                          'leftCharacter':'bubu' if n in left_bubu else 'yier','automatic':False,
                          'note':'原文件为静态 PNG；只显示原图，不生成动作中间帧。'})
    save('catalog.getstickerpack.json',catalog)
    save('interactions.getstickerpack.json',{'schemaVersion':1,'interactions':rules,'states':{}})

    # The user rejected generated walking. This legacy entry point must never
    # reintroduce it when a supplemental pack is reimported.
    import prepare_semantic_links
    prepare_semantic_links.main()

if __name__=='__main__':main()
