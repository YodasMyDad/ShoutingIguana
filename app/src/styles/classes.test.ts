// @vitest-environment node
// Every class the app writes makes CSS. The theme is closed (tokens.css), so a utility off its scales, such as p-13,
// max-w-72 or font-normal, compiles to nothing and silently does nothing. This reads the static classes in every
// .tsx file under src, stories and test helpers too (they render the screenshots), compiles them the way
// tokens.test.ts does, and names each class that makes no CSS, with its file and line.
//
// An `si-*` name with no CSS of its own passes only as a hook: a unit test or an e2e spec finds things by it.
//
// Where it reads classes: className and *ClassName props and object properties, `.className =` assignments and
// clsx(...) calls; inside those, a ?: branch, && and || operands, arrays, clsx's object keys, a const in the same file
// holding a class string, and a const object's values read by key. What it can't see: the part of a template literal
// next to an interpolation (`si-badge--${tone}`), and a class that comes from anywhere else (a prop passed on, a
// function's result, a constant imported from another module).
import { compile } from '@tailwindcss/node';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import { beforeAll, describe, expect, it } from 'vitest';

const app = path.resolve(import.meta.dirname, '../..');
const entry = path.resolve(import.meta.dirname, 'index.css');

interface ClassUse {
  name: string;
  file: string;
  line: number;
}

/** The files under a folder whose names pass the test, at any depth. */
function filesUnder(folder: string, test: (name: string) => boolean): string[] {
  return readdirSync(folder, { recursive: true, withFileTypes: true })
    .filter((found) => found.isFile() && test(found.name))
    .map((found) => path.join(found.parentPath, found.name));
}

const isClassProp = (name: string) => name === 'className' || name.endsWith('ClassName');

/**
 * The static classes one source file writes; how many places hold classes this can't read; and the classes a story's
 * pseudo-states parameter forces hover or focus on (`pseudo: { hover: ['.is-hover'] }`), which need no CSS.
 */
