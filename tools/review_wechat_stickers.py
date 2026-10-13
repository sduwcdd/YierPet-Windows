"""Persist contact-sheet review; keep semantic decisions separate from downloads."""
import json
from pathlib import Path
import numpy as np
from scipy.ndimage import binary_propagation
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'content/wechat'

# Reviewed from original-image contact sheets; this is not a classifier inference.
BUBU = set(map(int, '''3 11 13 15 21 28 30 31 33 36 37 39 42 44 47 53 55 57 60
64 66 70 71 73 76 78 80 81 84 87 90 93 102 103 106 110 111 113 115
121 123 124 128 129 134 144 145 148 151 152 156 158 165 166 171 174 177 178 179
183 189 191 194 197 201 204 208 209 214 215 217 218 221 227 234 239 240
242 247 250 252 254 255 256 257 258 261 262 276 280 282 286 288 290 291 292 294 296 300
302 304 307 310 317 322 326 328 333 334 335 340 343 344 346 349 351 359
364 375 377 380 382 390 391 392 400 405 415
426 427 428 431 432 435 436 441 442 449 451 453 456 459 463 465 466 467 469 471 489 491'''.split()))
PAIRS = set(map(int, '''9 14 25 26 32 34 49 63 65 75 77 83 86 94 95 98 99 105 107 108 109
131 135 137 153 154 160 161 168 170 173 175 181 187 196 203 210 224 226 229 230 231 236
241 243 244 249 259 265 266 267 270 271 279 311 313 315 327 331 336 353 360
363 366 368 378 379 384 393 394 401 402 404 412 433 446 468 475 476 479 487 488 492'''.split()))
# Single poster does not identify these unambiguously; no invented identity.
UNREVIEWED = set()
# These contain multiple Yier figures rather than Yier+Bubu.
SCENES = {35, 101, 112, 167, 277, 387, 423}
BUBU.update({88,295,381})
PAIRS.update({162,306,408})
LEFT_BUBU = set(map(int, '''9 14 25 34 49 65 77 94 95 107 131 135 137 153 154 160 161 168 170 173 175 181 196 203 210 224 226 230 231 236 241 243 244 249 265 266 279 311 313 327 336 353 363 368 378 384 393 394 401 402 404 412 433 446 475 476 479 488 492'''.split()))

NAMES = {7:'一二发射爱心',15:'布布拍照',19:'一二盖被子',25:'背靠背',29:'一二站立',38:'一二喝饮料',39:'布布喝饮料',
44:'布布扭动',52:'一二抱臂',54:'一二小手',55:'布布微笑',56:'一二比心',68:'一二流泪',79:'一二比心回应',
94:'一起泡澡',95:'一起过生日',129:'布布送花',131:'牵手靠近',137:'一二给布布洗头',150:'一二舒展',
154:'抱抱',163:'一二站立',166:'布布舞动',181:'相拥',191:'布布趴下',210:'贴贴依偎',221:'布布荡秋千',
224:'一二趴在布布头上',229:'抱抱靠肩',231:'亲吻回应',233:'一二看手机',234:'布布流泪',243:'亲亲脸颊',
259:'背后抱抱',271:'面对面亲吻',290:'布布阅读',291:'布布阅读',304:'布布发射爱心',306:'一起乘热气球',313:'亲密贴脸',
326:'布布献玫瑰',331:'喂饮料',336:'亲亲',337:'一二小手',344:'布布飞吻',354:'一二转身',
362:'一二写字',368:'一起洗头',379:'牵着走',402:'一起玩水',406:'一二亲亲',409:'一二荡秋千',
431:'布布捧脸',432:'布布坐着',436:'布布用电脑',437:'一二伏案',446:'一起出发',454:'一二站立',
467:'布布用电脑',475:'一二戳布布',479:'布布趴在一二头上',481:'一二抖动',482:'一二用电脑',
483:'一二伸手',485:'一二喝饮料',487:'一起玩游戏',488:'一二给布布洗头',491:'布布捧脸',492:'一起泡澡'}


def baseline():
    (OUT/'baseline').mkdir(exist_ok=True)
    with Image.open(ROOT/'docs/art-direction/preview-v3.png') as image:
        rgba = np.array(image.convert('RGBA'))
    rgb = rgba[:,:,:3]
    white = (rgb.min(axis=2) >= 245) & ((rgb.max(axis=2).astype(int)-rgb.min(axis=2)) <= 10)
    seed = np.zeros_like(white); seed[0,:]=white[0,:]; seed[-1,:]=white[-1,:]; seed[:,0]=white[:,0]; seed[:,-1]=white[:,-1]
    background = binary_propagation(seed, mask=white)
    rgba[background,3] = 0
    # Approved layout: Bubu left, Yier right. Split only in empty inter-character gap.
    for name, limits in [('bubu',(0,773)),('yier',(773,rgba.shape[1]))]:
        part = Image.fromarray(rgba[:,limits[0]:limits[1]])
        bounds = part.getbbox()
        part.crop(bounds).save(OUT/'baseline'/f'{name}.png')


