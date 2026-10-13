"""Compile reviewed descriptions and explicit pairings. No image generation or edits."""
import csv
import json
from pathlib import Path
from sticker_spatial import annotations, apply_rules

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / 'content/wechat'


def read(name):
    return json.loads((CONTENT / name).read_text(encoding='utf-8'))


def write(name, data):
    (CONTENT / name).write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def wx(number):
    return f'wx-{number:04}' if isinstance(number, int) else number


def main():
    base, extra = read('catalog.json'), read('catalog.getstickerpack.json')
    assets = base['assets'] + extra['assets']
    by_id = {a['id']: a for a in assets}
    with (CONTENT / 'semantics.tsv').open(encoding='utf-8', newline='') as stream:
        rows = list(csv.DictReader(stream, delimiter='\t'))
    assert len(rows) == len(by_id) == 522
    assert len({r['id'] for r in rows}) == 522
    assert {r['id'] for r in rows} == set(by_id)
    assert all(all(r[k].strip() for k in ('observedAction', 'partnerPossibility', 'category')) for r in rows)

    # Visual corrections from time-spaced frames plus denser review of ambiguous cases.
    roles = {44: 'pair', 83: 'yier', 188: 'pair', 321: 'scene', 328: 'pair', 343: 'pair', 453: 'scene'}
    rules = read('interactions.json')
    for number, role in roles.items():
        asset = by_id[wx(number)]
        asset['role'] = role
        asset['reviewScope'] = '2026-10-11 time-spaced semantic review; corrections confirmed in denser source samples where needed'
        if role == 'yier':
            rules['interactions'] = [r for r in rules['interactions'] if asset['id'] not in r['assetIds']]
        elif not any(asset['id'] in r['assetIds'] for r in rules['interactions']):
            rules['interactions'].append(dict(id=f'pair-{number:04}', name=f'原图场景 · {asset["id"]}',
                mode='scene' if role == 'scene' else 'composite', assetIds=[asset['id']],
                leftCharacter='bubu' if number in (44, 328, 343) else 'yier', automatic=False,
                note='多时点复核后纠正人物分类，保留完整原动画，不拆出重复角色。'))
    for number, name in ((150, '一二走路 · 原表情'), (183, '布布走路 · 原表情')):
        by_id[wx(number)].update(name=name, isLocomotion=True)
    by_id[wx(83)]['name'] = '一二吃大饼'
    for row in rows:
        if int(row['id'][3:]) in roles and row['id'].startswith('wx-') and row['id'] != wx(83):
            by_id[row['id']]['name'] = row['observedAction'].split('，')[0].rstrip('。')
    write('catalog.json', base)
    write('interactions.json', rules)
    # Empty compatibility catalogs overwrite already deployed generated entries.
    write('catalog.generated-motion.json', dict(schemaVersion=1, assets=[], retiredReason='用户指定改用 wx-0150 / wx-0183；生成移动不再加载。'))
    write('interactions.generated-motion.json', dict(schemaVersion=1, interactions=[], states={}))

    links = []

    def link(name, yier, bubu, category, triggers=None, mode='simultaneous', **options):
        ids = [wx(yier), wx(bubu)]
        assert by_id[ids[0]]['role'] == 'yier', ids
        assert by_id[ids[1]]['role'] == 'bubu', ids
        key = f'sem-{ids[0]}-{ids[1]}'
        assert not any(r['id'] == key for r in links)
        links.append(dict(id=key, name=name, mode=mode, assetIds=ids,
            triggerIds=[wx(i) for i in triggers] if triggers is not None else ids,
            leftCharacter='yier', automatic=False, durationSeconds=5 if mode == 'chase' else 4,
            note=f'{category}：根据两份原素材编排的互动设想，并非来源文章中的连续剧情。', **options))

    for y, b in [(4,53),(19,261),(357,53),(117,215),(342,37),(339,215),(246,'gsp-0014')]:
        link('一起晚安', y, b, '双方各自在原图中休息')
    for y,b in [(83,250),(120,33),(18,66),(91,197),(159,11),(473,489),(490,288),(149,400),(477,33)]:
        link('一起吃东西', y, b, '各自吃自己的食物')
    for y,b in [(38,39),(38,346),(50,151),(119,102),(485,102),(213,151),(96,252),(455,64),
                (297,102),(386,102),(361,257),(263,151)]:
        link('一起喝饮料', y, b, '各自捧杯或饮料瓶同时喝')
    for y,b in [(482,467),(362,290),(482,'gsp-0015'),(482,436),(486,291)]:
        link('各自忙碌，互相陪伴', y, b, '工作、写字或读书')
    for y,b in [(7,304),(338,344),(406,344),(79,326),(56,129),(72,326),(478,'gsp-0024')]:
        link('互相送出心意', y, b, '爱心、飞吻或送花相互回应')
    for y,b in [(68,326),(325,129),(211,326),(397,129),(202,326),(323,326)]:
        link('布布送花安慰一二', y, b, '一二流泪或低落，布布举花', triggers=[y])
    for y,b in [(72,234),(56,'gsp-0019'),(56,'gsp-0018'),(72,491)]:
        link('一二安慰布布', y, b, '布布难过，一二捧花或比心', triggers=[b])
    for y,b in [(79,15),(85,15),(235,15),(205,15),(350,15)]:
        link('布布拍照，一二摆姿势', y, b, '相机与造型分别来自两张原图')
    for y,b in [(157,55),(157,78)]:
        link('一二拍照，布布摆姿势', y, b, '一二举相机，布布微笑或做表情')
    for y,b in [(27,60),(176,239),(97,21),(97,152),(332,152),('gsp-0028','gsp-0029')]:
        link('一个表达，一个点头', y, b, '讲解或请求与点头、同意呼应')
    link('布布讲解，一二提出不同意见', 'gsp-0026',152, '教鞭与“不对不对”形成意见回应')
    link('一二行礼，布布说免礼',163,456,'两个文字表情构成招呼与回应')
    for y,b in [(222,28),(474,405),(233,115),(69,178),(434,28),(420,28),(312,405)]:
        link('各自看手机，隔空聊天',y,b,'手机动作的聊天编排设想')
    for name,y,b,reason in [
        ('一起荡秋千',409,221,'两份原图各自荡秋千'),
        ('一起洗衣物',285,242,'红黄头巾，各自在泡沫盆前搓洗'),
        ('一起拖地',484,307,'红黄头巾，各自用拖把擦地'),
        ('一起泡澡',193,286,'各自泡在木桶里'),
        ('一起泡澡',316,286,'各自坐在浴盆或木桶里'),
        ('一起吹风消暑',284,280,'两人各自拿小风扇'),
        ('一起扇风',268,280,'一二扇子与布布风扇'),
        ('带上泳圈出发',303,333,'粉色与蓝色泳圈'),
        ('同款泳圈',458,294,'两人同款黄绿泳圈'),
        ('一起扮小鸟',23,113,'对应黄色鸟形帽衫'),
        ('一起扮狮子',324,247,'对应狮子头套'),
        ('小鸡一起出壳',46,381,'各自从蛋壳里冒出来'),
        ('一起扮小羊',440,218,'对应白色羊头套'),
        ('一起扮小猫',403,70,'对应灰色动物帽衫'),
        ('一起舞狮庆祝',8,128,'对应舞狮头帽'),
        ('一起扮木乃伊',12,351,'各自裹绷带探头'),
        ('一起怕冷打喷嚏',352,335,'同款帽子围巾、同类喷嚏动作'),
        ('一起戴围巾做鬼脸',309,349,'同款红围巾表情'),
        ('星星头饰和围巾',439,204,'同款星星头饰、红围巾'),
        ('一起蹦跳',398,292,'同款黄垫子、红围巾'),
        ('一起在桌边探头',418,415,'相同桌边探头动作'),
        ('门后与墙角捉迷藏',314,'gsp-0030','两人各自在遮挡物后探头'),
        ('一起打卡',125,88,'两张各自角色的打卡机表情'),
        ('一起展示蓝色牌',419,428,'两张相应展示牌，仅图像无支付功能'),
        ('一起展示绿色牌',421,435,'两张相应展示牌，仅图像无支付功能'),
        ('一起骑小摩托',200,310,'对应道路、头盔和小摩托'),
        ('一起出来走走',447,317,'各自戴帽背包出游'),
        ('一起挂在晾衣绳上',472,57,'两张同构夸张趣味场景'),
        ('布布送糖，一二吃糖',417,441,'大号棒棒糖与吃棒棒糖动作'),
        ('互相打招呼',219,134,'粉蓝衬衣、抬手招呼'),
        ('一起跳舞',483,166,'两人抬手摆动身体'),
        ('一起累瘫',40,110,'两人各自趴椅子'),
        ('一起趴下休息',389,145,'两人由站立弯腰到趴地'),
        ('一起捧脸发呆',411,431,'托脸与侧脸手势'),
        ('一二逗趣，布布笑',414,302,'卷哨玩笑与布布仰头笑'),
        ('一起哈哈笑','gsp-0027',258,'大笑与得意笑'),
    ]:
        link(name,y,b,reason)

    # Only the prop-holder initiates pursuit; ordinary Bubu walking never starts it.
    for y,name,mirror_left,mirror_right in [
        (452,'一二拖锤追，布布在前面跑',[wx(452)],[wx(183)]),
        (82,'一二挥道具追，布布躲',[],[wx(183)]),
        (195,'一二举拖鞋状道具，布布跑开',[],[wx(183)]),
        (212,'一二扮恐龙，布布跑开',[],[wx(212),wx(183)]),
        (232,'一二穿恐龙装追，布布跑开',[],[wx(232),wx(183)]),
        (369,'恐龙装追逐游戏',[],[wx(369),wx(183)]),
    ]:
        link(name,y,183,'保持一二在后、布布在前的屏幕移动；不新增动作帧',triggers=[y],mode='chase',
            mirrorWhenMovingLeft=mirror_left,mirrorWhenMovingRight=mirror_right)

    spatial = annotations(assets)
    for row in rows:
        row.update(spatial[row['id']])
    extra_rules = read('interactions.getstickerpack.json')
    for group in (rules['interactions'], extra_rules['interactions'], links):
        apply_rules(group, spatial)
    write('interactions.json', rules)
    write('interactions.getstickerpack.json', extra_rules)
    write('interactions.semantic.json',dict(schemaVersion=1,interactions=links,
        states={'yier':{'RunningLeft':wx(150),'RunningRight':wx(150)},
                'bubu':{'RunningLeft':wx(183),'RunningRight':wx(183)}},
        stateMirrors={'yier':{'RunningLeft':False,'RunningRight':True},
                      'bubu':{'RunningLeft':False,'RunningRight':True}}))
    uncertain = {16,101,127,140,143,167,171,199,208,269,298,322,341,370,375,376,388,430,449}
    entries=[]
    for row in rows:
        identity = row['id']
        matches=[]
        for rule in links:
            if identity in rule['assetIds']:
                other = next(i for i in rule['assetIds'] if i != identity)
                matches.append(dict(assetId=other,interactionId=rule['id'],mode=rule['mode'],
                    triggersPartner=identity in rule['triggerIds'],reason=rule['note']))
        entries.append(dict(**row,role=by_id[identity]['role'],
            reviewStatus='partial-uncertainty' if identity.startswith('wx-') and int(identity[3:]) in uncertain else 'sampled-reviewed',
            reviewEvidence='review/semantics/review-%02d.png' % (len(entries)//24+1),
            matches=matches,matchStatus='curated-pairing' if matches else 'no-enabled-solo-pairing'))
    write('semantics.json',dict(schemaVersion=1,reviewScope='原素材四个时长采样点；疑点额外密集取帧；回应均为应用编排设想。',entries=entries))
    rows_md=['# 一二布布表情逐项说明与关联', '',
        f'522 张原素材，{len(links)} 组单人素材联动。生成移动已退出素材库。', '',
        '“画面动作”来自抽样帧审阅；“另一方可能的回应”是编排设想。只有列出的组合已接入运行时。未配对不等于另一方素材一定不存在；局部物品或角色有疑点的条目在说明中保留不确定性。', '',
        '表情库可按编号、动作或回应检索，并预览原图。对应审阅联系表在 content/wechat/review/semantics/。', '',
        '左右均以用户看到的屏幕为准。站位要求区分固定左右、与左右无关、随追逐方向变化；转圈动画按开场位置入场，原动画中的换位保留。单人动作的朝向用于编排推断，不代表来源中已有该双人剧情。', '',
        '| 编号 | 画面动作 | 另一方可能的回应 | 左右位置与入场要求 | 已接入的另一方素材 |',
        '| --- | --- | --- | --- | --- |']
    for item in entries:
        candidates=', '.join(dict.fromkeys(m['assetId'] for m in item['matches'])) or '暂无单人联动'
        rows_md.append(f'| {item["id"]} | {item["observedAction"]} | {item["partnerPossibility"]} | {item["positionNote"]} | {candidates} |')
    rows_md += ['', '## 每组互动的实际入场站位', '',
        '单人动作本身可以与左右无关，但选中有方向要求的搭配时，以本表的整组要求为准。所有互动都在双方到位后才开始播放。', '',
        '| 互动编号 | 素材 | 入场要求 |', '| --- | --- | --- |']
    for rule in rules['interactions'] + extra_rules['interactions'] + links:
        rows_md.append(f'| {rule["id"]} | {", ".join(rule["assetIds"])} | {rule["positionNote"]} |')
    (ROOT/'docs/art-direction/sticker-semantics.md').write_text('\n'.join(rows_md)+'\n',encoding='utf-8')
    print(json.dumps(dict(assets=len(assets),descriptions=len(entries),semanticInteractions=len(links),
        linkedAssets=len({i for r in links for i in r['assetIds']}),chases=sum(r['mode']=='chase' for r in links),
        remainingAmbiguousDetails=sum(e['reviewStatus']=='partial-uncertainty' for e in entries)),ensure_ascii=False))


if __name__ == '__main__':
    main()