function classesIn(file: string, text: string): { uses: ClassUse[]; unseen: number; states: string[] } {
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const uses: ClassUse[] = [];
  const read = new Set<ts.Node>();
  const states: string[] = [];
  let unseen = 0;

  // The file's consts by name; a name declared twice is ambiguous, so it isn't followed.
  const consts = new Map<string, ts.Expression | undefined>();
  const findConsts = (node: ts.Node) => {
    if (
      ts.isVariableDeclaration(node) &&
      ts.isIdentifier(node.name) &&
      node.initializer &&
      ts.getCombinedNodeFlags(node) & ts.NodeFlags.Const
    ) {
      consts.set(node.name.text, consts.has(node.name.text) ? undefined : node.initializer);
    }
    ts.forEachChild(node, findConsts);
  };
  findConsts(source);

  const lineOf = (node: ts.Node) => source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
  const add = (words: string, node: ts.Node) => {
    for (const name of words.split(/\s+/).filter(Boolean)) uses.push({ name, file, line: lineOf(node) });
  };
  const unwrap = (expression: ts.Expression): ts.Expression =>
    ts.isParenthesizedExpression(expression) ||
    ts.isAsExpression(expression) ||
    ts.isSatisfiesExpression(expression) ||
    ts.isNonNullExpression(expression)
      ? unwrap(expression.expression)
      : expression;
  const constObject = (name: ts.Expression) => {
    const initializer = ts.isIdentifier(name) ? consts.get(name.text) : undefined;
    return initializer && unwrap(initializer);
  };

  // Reads an expression in a place that holds classes.
  const take = (node: ts.Expression) => {
    const expression = unwrap(node);
    if (read.has(expression)) return;
    read.add(expression);
    if (ts.isStringLiteral(expression) || ts.isNoSubstitutionTemplateLiteral(expression)) {
      add(expression.text, expression);
    } else if (ts.isTemplateExpression(expression)) {
      // A word that touches an interpolation is only part of a class: it can't be read.
      const parts = [expression.head, ...expression.templateSpans.map((span) => span.literal)];
      parts.forEach((part, index) => {
        const words = part.text.split(/\s+/);
        words.forEach((word, at) => {
          if (!word) return;
          const glued = (at === 0 && index > 0) || (at === words.length - 1 && index < parts.length - 1);
          if (glued) unseen += 1;
          else add(word, part);
        });
      });
    } else if (ts.isConditionalExpression(expression)) {
      take(expression.whenTrue);
      take(expression.whenFalse);
    } else if (ts.isBinaryExpression(expression)) {
      const operator = expression.operatorToken.kind;
      if (operator === ts.SyntaxKind.AmpersandAmpersandToken) take(expression.right);
      else if (operator === ts.SyntaxKind.BarBarToken || operator === ts.SyntaxKind.QuestionQuestionToken) {
        take(expression.left);
        take(expression.right);
      } else unseen += 1;
    } else if (ts.isArrayLiteralExpression(expression)) {
      for (const element of expression.elements) take(ts.isSpreadElement(element) ? element.expression : element);
    } else if (ts.isObjectLiteralExpression(expression)) {
      // clsx({ 'si-row--open': open }): the keys are the classes.
      for (const property of expression.properties) {
        if (property.name && (ts.isStringLiteral(property.name) || ts.isIdentifier(property.name))) {
          add(property.name.text, property.name);
        } else unseen += 1;
      }
    } else if (ts.isCallExpression(expression) && isClsx(expression)) {
      for (const argument of expression.arguments) take(argument);
    } else if (ts.isIdentifier(expression)) {
      const initializer = consts.get(expression.text);
      if (initializer) take(initializer);
      else unseen += 1;
    } else if (ts.isElementAccessExpression(expression) || ts.isPropertyAccessExpression(expression)) {
      // TONE[severity] or TONE.error, where TONE is a const object or array of classes in this file.
      const held = constObject(expression.expression);
      const key = ts.isPropertyAccessExpression(expression) ? expression.name.text : undefined;
      if (held && ts.isObjectLiteralExpression(held)) {
        for (const property of held.properties) {
          if (!ts.isPropertyAssignment(property)) unseen += 1;
          else if (key === undefined || property.name.getText(source) === key) take(property.initializer);
        }
      } else if (held && ts.isArrayLiteralExpression(held) && key === undefined) {
        for (const element of held.elements) take(element);
      } else unseen += 1;
    } else unseen += 1;
  };
  const isClsx = (call: ts.CallExpression) => ts.isIdentifier(call.expression) && call.expression.text === 'clsx';

  const selectors = (node: ts.Node) => {
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) states.push(...classesStyled(node.text));
    ts.forEachChild(node, selectors);
  };

  const visit = (node: ts.Node) => {
    if (ts.isPropertyAssignment(node) && node.name.getText(source) === 'pseudo') selectors(node.initializer);
    if (ts.isJsxAttribute(node) && isClassProp(node.name.getText(source)) && node.initializer) {
      if (ts.isStringLiteral(node.initializer)) take(node.initializer);
      else if (ts.isJsxExpression(node.initializer) && node.initializer.expression) take(node.initializer.expression);
    } else if (ts.isPropertyAssignment(node) && isClassProp(node.name.getText(source).replace(/['"]/g, ''))) {
      take(node.initializer);
    } else if (
      ts.isBinaryExpression(node) &&
      node.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
      ts.isPropertyAccessExpression(node.left) &&
      node.left.name.text === 'className'
    ) {
      take(node.right);
    } else if (ts.isCallExpression(node) && isClsx(node)) {
      take(node);
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return { uses, unseen, states };
}

/** Every class a stylesheet's selectors name, unescaped. */
function classesStyled(css: string): Set<string> {
  const named = new Set<string>();
  const selectors = css.replace(/\/\*[\s\S]*?\*\//g, '');
  for (const [, escaped = ''] of selectors.matchAll(/\.((?:\\[0-9a-fA-F]{1,6} ?|\\[^0-9a-fA-F]|[\w-])+)/g)) {
    named.add(
      escaped.replace(/\\([0-9a-fA-F]{1,6}) ?|\\(.)/g, (_, hex: string | undefined, char: string | undefined) =>
        hex ? String.fromCodePoint(parseInt(hex, 16)) : (char ?? ''),
      ),
    );
  }
  return named;
}

/** Whether a unit test or an e2e spec finds things by a class: it names it more often than it writes it. */
function hooks(): (name: string) => boolean {
  const files = [
    ...filesUnder(path.join(app, 'src'), (name) => /\.test\.tsx?$/.test(name)),
    ...filesUnder(path.join(app, 'e2e'), (name) => name.endsWith('.ts')),
  ];
  const texts = files.map((file) => {
    const text = readFileSync(file, 'utf8');
    return { text, written: classesIn(file, text).uses.map((use) => use.name) };
  });
  return (name) =>
    texts.some(({ text, written }) => {
      const pattern = new RegExp(`(?<![\\w-])${name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?![\\w-])`, 'g');
      return (text.match(pattern)?.length ?? 0) > written.filter((use) => use === name).length;
    });
}

describe('the classes the app writes', () => {
  let uses: ClassUse[];
  let unseen: number;
  let states: Set<string>;
  let styled: Set<string>;

  beforeAll(async () => {
    const sources = filesUnder(path.join(app, 'src'), (name) => name.endsWith('.tsx'));
    const read = sources.map((file) => classesIn(path.relative(app, file), readFileSync(file, 'utf8')));
    uses = read.flatMap((file) => file.uses);
    unseen = read.reduce((sum, file) => sum + file.unseen, 0);
    states = new Set(read.flatMap((file) => file.states));
    const compiler = await compile(readFileSync(entry, 'utf8'), {
      base: path.dirname(entry),
      from: entry,
      onDependency: () => undefined,
    });
    styled = classesStyled(compiler.build([...new Set(uses.map((use) => use.name))]));
  });

  it('are read from the source: all its files, and nearly every place that writes a class', () => {
    expect(new Set(uses.map((use) => use.file.replaceAll('\\', '/').split('/')[1])).size).toBeGreaterThan(10);
    expect(new Set(uses.map((use) => use.name)).size).toBeGreaterThan(600);
    expect(unseen).toBeLessThan(uses.length / 10);
  });

  it('each compile to CSS, or are an si- hook a test finds things by, or a story’s forced state', () => {
    const isHook = hooks();
    const failing = uses.filter(
      (use) => !styled.has(use.name) && !(use.name.startsWith('si-') && isHook(use.name)) && !states.has(use.name),
    );
    expect(failing.map((use) => `${use.name} (${use.file.replaceAll('\\', '/')}:${use.line})`)).toEqual([]);
  });
});

describe('reading classes', () => {
  const read = (text: string) => classesIn('sample.tsx', text);
  const names = (text: string) => read(text).uses.map((use) => use.name);

  it('reads className and *ClassName props, properties, assignments and clsx', () => {
    expect(names(`<div className="a b" />`)).toEqual(['a', 'b']);
    expect(names(`<div className={'a'} panelClassName={\`b c\`} />`)).toEqual(['a', 'b', 'c']);
    expect(names(`const args = { className: 'a', 'titleClassName': 'b', label: 'not' };`)).toEqual(['a', 'b']);
    expect(names(`element.className = 'a'; element.title = 'not';`)).toEqual(['a']);
    expect(names(`const c = clsx('a', open && 'b', { c: open, 'd e': on }, ['f'], x ? 'g' : 'h');`)).toEqual([
      'a',
      'b',
      'c',
      'd',
      'e',
      'f',
      'g',
      'h',
    ]);
  });

  it('follows a const in the same file, and a const object read by key', () => {
    expect(names(`const HEAD = 'a b'; <div className={HEAD} />`)).toEqual(['a', 'b']);
    expect(names(`const TONE = { x: 'a', y: 'b' } as const; <i className={TONE[tone]} />`)).toEqual(['a', 'b']);
    expect(names(`const TONE = { x: 'a', y: 'b' }; <i className={TONE.y} />`)).toEqual(['b']);
  });

  it('leaves out the words an interpolation touches, and counts what it can’t read', () => {
    const { uses, unseen } = read(
      `<i className={\`a si-x--\${tone} b\${c}d e\`} title={'not'} />; <b className={pick()} />`,
    );
    expect(uses.map((use) => use.name)).toEqual(['a', 'e']);
    expect(unseen).toBe(4);
  });

  it('knows the classes a story forces a state on, and that they aren’t classes it writes', () => {
    const { uses, states } = read(
      `const meta = { parameters: { pseudo: { hover: ['.is-hover'], focusVisible: ['.is-focus .si-field'] } } };`,
    );
    expect(uses).toEqual([]);
    expect(states).toEqual(['is-hover', 'is-focus', 'si-field']);
  });

  it('never reads the condition as a class', () => {
    expect(names(`<i className={kind === 'error' ? 'a' : 'b'} />`)).toEqual(['a', 'b']);
  });

  it('knows a class a stylesheet names, escaped or not', () => {
    const styled = classesStyled('.p-2\\.5{} .w-1\\/2:hover{} /* .commented */ .\\[\\&\\>svg\\]\\:size-4>svg{}');
    expect([...styled]).toEqual(expect.arrayContaining(['p-2.5', 'w-1/2', '[&>svg]:size-4']));
    expect(styled.has('commented')).toBe(false);
  });
});
