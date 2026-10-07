#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Stage 2 glyph font-pack generator (字库路线 A, see doc/阶段2-字库路线.md).

Converts hanzi-writer-data per-character medians (stroke-order centerlines,
one polyline per stroke, ordered like a person writes) into the AutoWriter
glyph font pack JSON consumed by GlyphFontPackService / TextLayoutService.

Source coordinate system (measured over all 9574 characters of 2.0.1):
  em box 1024 units, Y up, baseline at y=0;
  ink height P50=813 P90=886 max=963 units; ink floor min=-96; ink top max=885.

Calibration (frozen constants, doc section 2):
  cap 880 units -> BodySizeMm 10 mm  (scale = 10/880 = 0.0113636 mm/unit)
  baseline y=0  -> BaselineMm 1.0 mm (same 1..11 body convention as the
                                    built-in glyphs, so line height 14mm works)
  x centered in the 12 mm advance: offset = (EmSizeMm - 1024*scale)/2

Deterministic output: sorted keys, 3-decimal mm rounding, no timestamps.

Usage:
  python scripts/generate-stage2-fontpack.py                # pilot pack (default)
  python scripts/generate-stage2-fontpack.py --all          # whole corpus (9574)
  python scripts/generate-stage2-fontpack.py --chars-file my.txt
