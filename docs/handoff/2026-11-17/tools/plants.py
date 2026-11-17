import sys, re
src = open(sys.argv[1], encoding='utf-8').read().replace('\nmain()\n', '\n'); m = {}
exec(compile(src, 'm', 'exec'), m)
upem, width, cmap = m['load_font'](sys.argv[2])
plants = open(sys.argv[3], encoding='utf-8').read()
names = re.findall(r'Page\("([^"]+)",\s*"([^"]+)"', plants)
rows = []
for slug, name in names:
    t = f'{name} | Synthetic Store planted page'
    d = f'{name}: a planted test page with one deliberate SEO problem, part of the Synthetic Store crawler tests.'
    tw = round(m['measure'](t, 20, upem, width)[0]); dw = round(m['measure'](d, 14, upem, width)[0])
    rows.append((tw, len(t), dw, len(d), slug))
print('plants', len(rows))
for r in sorted(rows, reverse=True)[:8]: print('widest title', r)
for r in sorted(rows)[:3]: print('narrowest title', r)
print('desc range', min(r[2] for r in rows), max(r[2] for r in rows))
for t in ['This planted page has a title that runs on far past what search results can show | Synthetic Store', 'Short page', 'Self canonical',
          'Synthetic Store | Everyday goods for crawler tests', 'Category 19 products | Synthetic Store']:
    print(round(m['measure'](t, 20, upem, width)[0]), len(t), t)
for d in ['This planted meta description keeps going well past the point where search results cut it off, adding clause after clause about nothing in particular until it is far too long to show.', 'Too short.',
          'The Synthetic Store home page: a deterministic shop with thousands of products, built to test SEO crawlers.',
          'Every product filed under category 19 in the Synthetic Store, a deterministic shop built to test SEO crawlers.',
          'Two planted pages share this exact meta description, so the duplicate description check has to flag both of them.']:
    print(round(m['measure'](d, 14, upem, width)[0]), len(d), d[:50])
