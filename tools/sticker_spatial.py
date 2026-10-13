"""Compile visually reviewed screen positions, separately from action semantics."""
import json
from pathlib import Path

CONTENT = Path(__file__).resolve().parents[1] / 'content/wechat'


def identity(value):
    return f'wx-{value:04}' if isinstance(value, int) else value


def annotations(assets):
    review = json.loads((CONTENT / 'spatial-review.json').read_text(encoding='utf-8'))
    fixed_y = {identity(i) for i in review['fixedYierLeft']}
    fixed_b = {identity(i) for i in review['fixedBubuLeft']}
    any_pair = {identity(i) for i in review['unconstrainedPair']}
    solo_y = {identity(i) for i in review['soloYierLeft']}
    solo_b = {identity(i) for i in review['soloBubuLeft']}
    dynamic = {identity(int(k)): v for k, v in review['dynamic'].items()}
    notes = {identity(int(k)) if k.isdigit() else k: v for k, v in review['notes'].items()}
    directional = {
        'wx-0003': '布布的游戏道具朝左，若与另一方对向玩耍，应一二在左、布布在右。',
        'wx-0015': '布布举相机朝右拍照，拍一二时布布在左、一二在右。',
        'wx-0027': '一二在黑板左侧向右讲解，回应者布布应在右：一二在左、布布在右。',
        'wx-0139': '一二的游戏道具朝右，若与另一方对向玩耍，应一二在左、布布在右。',
        'wx-0152': '布布持教鞭朝右讲解，回应者一二应在右：布布在左、一二在右。',
        'wx-0157': '一二举相机朝右拍照，拍布布时一二在左、布布在右。',
        'wx-0176': '一二在黑板左侧向右讲解，布布应在右听讲：一二在左、布布在右。',
        'wx-0182': '一二伸手指向画面右侧，若指向布布，应一二在左、布布在右。',
        'wx-0326': '布布把玫瑰举在画面左侧，递给一二时一二在左、布布在右。',
        'wx-0414': '一二的卷哨展开朝右，逗布布时一二在左、布布在右。',
        'wx-0441': '布布将大棒棒糖伸向画面左侧，递给一二时一二在左、布布在右。',
        'wx-0445': '一二手势指向画面左侧，若对布布做这个动作，应布布在左、一二在右。',
    }
    chasers = {identity(i) for i in (82, 195, 212, 232, 369, 452)}
    result = {}
    for index, asset in enumerate(assets):
        id, role = asset['id'], asset['role']
        policy, kind = 'any', 'single'
        if role in ('pair', 'scene'):
            assert id in fixed_y | fixed_b | any_pair | dynamic.keys(), f'Unreviewed scene: {id}'
            kind = 'overlap-or-scene' if id in any_pair else 'side-by-side'
            if id in fixed_y:
                policy = 'yier-left'
                note = '画面中一二在左、布布在右；互动前先走到一二左、布布右的位置。'
            elif id in fixed_b:
                policy = 'bubu-left'
                note = '画面中布布在左、一二在右；互动前先走到布布左、一二右的位置。'
            elif id in dynamic:
                policy, kind = dynamic[id]['left'] + '-left', 'changes-during-clip'
                note = dynamic[id]['note']
            else:
                note = '画面以居中重叠或上下关系为主，与左右无关；保持当前左右顺序靠近即可。'
            note = notes.get(id, note)
        elif id in solo_y | solo_b:
            policy = 'yier-left' if id in solo_y else 'bubu-left'
            kind, note = 'directional-solo', directional[id]
        elif id in chasers or id == 'wx-0183':
            policy, kind = 'chase', 'direction-dependent'
            note = '追逐时布布在前、一二在后：向左走时布布在左，向右走时一二在左；先按行进方向站位。独立播放与左右无关。'
        else:
            who = '一二' if role == 'yier' else '布布'
            note = f'本条按{who}单人动作使用，未要求另一方在固定一侧，与左右无关；配对时以所选联动的站位说明为准。'
        note = note.replace('与双人左右无关', '与左右无关')
        if policy == 'any' and '与左右无关' not in note:
            note += '互动入场与左右无关，保持当前左右顺序即可。'
        result[id] = dict(positionRequirement=policy, positionNote=note, sourceLayout=kind,
            positionEvidence=f'review/semantics/review-{index // 24 + 1:02}.png')
    assert not fixed_y & fixed_b and not solo_y & solo_b
    assert set(directional) == solo_y | solo_b
    return result


def apply_rules(rules, spatial):
    for rule in rules:
        if rule['mode'] == 'chase':
            policy = 'chase'
            note = '先按行进方向站位：向左时布布左、一二右；向右时一二左、布布右，始终布布在前、一二在后。'
        else:
            required = {spatial[i]['positionRequirement'] for i in rule['assetIds']} & {'yier-left', 'bubu-left'}
            assert len(required) <= 1, f'Conflicting directions need manual review: {rule["id"]}'
            policy = next(iter(required)) if required else 'any'
            note = ('与左右无关，保持当前左右顺序靠近后互动。' if policy == 'any' else
                ('互动前先走到一二在左、布布在右。' if policy == 'yier-left' else '互动前先走到布布在左、一二在右。'))
            if len(rule['assetIds']) == 1:
                note = spatial[rule['assetIds'][0]]['positionNote']
            elif required:
                note += ' ' + ' '.join(dict.fromkeys(spatial[i]['positionNote'] for i in rule['assetIds']
                    if spatial[i]['positionRequirement'] == policy))
        rule.update(positionRequirement=policy, positionNote=note)
        if policy in ('yier-left', 'bubu-left'):
            rule['leftCharacter'] = policy.split('-')[0]
