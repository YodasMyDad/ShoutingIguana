# Joins head.cjs to the earlier gallery builder's figure body and page template, with this gallery's words.
# usage: make-builder.py <old build.cjs> <head.cjs> <new build.cjs>
import io
import sys

old = io.open(sys.argv[1], encoding='utf-8').read().split('\n')
head = io.open(sys.argv[2], encoding='utf-8').read().rstrip('\n').split('\n')
start = next(i for i, line in enumerate(old) if line.strip().startswith('const caption = (only ?? light).shows'))
body = '\n'.join(head + old[start:])
replacements = [
    ('<title>Shouting Iguana Screens</title>', '<title>Simple Mode Mockups</title>'),
    ('Shouting Iguana v2 · step 3, the desktop app', 'Shouting Iguana v2 · step 3, Simple mode'),
    ('<h1>The screens so far</h1>', '<h1>Simple mode, as designed</h1>'),
    ('<dt>Photographed</dt>', '<dt>Drawn</dt>'),
    ('<dt>Shell</dt>', '<dt>What these are</dt>'),
    ('<dt>Window</dt>', '<dt>Sizes</dt>'),
    ('<dt>Built from</dt>', '<dt>The design in full</dt>'),
    ('<h2>Seen in these shots</h2>', '<h2>Decided, and yours to overturn</h2>'),
    ('<h2>Not here yet</h2>', '<h2>What happens next</h2>'),
    ("'shouting-iguana-screens.html'", "'simple-mode-mockups.html'"),
]
for before, after in replacements:
    assert before in body, before
    body = body.replace(before, after)
io.open(sys.argv[3], 'w', encoding='utf-8', newline='\n').write(body)
at = body.index('<p class="lede">')
print(body[at:at + 300])
print([line.strip() for line in body.split('\n') if 'copyFileSync' in line or 'mkdirSync' in line])