def main():
    path = OUT/'catalog.json'
    catalog = json.loads(path.read_text(encoding='utf-8'))
    interactions=[]
    for a in catalog['assets']:
        n = int(a['id'].split('-')[1])
        role = 'unreviewed' if n in UNREVIEWED else 'scene' if n in SCENES else 'pair' if n in PAIRS else 'bubu' if n in BUBU else 'yier'
        a.update(role=role, reviewed=role != 'unreviewed', reviewScope='contact-sheet identity; curated interactions and initially ambiguous assets also checked across frames')
        a['name'] = NAMES.get(n, {'yier':'一二','bubu':'布布','pair':'双人','scene':'场景','unreviewed':'待确认'}[role]+' · '+a['id'])
        if role in ('pair','scene'):
            interactions.append({'id':'hug' if n==154 else 'nuzzle' if n==210 else f'pair-{n:04}',
                'name':a['name'], 'mode':'scene' if role=='scene' else 'composite','assetIds':[a['id']],
                'leftCharacter':'bubu' if n in LEFT_BUBU else 'yier',
                'automatic':n in (25,94,131,154,210,243,259,271,313,402,492),
                'note':'独立场景播放，保留原画面，不推断一二布布互动。' if role=='scene' else '保留同一原始 GIF 内两只角色的动作联系与全部帧。左右身份依预览帧标注；重叠时按所在半区拖动。'})
    interactions += [
        {'id':'hearts-response','name':'一二发射爱心 → 布布回应','mode':'response','assetIds':['wx-0007','wx-0304'],
         'leftCharacter':'yier','automatic':True,'note':'两份原图均从举起小道具过渡到前方出现粉色爱心；顺序播放形成回应。这是应用编排，不是文章已有的连续剧情。'},
        {'id':'kiss-response','name':'布布飞吻 → 一二比心','mode':'response','assetIds':['wx-0344','wx-0079'],
         'leftCharacter':'yier','automatic':True,'note':'原图飞吻与比心动作的应用层语义配对；不生成新画面。'},
        {'id':'take-photo','name':'布布拍照 → 一二摆姿势','mode':'response','assetIds':['wx-0015','wx-0079'],
         'leftCharacter':'bubu','automatic':False,'note':'拍照、比心依序编排；不是原文章剧情。'}]
    states={'yier':{'Waving':'wx-0483','Failed':'wx-0068','Waiting':'wx-0437','Running':'wx-0482'},
            'bubu':{'Failed':'wx-0234','Waiting':'wx-0432','Running':'wx-0467','Review':'wx-0290'}}
    path.write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
    (OUT/'interactions.json').write_text(json.dumps({'schemaVersion':1,'interactions':interactions,'states':states},ensure_ascii=False,indent=2),encoding='utf-8')
    baseline()
    # Sample multiple frames of the interactions that will run automatically.
    chosen=sorted({int(i.split('-')[1]) for r in interactions if r['automatic'] or r['mode']=='response' for i in r['assetIds']})
    canvas=Image.new('RGB',(1000,len(chosen)*150),'#e5e9ed');draw=ImageDraw.Draw(canvas)
    for row,n in enumerate(chosen):
        a=catalog['assets'][n-1]; draw.text((2,row*150+4),a['id'],fill='black')
        for j,t in enumerate([0,.25,.5,.75]):
            k=min(a['frameCount']-1,int(a['frameCount']*t));p=next(p for p in a['pages'] if p['firstFrame']<=k<p['firstFrame']+p['frameCount']);local=k-p['firstFrame']
            with Image.open(OUT/p['file']) as sheet:
                x=local%p['columns']*a['width'];y=local//p['columns']*a['height']
                frame=sheet.crop((x,y,x+a['width'],y+a['height']));frame.thumbnail((200,140))
                canvas.paste(frame,(100+j*220,row*150),frame)
    # Several images keep review readable rather than one extremely tall file.
    for start in range(0,len(chosen),7):
        canvas.crop((0,start*150,1000,min(len(chosen),start+7)*150)).save(OUT/'review'/f'interactions-{start//7+1}.png')
    counts={r:sum(a['role']==r for a in catalog['assets']) for r in ('yier','bubu','pair','scene','unreviewed')}
    print(json.dumps({'roles':counts,'interactions':len(interactions),'automatic':sum(r['automatic'] for r in interactions)},ensure_ascii=True))


if __name__=='__main__':main()
