"""Calibration only: measure real titles/descriptions in Arial advance widths (no kerning). Read-only.
usage: python -I measure.py <font.ttf> <crawl.sicrawl> [<crawl2> ...]
"""
import sqlite3, struct, sys, unicodedata, re

def load_font(path):
    data = open(path, 'rb').read()
    num = struct.unpack('>H', data[4:6])[0]
    tables = {}
    for i in range(num):
        tag, _, off, ln = struct.unpack('>4sIII', data[12 + 16 * i: 28 + 16 * i])
        tables[tag.decode()] = (off, ln)
    head = tables['head'][0]
    upem = struct.unpack('>H', data[head + 18: head + 20])[0]
    hhea = tables['hhea'][0]
    nh = struct.unpack('>H', data[hhea + 34: hhea + 36])[0]
    hm = tables['hmtx'][0]
    adv = [struct.unpack('>H', data[hm + 4 * i: hm + 4 * i + 2])[0] for i in range(nh)]
    cmo = tables['cmap'][0]
    n = struct.unpack('>H', data[cmo + 2: cmo + 4])[0]
    cmap = {}
    for i in range(n):
        pid, eid, off = struct.unpack('>HHI', data[cmo + 4 + 8 * i: cmo + 12 + 8 * i])
        sub = cmo + off
        fmt = struct.unpack('>H', data[sub:sub + 2])[0]
        if fmt == 4 and pid == 3 and eid == 1:
            segx2 = struct.unpack('>H', data[sub + 6: sub + 8])[0]
            seg = segx2 // 2
            ends = struct.unpack('>%dH' % seg, data[sub + 14: sub + 14 + segx2])
            starts = struct.unpack('>%dH' % seg, data[sub + 16 + segx2: sub + 16 + 2 * segx2])
            deltas = struct.unpack('>%dh' % seg, data[sub + 16 + 2 * segx2: sub + 16 + 3 * segx2])
            ro_pos = sub + 16 + 3 * segx2
            ros = struct.unpack('>%dH' % seg, data[ro_pos: ro_pos + segx2])
            for s in range(seg):
                for cp in range(starts[s], ends[s] + 1):
                    if cp == 0xFFFF:
                        continue
                    if ros[s] == 0:
                        g = (cp + deltas[s]) & 0xFFFF
                    else:
                        addr = ro_pos + 2 * s + ros[s] + 2 * (cp - starts[s])
                        g = struct.unpack('>H', data[addr: addr + 2])[0]
                        if g:
                            g = (g + deltas[s]) & 0xFFFF
                    if g:
                        cmap[cp] = g
    def width(cp):
        g = cmap.get(cp)
        if g is None:
            return None
        return adv[g] if g < nh else adv[-1]
    return upem, width, cmap

WIDE = re.compile('[ᄀ-ᅟ⺀-〾ぁ-㏿㐀-䶿一-鿿가-힣豈-﫿＀-｠\U0001F300-\U0001FAFF]')

def measure(text, size, upem, width):
    text = ' '.join(text.split())
    total = 0.0
    unknown = 0
    for ch in text:
        cp = ord(ch)
        if unicodedata.category(ch) in ('Mn', 'Me', 'Cf'):
            continue
        if WIDE.match(ch):
            total += size
            continue
        w = width(cp)
        if w is None:
            unknown += 1
            w = width(ord('n'))
        total += w / upem * size
    return total, len(text), unknown

def main():
    upem, width, cmap = load_font(sys.argv[1])
    print('unitsPerEm', upem, 'mapped code points', len(cmap), "W", width(ord('W')), "i", width(ord('i')), "space", width(32), "n", width(ord('n')), "period", width(ord('.')), "ellipsis", width(0x2026))
    for crawl in sys.argv[2:]:
        c = sqlite3.connect('file:' + crawl + '?mode=ro', uri=True)
        rows = c.execute('''select r.title, r.meta_description from url_summary s join responses r on r.url_id = s.url_id
                            where s.indexable = 1 and r.title is not null''').fetchall()
        t = [(measure(a, 20, upem, width), a) for a, _ in rows if a and a.strip()]
        d = [(measure(b, 14, upem, width), b) for _, b in rows if b and b.strip()]
        print('\n==', crawl, 'pages', len(rows))
        def report(items, chmax, chmin, pxmax, pxmin, name):
            ch_over = sum(1 for (w, n, u), _ in items if n > chmax)
            px_over = sum(1 for (w, n, u), _ in items if round(w) > pxmax)
            both = sum(1 for (w, n, u), _ in items if n > chmax and round(w) > pxmax)
            ch_under = sum(1 for (w, n, u), _ in items if n < chmin)
            px_under = sum(1 for (w, n, u), _ in items if round(w) < pxmin)
            avg = sum(w for (w, n, u), _ in items) / max(1, sum(n for (w, n, u), _ in items))
            unk = sum(u for (w, n, u), _ in items)
            print(f'{name}: n={len(items)} over chars>{chmax}: {ch_over}  over px>{pxmax}: {px_over}  both: {both}  '
                  f'under chars<{chmin}: {ch_under}  under px<{pxmin}: {px_under}  avg px/char {avg:.2f}  unknown cps {unk}')
            flips_in = [(round(w), n, s) for (w, n, u), s in items if n > chmax and round(w) <= pxmax]
            flips_out = [(round(w), n, s) for (w, n, u), s in items if n <= chmax and round(w) > pxmax]
            for x in sorted(flips_in)[-3:]:
                print('   chars-flag, px-pass:', x)
            for x in sorted(flips_out)[:3]:
                print('   chars-pass, px-flag:', x)
            near = [(round(w), n, s) for (w, n, u), s in items if chmax < n <= chmax + 5]
            print(f'   {name} {chmax+1}-{chmax+5} chars: {len(near)}, of which px-flagged {sum(1 for w, n, s in near if w > pxmax)}')
        report(t, 60, 30, 561, 200, 'titles')
        report(d, 155, 70, 985, 400, 'descriptions')

main()
