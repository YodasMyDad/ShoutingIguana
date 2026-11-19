// Syntax colours for a few lines of example code (CodeExample): a small tokenizer by hand, as a snippet of one tag or
// one header doesn't need a parser. The source viewer's CodeMirror, with its Lezer grammars, stays in the inspector's
// own chunk; mounting an editor, or loading its HTML grammar, for a line of code beside an explanation would cost more
// than the whole screen it sits on.

/** What the code is written in. Anything else is shown without colours. */
export type CodeLanguage = 'html' | 'httpHeader' | 'robotsTxt' | 'xml' | 'json';

/** The colour a token takes: one of the `--color-code-*` tokens, or the text's own. */
export type CodeTokenKind = 'tag' | 'attribute' | 'string' | 'comment' | 'keyword' | 'number' | 'plain';

export interface CodeToken {
  text: string;
  kind: CodeTokenKind;
}

/** Collects tokens, joining neighbours of a kind. */
function collector() {
  const tokens: CodeToken[] = [];
  const add = (text: string, kind: CodeTokenKind) => {
    if (text === '') return;
    const last = tokens.at(-1);
    if (last?.kind === kind) last.text += text;
    else tokens.push({ text, kind });
  };
  return { tokens, add };
}

/**
 * HTML and XML: tags and their brackets, attribute names, attribute values and comments. It reads what it's given
 * left to right and never fails: a snippet may start or stop mid-document, and a tag may run over several lines.
 */
function markup(code: string): CodeToken[] {
  const { tokens, add } = collector();
  let at = 0;
  while (at < code.length) {
    if (code.startsWith('<!--', at)) {
      const end = code.indexOf('-->', at + 4);
      const stop = end < 0 ? code.length : end + 3;
      add(code.slice(at, stop), 'comment');
      at = stop;
      continue;
    }
    // A tag's opening: <name, </name, <?xml or <!DOCTYPE.
    const open = /^<[/?!]?[A-Za-z][\w:.-]*/.exec(code.slice(at));
    if (!open) {
      const next = code.indexOf('<', at + 1);
      const stop = next < 0 ? code.length : next;
      add(code.slice(at, stop), 'plain');
      at = stop;
      continue;
    }
    add(open[0], 'tag');
    at += open[0].length;
    // Inside the tag, to its closing bracket: names, then values after an equals sign.
    let value = false;
    while (at < code.length) {
      const rest = code.slice(at);
      const close = /^[/?]?>/.exec(rest);
      if (close) {
        add(close[0], 'tag');
        at += close[0].length;
        break;
      }
      const space = /^\s+/.exec(rest);
      if (space) {
        add(space[0], 'plain');
        at += space[0].length;
        continue;
      }
      if (rest.startsWith('=')) {
        add('=', 'plain');
        at += 1;
        value = true;
        continue;
      }
      const quote = rest.startsWith('"') ? '"' : rest.startsWith("'") ? "'" : null;
      if (quote) {
        const end = rest.indexOf(quote, 1);
        const text = end < 0 ? rest : rest.slice(0, end + 1);
        add(text, 'string');
        at += text.length;
        value = false;
        continue;
      }
      const word = /^[^\s=>"']+/.exec(rest)?.[0] ?? rest.slice(0, 1);
      // After a name, a closing slash or question mark belongs to the bracket that follows it; an unquoted value keeps
      // its slash, as browsers read it.
      const closes = !value && /[/?]$/.test(word) && rest[word.length] === '>' && word.length > 1;
      const name = closes ? word.slice(0, -1) : word;
      add(name, value ? 'string' : 'attribute');
      at += name.length;
      value = false;
    }
  }
  return tokens;
}

/** HTTP headers and robots.txt: each line's name, up to its colon; a robots.txt comment from its `#`. */
function fields(code: string, comments: boolean): CodeToken[] {
  const { tokens, add } = collector();
  code.split('\n').forEach((line, index) => {
    if (index > 0) add('\n', 'plain');
    const hash = comments ? line.indexOf('#') : -1;
    const body = hash < 0 ? line : line.slice(0, hash);
    const [, indent, name] = /^(\s*)([A-Za-z][\w-]*):/.exec(body) ?? [];
    if (indent !== undefined && name !== undefined) {
      add(indent, 'plain');
      add(name, 'attribute');
      add(body.slice(indent.length + name.length), 'plain');
    } else {
      add(body, 'plain');
    }
    if (hash >= 0) add(line.slice(hash), 'comment');
  });
  return tokens;
}

/** JSON: property names, strings, numbers and the three words. A fragment (one property, a trailing comma) is fine. */
function json(code: string): CodeToken[] {
  const { tokens, add } = collector();
  const pattern = /"(?:[^"\\\n]|\\.)*"?|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|\b(?:true|false|null)\b|\s+|[^\s"]/g;
  for (const match of code.matchAll(pattern)) {
    const text = match[0];
    if (text.startsWith('"')) {
      const isName = /^\s*:/.test(code.slice(match.index + text.length));
      add(text, isName ? 'attribute' : 'string');
    } else if (/^-?\d/.test(text)) {
      add(text, 'number');
    } else if (text === 'true' || text === 'false' || text === 'null') {
      add(text, 'keyword');
    } else {
      add(text, 'plain');
    }
  }
  return tokens;
}

/** The code as coloured tokens. They join back into the code exactly. */
export function tokenizeCode(code: string, language: string): CodeToken[] {
  switch (language as CodeLanguage) {
    case 'html':
    case 'xml':
      return markup(code);
    case 'httpHeader':
      return fields(code, false);
    case 'robotsTxt':
      return fields(code, true);
    case 'json':
      return json(code);
    default:
      return code === '' ? [] : [{ text: code, kind: 'plain' }];
  }
}

/** A language's name as a reader knows it, for the corner of an example; null for one this version doesn't know. */
export function languageName(language: string): string | null {
  switch (language as CodeLanguage) {
    case 'html':
      return 'HTML';
    case 'xml':
      return 'XML';
    case 'httpHeader':
      return 'HTTP header';
    case 'robotsTxt':
      return 'robots.txt';
    case 'json':
      return 'JSON';
    default:
      return null;
  }
}
