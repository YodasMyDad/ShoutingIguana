# Writes a finished subagent's last text message to a file. usage: last-message.py <task .output jsonl> <out.md> [header line]
import json, sys, io
last = None
with io.open(sys.argv[1], encoding='utf-8', errors='replace') as f:
    for line in f:
        line = line.strip()
        if not line:
            continue
        try:
            rec = json.loads(line)
        except Exception:
            continue
        msg = rec.get('message') if isinstance(rec, dict) else None
        if not isinstance(msg, dict) or msg.get('role') != 'assistant':
            continue
        content = msg.get('content')
        if isinstance(content, str):
            texts = [content]
        else:
            texts = [c.get('text', '') for c in (content or []) if isinstance(c, dict) and c.get('type') == 'text']
        text = '\n'.join(t for t in texts if t.strip())
        if text.strip():
            last = text
if last is None:
    sys.exit('no assistant text found')
header = (sys.argv[3] + '\n\n') if len(sys.argv) > 3 else ''
io.open(sys.argv[2], 'w', encoding='utf-8', newline='\n').write(header + last + '\n')
print(len(last), 'chars;', last[:80].replace('\n', ' '))