"""
import argparse
import json
import math
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SRC = os.path.join(REPO_ROOT, 'artifacts', 'stage2', 'src', 'package')
DEFAULT_OUT = os.path.join(REPO_ROOT, 'GRBL-Plotter', 'AutoWriter', 'Fonts',
                           'hanziwriter-pilot.fontpack.json')
DEFAULT_REPORT = os.path.join(REPO_ROOT, 'artifacts', 'stage2', 'fontpack-report.txt')

SOURCE_NAME = 'hanzi-writer-data 2.0.1 (github.com/skishore/makemeahanzi)'
SOURCE_LICENSE = 'Arphic Public License (ARPHICPL.TXT ships with the pack and in Fonts/LICENSE-arphic.txt)'
SCHEMA_VERSION = 1

SOURCE_EM_UNITS = 1024.0
CAP_UNITS = 880.0
BODY_MM = 10.0
BASELINE_MM = 1.0
EM_MM = 12.0
ADVANCE_CJK_MM = 12.0
ADVANCE_SPACE_MM = 6.0
MM_ROUND = 3

# Pilot charset: 72 lines x 10 common simplified characters (+ numerals),
# 723 unique after dedup. The paper-sample characters 永 and 国 are included.
PILOT_CHARS = """
的一是了我不人在他有
这个上们来到时大地为
子中你说生国年着就那
和要她出也得里后自以
会家可下而过天去能对
小多然于心学么之都好
看起发当没成只如事把
还用第样道想作种开美
总从无情己面最女但现
前些所同日手又行意动
方期它头经长儿回位分
爱老因很给名法间斯知
世什两次使身者被高已
亲其进此话常与活正感
见明问力理尔点文几定
本公特做外孩相西果走
将月十实向声车全信重
三机工物气每并别真打
太新比才便夫再书部水
像眼等体却加电主界门
利海受听表德少克代员
许先口由死安写性马光
白或住难望教命花结乐
色更拉东神记处让母父
应直字场平报友关放至
张认接告入笑内英军候
民岁往何度山觉路带万
男边风解叫任金快原吃
妈变通师立象数四失满
战远格士音轻目条呢病
始达深完今提求清王化
空业思切怎非找片罗钱
吗语元喜曾离飞科言干
流欢约各即指合反题必
该论交终林请医晚制球
决传画保读运及则房早
院量苦火布品近坐产答
星精视五连司巴呀管类
未朋且婚台夜青北队久
乎越观落尽形影红爸百
令周吧识步希亚术留市
半热送兴造谈容极随演
收首根讲整式取照办强
石古华拿计您装似足双
妻尼转诉米称丽客南领
节衣站黑刻统断福城故
历惊脸选包紧争另建维
绝树系伤示愿持千史谁
准联妇纪基买志静阿诗
独复痛消社算义竟确酒
需单治卡幸兰念举仅钟
怕共毛句息功官待究跟
穿室易游程号居考突皮
哪费倒价图具刚脑永歌
响商礼细专黄块脚味灵
改据般破引食仍存众注
笔甚某沉血备习校默务
土微娘须试怀料调广苏
显赛查密议底列富梦错
座参八除跑亮假印设线
温虽掉京初养香停际致
阳纸李纳验助激够严证
帝饭忘趣支春集丈木研
班普导顿睡展跳获艺六
波察群皇段急庭创区奥
器谢弟店否害草排背止
组州朝封睛板角况曲馆
育忙质河续哥呼若推境
遇雨标姐充围案伦护冷
警贝著雪索剧啊船险烟
依斗值帮汉慢佛肯闻唱
沙局伴秋冬雷冰晴晨昏
九七二
"""


def fail(message):
    raise SystemExit('FAIL: ' + message)


def load_charset(args):
    if args.all:
        chars = None
    elif args.chars_file:
        with open(args.chars_file, encoding='utf-8') as handle:
            chars = set(handle.read()) - set(' \t\r\n')
    else:
        chars = set(PILOT_CHARS) - set(' \t\r\n')
    if chars is not None and not chars:
        fail('字符表为空。')
    return chars


def convert_strokes(medians, scale, x_offset, symbol):
    strokes = []
    for index, stroke in enumerate(medians):
        if len(stroke) < 2:
            fail('「%s」第 %d 笔只有 %d 个点（少于 2），拒绝生成。' % (symbol, index + 1, len(stroke)))
        flat = []
        for point in stroke:
            x, y = point[0], point[1]
            if not (math.isfinite(x) and math.isfinite(y)):
                fail('「%s」第 %d 笔含非有限坐标 %r。' % (symbol, index + 1, point))
            flat.append(round(x * scale + x_offset, MM_ROUND))
            flat.append(round(y * scale + BASELINE_MM, MM_ROUND))
        strokes.append(flat)
    return strokes


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--src', default=DEFAULT_SRC, help='hanzi-writer-data package dir')
    parser.add_argument('--out', default=DEFAULT_OUT, help='font pack JSON output path')
    parser.add_argument('--report', default=DEFAULT_REPORT, help='report output path')
    parser.add_argument('--chars-file', default=None, help='one character set file (UTF-8)')
    parser.add_argument('--all', action='store_true', help='convert every character in the source')
    args = parser.parse_args()

    if not os.path.isdir(args.src):
        fail('源数据目录不存在：%s（复现步骤见 doc/阶段2-字库路线.md 第 5 节）' % args.src)

    scale = BODY_MM / CAP_UNITS
    x_offset = (EM_MM - SOURCE_EM_UNITS * scale) / 2.0

    charset = load_charset(args)
    if charset is not None:
        missing = sorted(c for c in charset if not os.path.isfile(os.path.join(args.src, c + '.json')))
        if missing:
            fail('源数据缺 %d 个字：%s（不静默跳过；扩源或修订字符表。）'
                 % (len(missing), ' '.join(missing)))

    glyphs = {}
    total_strokes = 0
    total_points = 0
    ink_min_x = ink_min_y = math.inf
    ink_max_x = ink_max_y = -math.inf

    names = (sorted(os.listdir(args.src)) if charset is None
             else sorted(c + '.json' for c in charset))
    for name in names:
        if not name.endswith('.json') or name == 'package.json':
            continue
        symbol = name[:-5]
        if len(symbol) != 1:
            continue
        with open(os.path.join(args.src, name), encoding='utf-8') as handle:
            data = json.load(handle)
        medians = data.get('medians') or []
        if not medians:
            fail('「%s」没有 medians（笔顺中心线）数据。' % symbol)
        strokes = convert_strokes(medians, scale, x_offset, symbol)
        glyphs[symbol] = {'strokes': strokes}
        total_strokes += len(strokes)
        for flat in strokes:
            total_points += len(flat) // 2
            for i in range(0, len(flat), 2):
                ink_min_x = min(ink_min_x, flat[i])
                ink_max_x = max(ink_max_x, flat[i])
                ink_min_y = min(ink_min_y, flat[i + 1])
                ink_max_y = max(ink_max_y, flat[i + 1])

    # Em-box containment: honest bounds check, never a silent clip.
    tolerance = 0.5
    if ink_min_x < -tolerance or ink_min_y < -tolerance or \
       ink_max_x > EM_MM + tolerance or ink_max_y > EM_MM + tolerance:
        fail('墨迹超出 em 框（容差 %.1fmm）：X %.3f..%.3f  Y %.3f..%.3f'
             % (tolerance, ink_min_x, ink_max_x, ink_min_y, ink_max_y))

    pack = {
        'schemaVersion': SCHEMA_VERSION,
        'name': ('HanziWriter 笔顺中心线·全库' if charset is None
                 else 'HanziWriter 笔顺中心线·试点 %d 字' % len(glyphs)),
        'source': SOURCE_NAME,
        'license': SOURCE_LICENSE,
        'sourceEmUnits': SOURCE_EM_UNITS,
        'capUnits': CAP_UNITS,
        'bodySizeMm': BODY_MM,
        'baselineMm': BASELINE_MM,
        'emSizeMm': EM_MM,
        'advanceCjkMm': ADVANCE_CJK_MM,
        'advanceSpaceMm': ADVANCE_SPACE_MM,
        # Array of entries, NOT an object keyed by character: the .NET Framework
        # DataContractJsonSerializer silently returns an EMPTY dictionary for
        # JSON objects (probed with Dictionary<string,double> too). The character
        # travels as a string value, which DCJS reads reliably. Sorted by code
        # point for deterministic output.
        'glyphs': [{'c': ch, 'strokes': glyphs[ch]['strokes']}
                   for ch in sorted(glyphs, key=ord)],
    }
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    payload = json.dumps(pack, sort_keys=True, ensure_ascii=False, separators=(',', ':'))
    with open(args.out, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write(payload)

    # Reference-character spot heights for the report (mm, from the written data).
    def ink_height_mm(symbol):
        flat_all = [v for s in glyphs[symbol]['strokes'] for v in s]
        ys = flat_all[1::2]
        return max(ys) - min(ys)

    references = {}
    for symbol in ('木', '中', '国', '王', '一', '永'):
        if symbol in glyphs:
            references[symbol] = round(ink_height_mm(symbol), 3)

    lines = [
        'PASS: font pack generated deterministically.',
        'pack: %s (%.1f KB)' % (args.out, os.path.getsize(args.out) / 1024.0),
        'glyphs: %d   strokes: %d   points: %d' % (len(glyphs), total_strokes, total_points),
        'calibration: cap %g units -> %.1fmm (scale %.7f), baseline y=0 -> %.1fmm, x offset %.3fmm'
        % (CAP_UNITS, BODY_MM, scale, BASELINE_MM, x_offset),
        'ink extents in mm: X %.3f..%.3f  Y %.3f..%.3f (em box 0..%.0f)'
        % (ink_min_x, ink_max_x, ink_min_y, ink_max_y, EM_MM),
        'reference ink heights mm: %s' % references,
        'source: %s' % SOURCE_NAME,
        'license: %s' % SOURCE_LICENSE,
    ]
    os.makedirs(os.path.dirname(args.report), exist_ok=True)
    with open(args.report, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
