import sys, sqlite3, importlib.util
spec = importlib.util.spec_from_file_location('m', sys.argv[1]); m = importlib.util.module_from_spec(spec)
sys.argv_backup = sys.argv; src = open(sys.argv[1], encoding='utf-8').read().replace('\nmain()\n', '\n')
exec(compile(src, 'm', 'exec'), m.__dict__)
upem, width, cmap = m.load_font(sys.argv[2])
for crawl in sys.argv[3:]:
    c = sqlite3.connect('file:' + crawl + '?mode=ro', uri=True)
    rows = c.execute('select r.title, r.meta_description from url_summary s join responses r on r.url_id = s.url_id where s.indexable = 1 and r.title is not null').fetchall()
    tw = [round(m.measure(a, 20, upem, width)[0]) for a, _ in rows if a and a.strip()]
    dw = [round(m.measure(b, 14, upem, width)[0]) for _, b in rows if b and b.strip()]
    print(crawl.split('/')[-1], 'titles >', {t: sum(w > t for w in tw) for t in (561, 580, 600)}, '<', {t: sum(w < t for w in tw) for t in (200,)},
          '| desc >', {t: sum(w > t for w in dw) for t in (920, 960, 985, 1005)}, '<', {t: sum(w < t for w in dw) for t in (400,)})
    # widest/narrowest char counts at the limit
    print('  titles: min/max chars among 556-566 px:', sorted({len(' '.join(a.split())) for a, _ in rows if a and 556 <= round(m.measure(a, 20, upem, width)[0]) <= 566}))
