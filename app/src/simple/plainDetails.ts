// Every check's detail in Simple's plain words (docs/v2-ui-simple.md, "Plain words"; the fixit audit's item 7). The
// engine writes a detail as a fact with its unit, for Advanced: "H1 → H3", "1,200 ms, over 1,000 ms", "Empty
// og:title". Simple says the same fact as a site owner would: "It goes from the main heading to a level-3 heading,
// skipping level 2", "It took 1.2 seconds to open, more than 1 second", "For sharing, its title is empty". Each check
// reads the forms its rule writes (the engine's `Analysis/Rules`), the older forms crawl files keep among them; a
// detail in no form a check knows gives null, and the engine's own text is said instead. The site's own words stay
// quoted; addresses stay as written. Pure.
import { formatCount } from '../lib/format';
import {
  plainCodeClause,
  plainFailureClause,
  plainFailureWords,
  plainFileKind,
  plainLength,
  plainSeconds,
} from './plainAnswers';

type Say = (detail: string) => string | null;

/** A count as the engine writes it ("1,036"), as a number. */
const numberOf = (written: string) => Number(written.replace(/,/g, ''));

/** "1 image", "3 images", from a count as written. */
function counted(written: string, one: string, many: string): string {
  return `${written} ${numberOf(written) === 1 ? one : many}`;
}

/** Text the engine quoted ("Sofas"), quoted as Simple quotes the site's words (“Sofas”), a quote cut short closed. */
function curly(text: string): string {
  const said = text.replace(/"([^"]*)"/g, '“$1”');
  const open = said.indexOf('"');
  return open < 0 ? said : `${said.slice(0, open)}“${said.slice(open + 1).replace(/"/g, '')}”`;
}

/** "a, b and c". */
function listed(items: readonly string[], last = 'and'): string {
  return items.length > 1 ? `${items.slice(0, -1).join(', ')} ${last} ${items[items.length - 1]}` : (items[0] ?? '');
}

/** A line as a sentence: its first letter up. */
const sentence = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** "2 steps", "1 step". */
const steps = (written: string) => counted(written, 'step', 'steps');

// ------------------------------------------------------------------------------- How an address answered, plainly

/** Where a canonical's redirects end, after "forwards (301 Moved Permanently)": " to /b", " and, after 2 steps, ends at /b". */
function forwardTail(tail: string): string | null {
  if (tail === '') return '';
  const which = (rest: string | undefined) => {
    if (!rest) return '';
    const answer = statusPhrase(rest);
    return answer ? `, which ${answer}` : null;
  };
  const to = /^ to (\S+?)(?:, which (.+))?$/.exec(tail);
  if (to) {
    const end = which(to[2]);
    return end === null ? null : ` to ${to[1] ?? ''}${end}`;
  }
  const after = /^ and, after (\d[\d,]*) redirects?, (ends at|leads to) (\S+?)(?:, which (.+))?$/.exec(tail);
  if (after) {
    const end = which(after[4]);
    return end === null ? null : ` and, after ${steps(after[1] ?? '')}, ${after[2] ?? ''} ${after[3] ?? ''}${end}`;
  }
  return null;
}

/**
 * What the crawl found at an address, as the engine says it after "the URL" or "that" (`RuleText.Status`,
 * `RuleText.BrokenStatus`): "returns 404 Not Found", "redirects to a URL that returns 404 Not Found", "is noindex". As a
 * clause after "It": "is missing (404 Not Found)", "forwards to an address that is missing (404 Not Found)", "asks not
 * to appear in search results". Null for a form it doesn't know.
 */
function statusPhrase(text: string): string | null {
  const said = text.trim();
  const returns = /^returns ((\d{3}) .+|HTTP (\d{3}))$/.exec(said);
  if (returns) return plainCodeClause(Number(returns[2] ?? returns[3]), returns[1] ?? '');
  const through = /^redirects to a URL that (.+)$/.exec(said);
  if (through) {
    const end = statusPhrase(through[1] ?? '');
    return end ? `forwards to an address that ${end}` : null;
  }
  const still = /^is still redirecting after (\d[\d,]*) hops?, more than browsers follow$/.exec(said);
  if (still) return `is still forwarding after ${steps(still[1] ?? '')}, more than browsers follow`;
  const nowhere = /^redirects with (.+) but no usable Location header$/.exec(said);
  if (nowhere) return `says it forwards (${nowhere[1] ?? ''}) but not where to`;
  const forwards = /^redirects \(([^)]+)\)(.*)$/.exec(said);
  if (forwards) {
    const tail = forwardTail(forwards[2] ?? '');
    return tail === null ? null : `forwards (${forwards[1] ?? ''})${tail}`;
  }
  return STATUS_WORDS[said] ?? null;
}

const STATUS_WORDS: Readonly<Record<string, string>> = {
  'redirects in a loop': 'forwards round in a loop',
  'redirects with an instant meta refresh': 'forwards straight away',
  'redirects back to this page': 'forwards back to this page',
  'timed out': 'didn’t answer in time',
  "couldn't be reached": 'couldn’t be reached',
  'is blocked by a firewall': 'turned the check away',
  'is blocked by robots.txt': 'is kept out by the rules file',
  'is noindex': 'asks not to appear in search results',
  'is canonicalised to another URL': 'names another address as its main address',
  "wasn't checked": 'wasn’t checked',
};

/** A status as the engine names it ("404 Not Found", "HTTP 599"), as a clause after "It": "is missing (404 Not Found)". */
function codeClause(detail: string): string | null {
  const code = /^(?:(\d{3}) \S.*|HTTP (\d{3}))$/.exec(detail);
  return code ? plainCodeClause(Number(code[1] ?? code[2]), detail) : null;
}

/** "(it|the first) redirects to a URL that …" after a count of broken addresses: "; the first forwards to …". */
function brokenTail(which: string | undefined, phrase: string | undefined): string | null {
  if (!which || !phrase) return '';
  const said = statusPhrase(phrase);
  return said ? `; ${which} ${said}` : null;
}

/** Each "address (what went wrong)" in a list the engine joins with ", ", and " and N more" after them. */
function addressFailures(detail: string): { items: { url: string; failure: string }[]; more: string | null } | null {
  const more = /^(.*) and (\d[\d,]*) more$/.exec(detail);
  const list = more ? (more[1] ?? '') : detail;
  const items = [...list.matchAll(/(\S+) \(((?:[^()]|\([^()]*\))*)\)(?:, |$)/g)].map((match) => ({
    url: match[1] ?? '',
    failure: match[2] ?? '',
  }));
  return items.length > 0 ? { items, more: more ? (more[2] ?? null) : null } : null;
}

// --------------------------------------------------------------------------------------------- The page's parts

/** A heading by its level: "the main heading", "a section heading", "a level-4 heading". */
function headingName(level: number, article: 'the' | 'a'): string {
  if (level === 1) return 'the main heading';
  const name = level === 2 ? 'section heading' : `level-${level} heading`;
  return article === 'the' ? `the ${name}` : `a ${name}`;
}

/** Levels a jump leaves out: "level 2", "levels 2 and 3". */
function skippedLevels(from: number, to: number): string {
  const levels = Array.from({ length: to - from - 1 }, (_, index) => `${from + index + 1}`);
  return levels.length === 1 ? `level ${levels[0] ?? ''}` : `levels ${listed(levels)}`;
}

/**
 * What ended a page's head early, as the engine says it (`RuleText.HeadEnded`): "It's in the body (after <body> on
 * line 30)", "<img src="/t.gif"> on line 12 ended the head early". As where the setting is, plainly.
 */
function headEnded(text: string): string | null {
  const body = /^It's in the body(?: \(after <body> on line (\d[\d,]*)\))?$/i.exec(text);
  if (body) {
    return `it’s below the top part of the page’s code${body[1] ? ` (from line ${body[1]})` : ''}, where search engines ignore it`;
  }
  const ended = /^.+? ended the head early$/i.exec(text);
  if (ended) {
    const line = / on line (\d[\d,]*) ended the head early$/.exec(text)?.[1];
    return `${line ? `code on line ${line}` : 'code on the page'} ends the top part of the page’s code early, so search engines ignore it`;
  }
  return null;
}

/** An image the engine describes by what it has: `alt "Sofa"`, `class "hero"`. */
function imageMark(text: string): string | null {
  const mark = /^(alt|class|id|title|data-src|data-srcset) "(.*?)"?$/.exec(text.trim());
  if (!mark) return null;
  const value = `“${mark[2] ?? ''}”`;
  switch (mark[1]) {
    case 'alt':
      return `alt text ${value}`;
    case 'title':
      return `the hover text ${value}`;
    case 'data-src':
    case 'data-srcset':
      return `set to load ${value} by script`;
    default:
      return `marked ${value}`;
  }
}

/** Open Graph and X card tags by what they set: og:title, the share title. */
const SHARE_TAGS: Readonly<Record<string, [string, string]>> = {
  'og:title': ['share title', 'share titles'],
  'og:description': ['share description', 'share descriptions'],
  'og:image': ['share image', 'share images'],
  'og:url': ['share address', 'share addresses'],
  'og:type': ['share type', 'share types'],
  'twitter:card': ['X card type', 'X card types'],
  'twitter:title': ['X card title', 'X card titles'],
  'twitter:description': ['X card description', 'X card descriptions'],
  'twitter:image': ['X card image', 'X card images'],
};

/** "The share image", "The X card image": a share image by the tag that names it. */
const shareImage = (tag: string) => (tag.startsWith('twitter:') ? 'the X card image' : 'the share image');

/** The security headers by what each one does. */
const SECURITY_SETTINGS: Readonly<Record<string, string>> = {
  'Strict-Transport-Security': 'keep browsers on the secure connection',
  'X-Content-Type-Options': 'stop browsers guessing file types',
  'Content-Security-Policy': 'limit what the pages may load',
  'X-Frame-Options': 'stop other sites showing your pages inside theirs',
  'Referrer-Policy': 'limit what other sites learn of where visitors came from',
};

/** Tags the engine names in the body, by what they set. */
const HEAD_TAGS: Readonly<Record<string, string>> = {
  title: 'the title',
  'meta description': 'the search description',
  'meta robots': 'the instructions for search engines',
  'meta googlebot': 'the instructions for Google',
  'meta viewport': 'the setting for phone screens',
  'meta refresh': 'the instruction to move to another page',
  'meta content-type': 'the text encoding',
  'link alternate': 'the links to other versions of the page',
  'link amphtml': 'the link to the fast mobile version',
};

function headTagName(tag: string): string {
  const named = HEAD_TAGS[tag];
  if (named) return named;
  const social = /^(\d[\d,]*) (Open Graph|X) tags?$/.exec(tag);
  if (social) {
    return social[2] === 'X'
      ? counted(social[1] ?? '', 'X card setting', 'X card settings')
      : counted(social[1] ?? '', 'share preview setting', 'share preview settings');
  }
  return tag.startsWith('meta ') ? 'the instructions for search engines' : `“${tag}”`;
}

// ------------------------------------------------------------------------------ Details for search engines (JSON)

/** What a JSON parser's message comes down to, for a site owner to hand on: "a comma or closing bracket is missing". */
function jsonMistake(message: string): string | null {
  if (/invalid after a value/i.test(message)) return 'a comma or closing bracket is missing';
  if (/invalid after a property name/i.test(message)) return 'a colon is missing after a name';
  if (/trailing comma/i.test(message)) return 'there’s an extra comma at the end of a list';
  if (/depth to be zero|should be closed/i.test(message)) return 'a closing bracket is missing';
  if (/invalid start of a (?:value|property name)/i.test(message))
    return 'a value starts with a character that isn’t allowed';
  if (/control character|invalid within a JSON string/i.test(message))
    return 'a text holds a line break or other hidden character';
  if (/end of (?:the )?data|unexpected end|incomplete/i.test(message)) return 'it stops part-way through';
  return null;
}

const DETAILS_FOR_SEARCH = 'The details for search engines';

function structuredDataInvalid(detail: string): string | null {
  if (detail.startsWith('A property name escapes an invalid character')) {
    return `${DETAILS_FOR_SEARCH} hold a character that can’t be read`;
  }
  const where = (line: number, position: number, message: string, block = '') => {
    const what = jsonMistake(message);
    return `${DETAILS_FOR_SEARCH}${block} have a mistake on line ${formatCount(line)}, at character ${formatCount(position)}${what ? `: ${what}` : ''}`;
  };
  const block = /^Block (\d+) of (\d+), line (\d+), position (\d+): (.*)$/.exec(detail);
  if (block) {
    const [, which = '', of = '', line = '', position = '', message = ''] = block;
    return where(Number(line), Number(position), message, of === '1' ? '' : ` (block ${which} of ${of})`);
  }
  // An older engine's: .NET's message, its line counted from 0 and its position from 0.
  const dotnet = /^(.*?)\s*LineNumber: (\d+) \| BytePositionInLine: (\d+)\.?$/.exec(detail);
  if (dotnet) return where(Number(dotnet[2]) + 1, Number(dotnet[3]) + 1, dotnet[1] ?? '');
  const what = jsonMistake(detail);
  return `${DETAILS_FOR_SEARCH} have a mistake${what ? `: ${what}` : ''}, so search engines can’t read them`;
}

/** "offers or review or aggregateRating" → "one of “offers”, “review” or “aggregateRating”". */
function neededParts(text: string): string {
  return listed(
    text.split(', ').map((group) => {
      const options = group.split(' or ').map((name) => `“${name.trim()}”`);
      return options.length > 1 ? `one of ${listed(options, 'or')}` : (options[0] ?? '');
    }),
  );
}

/** A list the engine joins with "; ", each part said its own way; a part cut short ends it with "and more". */
function eachPart(detail: string, sayPart: (part: string) => string | null, joiner = '; '): string | null {
  const parts = detail.split('; ');
  const said: string[] = [];
  for (const [index, part] of parts.entries()) {
    const more = /^and (\d[\d,]*) more$/.exec(part);
    if (more) {
      said.push(`and ${more[1] ?? ''} more`);
      continue;
    }
    const plain = sayPart(part);
    if (plain === null) {
      // Cut to the detail's length: the last part, part-said, is left for "and more".
      if (index === parts.length - 1 && index > 0 && part.endsWith('…')) {
        said.push('and more');
        break;
      }
      return null;
    }
    said.push(plain);
  }
  return said.join(joiner);
}

function schemaFault(fault: string): string | null {
  const context = /^@context (".*"?) isn't schema\.org$/.exec(fault);
  if (context) return `the details name ${curly(context[1] ?? '')} as their vocabulary, not the standard one`;
  const type = /^(\S+) isn't a schema\.org type$/.exec(fault);
  if (type) return `“${type[1] ?? ''}” isn’t a kind of thing search engines know`;
  const property = /^(.+?): "(.+?)" isn't a schema\.org property$/.exec(fault);
  if (property) return `“${property[2] ?? ''}” isn’t a detail search engines know (in ${property[1] ?? ''})`;
  const ownProperty = /^(.+?): "(.+?)" isn't a property of (.+)$/.exec(fault);
  if (ownProperty) return `a “${ownProperty[3] ?? ''}” can’t have “${ownProperty[2] ?? ''}”`;
  const date = /^(.+?): (\S+) "(.*?)" isn't an ISO 8601 date$/.exec(fault);
  if (date) return `the date “${date[3] ?? ''}” isn’t written the standard way (year-month-day)`;
  const currency = /^(.+?): priceCurrency "(.*?)" isn't ISO 4217$/.exec(fault);
  if (currency) return `the currency “${currency[2] ?? ''}” isn’t a standard three-letter code, such as GBP`;
  return null;
}

// --------------------------------------------------------------------------------------------- Security settings

function weakSetting(part: string): string | null {
  const hsts = /^HSTS max-age (\d+) \(.+?\), under a year$/.exec(part);
  if (hsts)
    return `it tells browsers to use the secure connection for ${plainLength(Number(hsts[1]))}; a year is advised`;
  if (part === 'HSTS without max-age')
    return 'it asks browsers to keep to the secure connection, but doesn’t say for how long';
  const unreadable = /^HSTS max-age "(.*)" unreadable$/.exec(part);
  if (unreadable) {
    return `it asks browsers to keep to the secure connection for a time that can’t be read (“${unreadable[1] ?? ''}”)`;
  }
  const options = /^X-Content-Type-Options "(.*)"$/.exec(part);
  if (options) return `the setting that stops browsers guessing file types is set wrongly (“${options[1] ?? ''}”)`;
  return null;
}

function insecureCookies(detail: string): string | null {
  const unsecured: string[] = [];
  const readable: string[] = [];
  const refused: string[] = [];
  let cut = false;
  const parts = detail.split('; ');
  for (const [index, part] of parts.entries()) {
    const cookie = /^(.+?): (.+)$/.exec(part);
    const faults = cookie?.[2]?.split(', ') ?? [];
    const known = faults.every((fault) => /^(?:no Secure|no HttpOnly|SameSite=None without Secure)$/.test(fault));
    if (!cookie || !known || faults.length === 0) {
      if (index === parts.length - 1 && index > 0 && part.endsWith('…')) {
        cut = true;
        break;
      }
      return null;
    }
    const name = `“${cookie[1] ?? ''}”`;
    if (faults.includes('no Secure')) unsecured.push(name);
    if (faults.includes('no HttpOnly')) readable.push(name);
    if (faults.includes('SameSite=None without Secure')) refused.push(name);
  }
  const cookies = (names: string[]) => `${names.length === 1 ? 'the cookie' : 'the cookies'} ${listed(names)}`;
  const said = [
    unsecured.length > 0 ? `${cookies(unsecured)} can be sent without the secure connection` : null,
    readable.length > 0
      ? `the page’s scripts can read ${cookies(readable)}, which ${readable.length === 1 ? 'looks' : 'look'} like a login`
      : null,
    refused.length > 0
      ? `browsers will refuse ${cookies(refused)}: shared with other sites, but not kept to the secure connection`
      : null,
  ].filter((part): part is string => part !== null);
  return said.length > 0 ? `${said.join('; ')}${cut ? '; and more' : ''}` : null;
}

// ------------------------------------------------------------------------------------------------- Each check

/** "1 other page has", "3 other pages have": a duplicate's other pages. */
function others(detail: string, what: string): string | null {
  const match = /^(\d[\d,]*) other pages?$/.exec(detail);
  if (!match) return null;
  const count = match[1] ?? '';
  return numberOf(count) === 1 ? `another page has the same ${what}` : `${count} other pages have the same ${what}`;
}

/** "3 images" and what they lack: "3 images with no alt text". */
function images(detail: string, what: string): string | null {
  const match = /^(\d[\d,]*) images?$/.exec(detail);
  return match ? `${counted(match[1] ?? '', 'image', 'images')} ${what}` : null;
}

/** "2 titles: "A" · "B"" → "The page has 2 titles: “A” · “B”"; an older engine's "2 titles" without them. */
function several(detail: string, written: string, said: string): string | null {
  const match = new RegExp(`^(\\d[\\d,]*) ${written}(?:: (.*))?$`).exec(detail);
  return match ? `the page has ${match[1] ?? ''} ${said}${match[2] ? `: ${curly(match[2])}` : ''}` : null;
}

/**
 * A width over the most results show: "736 px, over 580 (59 characters)"; older engines' "61 characters, over 60",
 * "61 characters (63 counting full-width characters as two)".
 */
function tooWide(detail: string): string | null {
  const match = /^(?:[\d,]+ px, over [\d,]+ \()?(\d[\d,]*) characters?(?: \([^)]*\))?\)?(?:, over [\d,]+)?$/.exec(
    detail,
  );
  return match
    ? `too long to show in full in search results (${counted(match[1] ?? '', 'character', 'characters')})`
    : null;
}

/** Too few characters: "157 px, under 200 (16 characters)", older engines' "68 characters", "16 characters, under 30". */
function tooShort(detail: string): string | null {
  const match = /^(?:[\d,]+ px, under [\d,]+ \()?(\d[\d,]*) characters?\)?(?:, under [\d,]+)?$/.exec(detail);
  return match ? `only ${counted(match[1] ?? '', 'character', 'characters')} long` : null;
}

/** A canonical's target, said after "the canonical URL": "The main address it gives is missing (404 Not Found)". */
function canonicalTarget(detail: string): string | null {
  const match = /^The canonical URL (.+)$/.exec(detail);
  const said = match ? statusPhrase(match[1] ?? '') : null;
  return said ? `the main address it gives ${said}` : null;
}

/** A sitemap or robots.txt file, and what went wrong reading it. */
function filesNotRead(detail: string, one: string, many: string): string | null {
  const failures = addressFailures(detail);
  if (!failures) return null;
  const items = failures.items.map(({ url, failure }) => `${url} (${plainFailureWords(failure)})`);
  const more = failures.more ? ` and ${failures.more} more` : '';
  if (items.length === 1 && !failures.more) {
    const only = failures.items[0];
    return `${one} ${only?.url ?? ''} couldn’t be read (${plainFailureWords(only?.failure ?? '')})`;
  }
  return `${many} couldn’t be read: ${items.join(', ')}${more}`;
}

/** Sitemaps over or near the limits: "https://x/a.xml (62,113 URLs)" → "https://x/a.xml (62,113 addresses)". */
function sitemapSizes(detail: string, lead: string): string | null {
  if (!/^\S+ \(/.test(detail)) return null;
  return `${lead}: ${detail.replace(/(\d[\d,]*) URLs?\b/g, (_, count: string) => counted(count, 'address', 'addresses'))}`;
}

/** The share and X card tags a page repeats: "2 og:title: "A" · "B"" → "2 different share titles: “A” · “B”". */
function repeatedShareTags(detail: string): string | null {
  const parts = detail.split(/; |, (?=\d+ (?:og|twitter):)/);
  const said = parts.map((part) => {
    const match = /^(\d+) ((?:og|twitter):[\w:]+)(?:: (.*))?$/.exec(part);
    const names = match ? SHARE_TAGS[match[2] ?? ''] : undefined;
    if (!match || !names) return null;
    return `${match[1] ?? ''} different ${names[1]}${match[3] ? `: ${curly(match[3])}` : ''}`;
  });
  return said.every((part): part is string => part !== null) ? said.join('; ') : null;
}

/** The kinds of relative canonical a canonical-relative group is by (the engine's IssueFaults.RelativeCanonical). */
const RELATIVE_CANONICALS: Readonly<Record<string, string>> = {
  'Root-relative: starts with /': 'it gives its main address without the site’s name, starting with “/”',
  'Protocol-relative: starts with //': 'it gives its main address without “https:”, starting with “//”',
  "Relative to the page's own address":
    'it gives its main address as a path from its own address, without the site’s name',
};

/**
 * What ended a page's head early, as a head-tags-in-body group is by (IssueFaults.HeadEndCause, without the tags or the
 * line): `<div id="promo"> ended the head early`, `Text "Free delivery" ended the head early`, or "After <body>" for
 * tags written in the body.
 */
function headEndCause(detail: string): string | null {
  if (detail === 'After <body>') {
    return 'some of what belongs in the top part of the page’s code is written further down, where it may be ignored';
  }
  const ended = /^(.+) ended the head early$/.exec(detail);
  if (!ended) return null;
  const text = /^Text "(.*)"$/i.exec(ended[1] ?? '');
  return text
    ? `words on the page (“${text[1] ?? ''}”) end the top part of its code early, so what comes after them may be ignored`
    : `code on the page (“${ended[1] ?? ''}”) ends the top part of its code early, so what comes after it may be ignored`;
}

const DETAIL_WORDS: Readonly<Record<string, Say>> = {
  // Response codes
  'internal-client-error': (detail) => (codeClause(detail) ? `it ${codeClause(detail) ?? ''}` : null),
  'internal-server-error': (detail) => (codeClause(detail) ? `it ${codeClause(detail) ?? ''}` : null),
  'internal-unreachable': (detail) => {
    if (detail === 'Timed out') return 'it didn’t answer in time';
    if (detail === 'Connection failed') return 'it couldn’t be reached';
    return `it ${plainFailureClause(detail) ?? 'couldn’t be reached'}`;
  },
  'internal-blocked': (detail) => `the site’s protection turned the check away: ${detail}`,
  'content-type-mismatch': (detail) => {
    const match = /^Used as (.+?), sent (with no Content-Type|as (.+))$/.exec(detail);
    if (!match) return null;
    const uses = (match[1] ?? '').replace(/\bstylesheet\b/g, 'style sheet');
    const sent = match[3] ? `says it’s ${plainFileKind(match[3])}` : 'doesn’t say what kind of file it is';
    return `pages use it as ${uses}, but the server ${sent}`;
  },
  'soft-404': (detail) => {
    if (detail === "The title or H1 says the page wasn't found") {
      return 'the title or main heading says the page wasn’t found';
    }
    const match = /^The (title|H1) reads as an error: "(.*?)"?(?: · (\d[\d,]*) words?, under [\d,]+)?$/.exec(detail);
    if (!match) return null;
    const what = match[1] === 'H1' ? 'main heading' : 'title';
    const words = match[3] ? `, and the page has only ${counted(match[3], 'word', 'words')}` : '';
    return `the ${what} reads like an error: “${match[2] ?? ''}”${words}`;
  },
  // Redirects
  'redirect-chain': (detail) => {
    const chain = /^(\d[\d,]*) redirects(?:, more than the (\d+) Googlebot follows)?$/.exec(detail);
    if (chain) {
      const google = chain[2] ? `, more than the ${chain[2]} Google follows` : '';
      return `it forwards ${chain[1] ?? ''} times in a row${google}`;
    }
    const endless = /^More than (\d+) redirects, which browsers don't follow$/.exec(detail);
    return endless ? `it forwards more than ${endless[1] ?? ''} times in a row, more than browsers follow` : null;
  },
  'redirect-loop': (detail) => {
    const bare = /^(\d[\d,]*) hops?$/.exec(detail);
    if (bare) return `it forwards round in a loop of ${steps(bare[1] ?? '')}`;
    const match = /^\d[\d,]* hops?: (.*)$/.exec(detail);
    if (!match) return null;
    const hops = (match[1] ?? '').split(' → ');
    const back = hops.at(-1)?.startsWith('back to ') ? hops.pop()?.slice('back to '.length) : null;
    const addresses = hops.map((hop) => hop.replace(/ \((?:\d{3}|meta refresh)\)$/, ''));
    return `it forwards round in a loop: ${addresses.join(', then ')}${back ? `, then back to ${back}` : ''}`;
  },
  'redirect-broken': (detail) => {
    const ends = /^Ends at a URL that (.+)$/.exec(detail);
    if (ends) {
      const said = statusPhrase(ends[1] ?? '');
      return said ? `it forwards to an address that ${said}` : null;
    }
    const nowhere = /^(.+) without a usable Location header$/.exec(detail);
    return nowhere ? `it says it forwards (${nowhere[1] ?? ''}) but not where to` : null;
  },
  'redirect-temporary': (detail) => (codeClause(detail) ? `it says the move is only for now (${detail})` : null),
  'redirect-temporary-cached': (detail) => {
    const match = /^(.+), cached for (.+)$/.exec(detail);
    return match
      ? `it says the move is only for now (${match[1] ?? ''}), but tells browsers to remember it for ${match[2] ?? ''}`
      : null;
  },
  'meta-refresh': (detail) => {
    const match = /^(The Refresh header )?(moves to (\S+)|reloads the page)(?: after (\d[\d,]*) seconds?)?$/i.exec(
      detail,
    );
    if (!match) return null;
    const who = match[1] ? 'the server' : 'the page';
    const action = match[3] ? `sends visitors on to ${match[3]}` : 'reloads itself';
    const delay =
      match[4] === undefined
        ? ''
        : numberOf(match[4]) === 0
          ? ' straight away'
          : ` after ${counted(match[4], 'second', 'seconds')}`;
    return `${who} ${action}${delay}`;
  },
  'redirect-downgrade': () => null,
  // Links
  'broken-internal-links': brokenLinks,
  'broken-external-links': brokenLinks,
  'links-to-redirects': (detail) => {
    const match = /^(\d[\d,]*) redirecting URLs?$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `links to ${counted(match[1] ?? '', 'old address', 'old addresses')} that ${one ? 'forwards' : 'forward'} elsewhere`;
  },
  'internal-nofollow-links': (detail) => {
    const match = /^Links to (\d[\d,]*) URLs?$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `links to ${counted(match[1] ?? '', 'address', 'addresses')}, telling search engines not to follow ${one ? 'it' : 'them'}`;
  },
  'empty-anchor-text': (detail) => {
    const match = /^Links to (\d[\d,]*) URLs?$/.exec(detail);
    return match ? `links to ${counted(match[1] ?? '', 'address', 'addresses')} with no words to click` : null;
  },
  'broken-fragment-links': (detail) => {
    const match = /^(\d[\d,]*) links?(?: to missing sections: (.*))?$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    const sections = match[2] ? `: ${match[2]}` : '';
    return `${counted(match[1] ?? '', 'link', 'links')} to ${one ? 'part of a page that isn’t' : 'parts of pages that aren’t'} there${sections}`;
  },
  'orphan-page': (detail) => {
    if (detail === 'Only found in a sitemap') return 'no page links here: it’s only in your sitemap';
    if (detail === 'Only nofollow links lead here') return 'the only links here tell search engines not to follow them';
    return null;
  },
  'uncrawlable-links': (detail) => {
    const match = /^(\d[\d,]*) links?: (.*?)( …)?$/.exec(detail);
    if (!match) return null;
    const examples = [...(match[2] ?? '').matchAll(/(?:"(.*?)"|no text) \([^)]*\)/g)].map((example) =>
      example[1] === undefined ? 'one with no text' : `“${example[1]}”`,
    );
    if (examples.length === 0) return null;
    return `${counted(match[1] ?? '', 'link works', 'links work')} only by script, which search engines can’t follow: ${examples.join(', ')}${match[3] ? ' and more' : ''}`;
  },
  'broken-resources': (detail) => {
    const match = /^(\d[\d,]*) broken files?(?: \((it|the first) (.+)\))?$/.exec(detail);
    if (!match) return null;
    const tail = brokenTail(match[2], match[3]);
    if (tail === null) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'file', 'files')} the page loads ${one ? 'is' : 'are'} broken${tail}`;
  },
  'links-to-localhost': (detail) => {
    const match = /^(\d[\d,]*) links?, such as (\S+)$/.exec(detail);
    if (!match) return null;
    const to = numberOf(match[1] ?? '') === 1 ? 'link to an address that works' : 'links to addresses that work';
    return `${match[1] ?? ''} ${to} only on your own computer or network, such as ${match[2] ?? ''}`;
  },
  'few-internal-outlinks': (detail) => {
    if (detail === 'No links to other pages') return 'it links to none of your other pages';
    const match = /^(\d[\d,]*) links? to (?:another page|other pages), under (\d[\d,]*)$/.exec(detail);
    if (!match) return null;
    return `it links to only ${counted(match[1] ?? '', 'other page', 'other pages')}; at least ${match[2] ?? ''} ${numberOf(match[2] ?? '') === 1 ? 'is' : 'are'} advised`;
  },
  'generic-anchor-text': (detail) => {
    const match = /^(\d[\d,]*) links?: (.*)$/.exec(detail);
    if (!match) return null;
    const texts = curly(match[2] ?? '').replace(/ ×(\d+)/g, ' ($1 times)');
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'link', 'links')} whose words don’t say where ${one ? 'it goes' : 'they go'}: ${texts}`;
  },
  'non-indexable-inlinks-only': (detail) => {
    if (detail === "Linked from 1 page, which isn't indexable") {
      return 'only 1 page links here, and it can’t appear in search results';
    }
    const match = /^Linked from (\d[\d,]*) pages, none of them indexable$/.exec(detail);
    return match ? `only ${match[1] ?? ''} pages link here, and none of them can appear in search results` : null;
  },
  'deep-page': (detail) => {
    const match = /^(\d[\d,]*) clicks? from the start, over (\d[\d,]*)$/.exec(detail);
    return match
      ? `${counted(match[1] ?? '', 'click', 'clicks')} from the home page; at most ${match[2] ?? ''} is advised`
      : null;
  },
  'too-many-links': (detail) => {
    const match = /^(\d[\d,]*) internal links, over (\d[\d,]*)$/.exec(detail);
    return match ? `${match[1] ?? ''} links to your own pages, more than ${match[2] ?? ''}` : null;
  },
  'slow-external-links': (detail) => {
    const match = /^(\d[\d,]*) slow links?, the slowest ([\d.]+) s$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'link', 'links')} to ${one ? 'a slow page' : 'slow pages'} on other sites; the slowest took ${plainSeconds(Number(match[2]) * 1000)}`;
  },
  'pagination-not-linked': (detail) => {
    const match = /^rel="(next|prev)" (\S+) has no link$/.exec(detail);
    return match
      ? `it names ${match[2] ?? ''} as the ${match[1] === 'next' ? 'next' : 'previous'} page, but doesn’t link to it`
      : null;
  },
  'pagination-broken': pagedTarget,
  'pagination-to-non-indexable': pagedTarget,
  'pagination-sequence-error': (detail) => {
    // The kinds its groups are by (the engine's IssueFaults.PaginationFault), then each page's own.
    if (detail === "The next page doesn't point back") return 'the next page doesn’t name it as the one before it';
    if (detail === 'rel="next" leads back to an earlier page')
      return 'its next pages lead back round to an earlier page';
    if (detail === 'More than one rel="next" or rel="prev"') return 'it names more than one next or previous page';
    const back = /^(\S+) doesn't point back$/.exec(detail);
    if (back) return `${back[1] ?? ''} doesn’t name this page as the one before it`;
    const itself = /^rel="(next|prev)" names the page itself$/.exec(detail);
    if (itself) return `it names itself as the ${itself[1] === 'next' ? 'next' : 'previous'} page`;
    const many = /^(\d[\d,]*) rel="(next|prev)" URLs$/.exec(detail);
    if (many) return `it names ${many[1] ?? ''} different ${many[2] === 'next' ? 'next' : 'previous'} pages`;
    const round = /^rel="next" leads back to (\S+)$/.exec(detail);
    return round ? `its next pages lead back round to ${round[1] ?? ''}` : null;
  },
  // Indexability
  'noindex-page': () => null,
  'nofollow-page': () => null,
  'blocked-by-robots': (detail) => {
    const unreachable = /^robots\.txt unreachable: (.+)$/.exec(detail);
    if (unreachable) {
      return `the rules file couldn’t be read (${plainFailureWords(unreachable[1] ?? '')}), so search engines stay out`;
    }
    const rule = /^((?:Disallow|Allow):.*), line (\d[\d,]*)$/i.exec(detail);
    return rule ? `the rules file says “${rule[1] ?? ''}”, on line ${rule[2] ?? ''}` : null;
  },
  'snippet-blocked': (detail) => `the page tells search engines not to show a text preview of it (“${detail}”)`,
  'robots-txt-unreachable': (detail) => filesNotRead(detail, 'the rules file', 'these rules files'),
  'robots-txt-unreachable-other-origin': (detail) => filesNotRead(detail, 'the rules file', 'these rules files'),
  'robots-txt-missing': (detail) => {
    const failures = addressFailures(detail);
    if (!failures) return null;
    const items = failures.items.map(({ url, failure }) => `${url} (${plainFailureWords(failure)})`);
    return `there’s no rules file at ${listed(items)}${failures.more ? ` and ${failures.more} more` : ''}`;
  },
  'site-hidden-from-search': (detail) => {
    const match = /^(\d[\d,]*) of (\d[\d,]*) pages? (?:is|are) noindex$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${match[1] ?? ''} of ${counted(match[2] ?? '', 'page', 'pages')} ${one ? 'asks' : 'ask'} not to appear in search results`;
  },
  'robots-directives-conflict': (detail) => {
    const match = /^The HTML says "(.*?)"; the X-Robots-Tag header says "(.*?)"?$/.exec(detail);
    return match ? `the page says “${match[1] ?? ''}”, but the server says “${match[2] ?? ''}”` : null;
  },
  'noindex-near-home': (detail) => {
    const match = /^(\d[\d,]*) clicks? from the start$/.exec(detail);
    return match ? `${counted(match[1] ?? '', 'click', 'clicks')} from the home page` : null;
  },
  'unavailable-after-passed': (detail) => {
    const match = /^unavailable_after: (.+), passed$/.exec(detail);
    return match ? `the page told search engines to drop it after ${match[1] ?? ''}, which has passed` : null;
  },
  'media-preview-blocked': (detail) => {
    const what =
      /image/i.test(detail) && /video/i.test(detail) ? 'images or videos' : /video/i.test(detail) ? 'videos' : 'images';
    return `the page tells search engines not to show its ${what} in previews (“${detail}”)`;
  },
  'translation-blocked': () => null,
  'indexifembedded-without-noindex': () => null,
  // Canonicals
  'canonicalised-page': () => null,
  'canonical-missing': () => null,
  'canonical-multiple': (detail) => {
    const bare = /^(\d[\d,]*) different URLs$/.exec(detail);
    if (bare) return `it names ${bare[1] ?? ''} different main addresses`;
    const match = /^(\d[\d,]*) URLs: (.*)$/.exec(detail);
    if (!match) return null;
    const named = (match[2] ?? '').split(' · ').map((item) => {
      const where = /^(\S+) \((head|HTTP header)\)$/.exec(item);
      if (!where) return item;
      return `${where[1] ?? ''} (${where[2] === 'head' ? 'in the page’s code' : 'from the server'})`;
    });
    return `it names ${match[1] ?? ''} different main addresses: ${named.join(' · ')}`;
  },
  'canonical-broken': canonicalTarget,
  'canonical-to-redirect': canonicalTarget,
  'canonical-to-non-indexable': canonicalTarget,
  'canonical-outside-head': (detail) => headEnded(detail),
  'canonical-relative': (detail) =>
    RELATIVE_CANONICALS[detail] ?? `it gives its main address without the site’s name: ${detail}`,
  'canonical-invalid': (detail) => {
    // The kinds its groups are by (IssueFaults.InvalidCanonical), then each page's own address.
    if (detail === 'Empty') return 'the main address it gives is empty';
    if (detail === 'Malformed URL') return 'the main address it gives isn’t a real web address';
    const scheme = /^Uses (\S+): instead of http\(s\)$/.exec(detail);
    if (scheme) return `the main address it gives isn’t a web address: it starts with “${scheme[1] ?? ''}:”`;
    return `the main address it gives isn’t a real web address: “${detail}”`;
  },
  'noindex-with-canonical': () => null,
  'canonical-to-first-page': (detail) => {
    const page = /^Page (\d[\d,]*) names page 1 as its canonical$/.exec(detail);
    if (page) return `page ${page[1] ?? ''} names page 1 as its main address`;
    return detail === 'A later page names page 1 as its canonical'
      ? 'a later page names page 1 as its main address'
      : null;
  },
  'canonical-other-domain': (detail) => {
    const match = /^On another host: (.+)$/.exec(detail);
    return match ? `the main address it gives is on another website: ${match[1] ?? ''}` : null;
  },
  'canonical-loop': (detail) => {
    const match = /^A loop of \d[\d,]*: (.*)$/.exec(detail);
    if (!match) return null;
    const addresses = (match[1] ?? '').split(' → ');
    const back = addresses.at(-1)?.startsWith('back to ') ? addresses.pop()?.slice('back to '.length) : null;
    return `the main addresses go round in a loop: ${addresses.join(', then ')}${back ? `, then back to ${back}` : ''}`;
  },
  'canonical-on-redirect': (detail) => {
    const match = /^Redirects to (\S+); its canonical header names (\S+)$/.exec(detail);
    return match
      ? `it forwards to ${match[1] ?? ''}, but the server names ${match[2] ?? ''} as its main address`
      : null;
  },
  'canonical-fragment': (detail) => `the main address it gives points to part of a page: ${detail}`,
  'canonical-invalid-attribute': (detail) =>
    `the main address is marked for one kind of screen or language only (“${detail}”), so search engines ignore it`,
  'canonical-unlinked': (detail) =>
    detail === 'Only canonicals lead here' ? 'other pages name it as their main address, but none links to it' : null,
  // Sitemaps
  'sitemap-non-indexable': (detail) => {
    const match = /^The URL (.+)$/.exec(detail);
    const said = match ? statusPhrase(match[1] ?? '') : null;
    return said ? `it ${said}` : null;
  },
  'not-in-sitemap': () => null,
  'sitemap-error': (detail) => filesNotRead(detail, 'the sitemap', 'these sitemaps'),
  'sitemap-missing': () => null,
  'sitemap-url-other-host': (detail) => {
    const schemes = /^The URL is (\w+):\/\/, the sitemap (\w+):\/\/$/.exec(detail);
    if (schemes) return `its address starts ${schemes[1] ?? ''}://, but the sitemap’s starts ${schemes[2] ?? ''}://`;
    const match = /^The URL (.+)$/.exec(detail);
    const said = match ? statusPhrase(match[1] ?? '') : null;
    return said ? `it ${said}` : null;
  },
  'robots-txt-no-sitemap': (detail) => {
    const match = /^(\S+) has no Sitemap: line; the sitemap was found at (\S+)$/.exec(detail);
    return match
      ? `the rules file ${match[1] ?? ''} doesn’t say where your sitemap is; it was found at ${match[2] ?? ''}`
      : null;
  },
  'sitemap-over-limit': (detail) => sitemapSizes(detail, 'too big for search engines to read in full'),
  'sitemap-near-limit': (detail) => sitemapSizes(detail, 'close to the most search engines read'),
  'sitemap-empty': (detail) => {
    const more = /^(\S+) and (\d[\d,]*) more$/.exec(detail);
    if (more) return `the sitemaps ${more[1] ?? ''} and ${more[2] ?? ''} more list no pages`;
    return /^\S+$/.test(detail) ? `the sitemap ${detail} lists no pages` : null;
  },
  'sitemap-url-repeated': (detail) => {
    const match = /^In (\d[\d,]*) sitemaps$/.exec(detail);
    return match ? `listed in ${match[1] ?? ''} of your sitemaps` : null;
  },
  'sitemap-lastmod-invalid': (detail) => {
    const match = /^(\d[\d,]*) (?:entry|entries): (.+) \((.+)\)$/.exec(detail);
    if (!match) return null;
    const kinds = (match[2] ?? '').replace(/(\d[\d,]*) unreadable/, '$1 that can’t be read');
    return `${counted(match[1] ?? '', 'entry has', 'entries have')} a wrong “last changed” date (${kinds}), in ${match[3] ?? ''}`;
  },
  // Titles
  'title-missing': () => null,
  'title-multiple': (detail) => several(detail, 'titles', 'titles'),
  'title-too-long': tooWide,
  'title-too-short': tooShort,
  'title-duplicate': (detail) => others(detail, 'title'),
  // Meta descriptions
  'description-missing': () => null,
  'description-multiple': (detail) => several(detail, 'meta descriptions', 'search descriptions'),
  'description-too-long': tooWide,
  'description-too-short': tooShort,
  'description-duplicate': (detail) => others(detail, 'search description'),
  // Headings
  'h1-missing': (detail) => (detail === 'The H1 has no text' ? 'the main heading has no words' : null),
  'h1-multiple': (detail) => several(detail, 'H1s', 'main headings'),
  'h1-duplicate': (detail) => others(detail, 'main heading'),
  'heading-levels-skipped': (detail) => {
    const before = /^An H([2-6]) before the H1$/.exec(detail);
    if (before) return `${headingName(Number(before[1]), 'a')} comes before the main heading`;
    const match = /^H([1-6]) → H([1-6])(?:: (.*))?$/.exec(detail);
    if (!match) return null;
    const from = Number(match[1]);
    const to = Number(match[2]);
    if (to <= from + 1) return null;
    const quoted = match[3] ? `, ${curly(match[3])}` : '';
    return `it goes from ${headingName(from, 'a')} to ${headingName(to, 'a')}${quoted}, skipping ${skippedLevels(from, to)}`;
  },
  'h1-image-only': (detail) => {
    const match = /^The H1 is an image: (.*)$/.exec(detail);
    return match ? `the main heading is a picture: ${curly(match[1] ?? '')}` : null;
  },
  'heading-too-long': (detail) => {
    const match = /^H([1-6]) of (\d[\d,]*) characters, over (\d[\d,]*)(?:: (.*))?$/.exec(detail);
    if (!match) return null;
    const quoted = match[4] ? `: ${curly(match[4])}` : '';
    return `${headingName(Number(match[1]), 'a')} is ${match[2] ?? ''} characters long, more than ${match[3] ?? ''}${quoted}`;
  },
  'h2-missing': (detail) => {
    const match = /^(\d[\d,]*) words, no H2$/.exec(detail);
    return match ? `${match[1] ?? ''} words with no subheadings` : null;
  },
  // Content
  'low-word-count': (detail) => {
    const match = /^(\d[\d,]*) words?(?:, under (\d[\d,]*))?$/.exec(detail);
    if (!match) return null;
    return `only ${counted(match[1] ?? '', 'word', 'words')}${match[2] ? `, fewer than ${match[2]}` : ''}`;
  },
  'placeholder-text': (detail) => (detail.startsWith('"') ? `placeholder text: ${curly(detail)}` : null),
  'readability-low': (detail) => {
    const match = /^Reading ease (-?\d[\d,]*), under (\d+) \(very difficult\)$/.exec(detail);
    return match
      ? `very hard to read: it scores ${match[1] ?? ''} for reading ease, where under ${match[2] ?? ''} is very hard`
      : null;
  },
  'boilerplate-heavy': (detail) => {
    const match = /^(\d[\d,]*) of (\d[\d,]*) words are the page's own \((\d+)%\)$/.exec(detail);
    return match
      ? `only ${match[1] ?? ''} of its ${match[2] ?? ''} words are its own (${match[3] ?? ''}%); the rest is shared with other pages`
      : null;
  },
  'duplicate-content': (detail) => others(detail, 'text'),
  'near-duplicate-content': (detail) => {
    const match = /^(\d[\d,]*) similar pages?$/.exec(detail);
    if (!match) return null;
    const count = match[1] ?? '';
    return numberOf(count) === 1
      ? 'another page has almost the same text'
      : `${count} other pages have almost the same text`;
  },
  'duplicate-site-version': (detail) => {
    const answers = /^Answers (.+) instead of redirecting to (\S+)$/.exec(detail);
    if (answers) return `it opens (${answers[1] ?? ''}) instead of forwarding to ${answers[2] ?? ''}`;
    const redirects = /^Redirects to (\S+), which answers (.+) instead of redirecting to (\S+)$/.exec(detail);
    return redirects
      ? `it forwards to ${redirects[1] ?? ''}, which opens (${redirects[2] ?? ''}) instead of forwarding to ${redirects[3] ?? ''}`
      : null;
  },
  // Images
  'image-missing-alt': (detail) => images(detail, 'with no alt text'),
  'image-alt-too-long': (detail) => images(detail, 'with very long alt text'),
  'image-missing-dimensions': (detail) => images(detail, 'with no size set'),
  'image-broken': (detail) => {
    const match = /^(\d[\d,]*) images?(?: \((it|the first) (.+)\))?$/.exec(detail);
    if (!match) return null;
    const tail = brokenTail(match[2], match[3]);
    return tail === null ? null : `${counted(match[1] ?? '', 'broken image', 'broken images')}${tail}`;
  },
  'image-too-large': (detail) => {
    const match = /^(\d[\d,]*) images? over ([\d.]+ [KMG]B)(?:: ([\d.]+ [KMG]B)|, the largest ([\d.]+ [KMG]B))?$/.exec(
      detail,
    );
    if (!match) return null;
    const size = match[3] ? `: ${match[3]}` : match[4] ? `, the largest ${match[4]}` : '';
    return `${counted(match[1] ?? '', 'image', 'images')} bigger than ${match[2] ?? ''}${size}`;
  },
  'image-missing-src': (detail) => {
    const match = /^(\d[\d,]*) images?(?:(, in the [\w ]+)|: (.+)|, the first: (.+))?$/.exec(detail);
    if (!match) return null;
    const lead = `${counted(match[1] ?? '', 'image', 'images')} with no picture file`;
    if (match[2]) return `${lead}${match[2]}`;
    const which = match[3] ?? match[4];
    if (!which) return lead;
    const place = /^(.*?)(, in the [\w ]+)?$/.exec(which);
    const mark = imageMark(place?.[1] ?? which);
    if (!mark) return null;
    const where = place?.[2] ?? '';
    if (match[3]) return `${lead}, ${mark.startsWith('alt') ? `with ${mark}` : mark}${where}`;
    return `${lead}; the first ${mark.startsWith('alt') ? `has ${mark}` : `is ${mark}`}${where}`;
  },
  'image-no-srcset': (detail) => {
    const one = /^1 image, (\d[\d,]*) px wide$/.exec(detail);
    if (one) return `1 image with one size for every screen, ${one[1] ?? ''} pixels wide`;
    const many = /^(\d[\d,]*) images, the widest (\d[\d,]*) px$/.exec(detail);
    return many ? `${many[1] ?? ''} images with one size for every screen, the widest ${many[2] ?? ''} pixels` : null;
  },
  'image-legacy-format': (detail) => {
    const one = /^1 image: (.+)$/.exec(detail);
    if (one) return `1 image in an older, heavier file type: ${one[1] ?? ''}`;
    const many = /^(\d[\d,]*) images, the largest (.+)$/.exec(detail);
    return many ? `${many[1] ?? ''} images in older, heavier file types, the largest ${many[2] ?? ''}` : null;
  },
  'image-alt-unhelpful': (detail) => {
    const match = /^(\d[\d,]*) images?$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'image', 'images')} whose alt text doesn’t describe ${one ? 'it' : 'them'}`;
  },
  'image-hotlinked': (detail) => {
    const match = /^(\d[\d,]*) images? from (\d[\d,]*) sites?$/.exec(detail);
    if (!match) return null;
    const sites = numberOf(match[2] ?? '') === 1 ? 'another website' : `${match[2] ?? ''} other websites`;
    return `${counted(match[1] ?? '', 'image', 'images')} shown straight from ${sites}`;
  },
  'image-inline-large': (detail) => {
    const match =
      /^(?:1 inline image: ([\d.]+ [KMG]B)|(\d[\d,]*) inline images, the largest ([\d.]+ [KMG]B))(?:, (.+))?$/.exec(
        detail,
      );
    if (!match) return null;
    const mark = match[4] ? imageMark(match[4]) : null;
    if (match[4] && !mark) return null;
    const which = mark ? `, ${mark.startsWith('alt') ? `with ${mark}` : mark}` : '';
    return match[1]
      ? `1 image written into the page’s code: ${match[1]}${which}`
      : `${match[2] ?? ''} images written into the page’s code, the largest ${match[3] ?? ''}${which}`;
  },
  // Hreflang
  'hreflang-invalid-code': (detail) => `it uses a language code that doesn’t exist: “${detail}”`,
  'hreflang-missing-self': (detail) => {
    const match =
      /^(?:None of its (\d[\d,]*) entries names this URL|Its one entry doesn't name this URL)(?:; "(.+?)" names (\S+))?$/.exec(
        detail,
      );
    if (!match) return null;
    const lead = match[1]
      ? `its list of ${match[1]} language versions leaves out this page`
      : 'the one language version it lists isn’t this page';
    return `${lead}${match[2] ? `; it gives “${match[2]}” to ${match[3] ?? ''}` : ''}`;
  },
  'hreflang-missing-return': (detail) => {
    const match = /^(\d[\d,]*) alternates? (?:doesn't|don't) link back$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'language version', 'language versions')} ${one ? 'doesn’t' : 'don’t'} name this page back`;
  },
  'hreflang-broken-target': (detail) => {
    const match = /^(\d[\d,]*) URLs?$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'language version', 'language versions')} ${one ? 'is' : 'are'} broken`;
  },
  'hreflang-to-non-indexable': (detail) => {
    const match = /^(\d[\d,]*) URLs?$/.exec(detail);
    return match
      ? `${counted(match[1] ?? '', 'language version', 'language versions')} can’t appear in search results`
      : null;
  },
  'hreflang-duplicate-code': (detail) =>
    // A group's codes, without their addresses (IssueFaults.HreflangCodes): "en-gb, de".
    /^[^\s:;,]+(?:, [^\s:;,]+)+$/.test(detail)
      ? `more than one address is given for each of ${listed(detail.split(', ').map((code) => `“${code}”`))}`
      : eachPart(detail, (part) => {
          // An older engine's names the code alone.
          const match = /^([^\s:]+)(?:: (.+))?$/.exec(part);
          return match
            ? `more than one address is given for “${match[1] ?? ''}”${match[2] ? `: ${match[2]}` : ''}`
            : null;
        }),
  'hreflang-missing-x-default': () => null,
  'hreflang-outside-head': (detail) => {
    const match = /^(\d[\d,]*) (?:entry|entries) in the body(?:: (.*))?$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    const lead = `${counted(match[1] ?? '', 'language version is', 'language versions are')} listed below the top part of the page’s code, where search engines ignore ${one ? 'it' : 'them'}`;
    if (!match[2]) return lead;
    const line = /line (\d[\d,]*)/.exec(match[2])?.[1];
    if (/^it's in the body/i.test(match[2])) return line ? `${lead} (from line ${line})` : lead;
    return `${lead}: ${line ? `code on line ${line}` : 'code on the page'} ends the top part early`;
  },
  'hreflang-inconsistent-code': (detail) => {
    const match = /^Names (\S+) as "(.+?)"; \S+ calls itself "(.+?)"?$/.exec(detail);
    return match
      ? `it calls ${match[1] ?? ''} “${match[2] ?? ''}”, but ${match[1] ?? ''} calls itself “${match[3] ?? ''}”`
      : null;
  },
  'hreflang-header-mismatch': (detail) => {
    const match =
      /^The HTML names (\d[\d,]*) versions?, the header (\d[\d,]*)((?:; only in the (?:HTML|header): .+?)*)$/.exec(
        detail,
      );
    if (!match) return null;
    const lead = `the page’s code lists ${counted(match[1] ?? '', 'language version', 'language versions')}, but the server lists ${match[2] ?? ''}`;
    const codes = (list: string) => listed(list.split(', ').map((code) => `“${code}”`));
    const only = [...(match[3] ?? '').matchAll(/; only in the (HTML|header): (.+?)(?=; only in the |$)/g)].map(
      (part) => `; only ${part[1] === 'HTML' ? 'the page’s code' : 'the server'} has ${codes(part[2] ?? '')}`,
    );
    return `${lead}${only.join('')}`;
  },
  'hreflang-unlinked': (detail) =>
    detail === 'No link or sitemap entry leads here'
      ? 'other pages name it as a language version, but no link or sitemap leads here'
      : null,
  'hreflang-lang-mismatch': (detail) => {
    const match = /^hreflang (\S+), lang (\S+)$/.exec(detail);
    return match ? `listed as “${match[1] ?? ''}”, but the page says its language is “${match[2] ?? ''}”` : null;
  },
  'hreflang-self-only': () => null,
  // Structured data
  'structured-data-invalid': structuredDataInvalid,
  'structured-data-missing-required': (detail) =>
    eachPart(detail, (part) => {
      const match = /^(.+?): needs (.+)$/.exec(part);
      return match ? `the ${match[1] ?? ''} details lack what Google needs: ${neededParts(match[2] ?? '')}` : null;
    }),
  'structured-data-schema-errors': (detail) => eachPart(detail, schemaFault),
  'structured-data-missing-recommended': (detail) =>
    eachPart(detail, (part) => {
      const match = /^(.+?): lacks (.+)$/.exec(part);
      return match ? `the ${match[1] ?? ''} details lack what Google recommends: ${neededParts(match[2] ?? '')}` : null;
    }),
  'structured-data-on-noindex': (detail) => {
    const types = detail.split(', ').map((type) => `“${type}”`);
    return `it describes ${listed(types)} for search results, but asks not to appear in them`;
  },
  // Security
  'no-https': () => null,
  'http-page': () => null,
  'mixed-content': (detail) => {
    const match = /^(\d[\d,]*) HTTP resources?$/.exec(detail);
    return match ? `${counted(match[1] ?? '', 'file', 'files')} loaded without https` : null;
  },
  'security-headers-missing': (detail) => {
    const match = /^Missing (.+)$/.exec(detail);
    if (!match) return null;
    const settings = (match[1] ?? '').split(', ').map((name) => SECURITY_SETTINGS[name.trim()] ?? null);
    if (settings.some((setting) => setting === null)) return null;
    return `your site doesn’t send the security settings that ${listed(settings as string[])}`;
  },
  'security-headers-weak': (detail) => eachPart(detail, weakSetting),
  'cookies-insecure': insecureCookies,
  'form-not-secure': (detail) => {
    const sends = /^Sends to (\S+)$/.exec(detail);
    if (sends) return `a form sends what’s typed to ${sends[1] ?? ''}, without the secure connection`;
    return detail === 'A password form on an http:// page'
      ? 'a password form on a page without the secure connection'
      : null;
  },
  'protocol-relative-links': (detail) => {
    const match = /^(\d[\d,]*) URLs?, such as (\S+)$/.exec(detail);
    if (!match) return null;
    const one = numberOf(match[1] ?? '') === 1;
    return `${counted(match[1] ?? '', 'address', 'addresses')} that ${one ? 'leaves' : 'leave'} out “https:”, such as ${match[2] ?? ''}`;
  },
  // URLs
  'url-uppercase': () => null,
  'url-underscores': () => null,
  'url-too-long': (detail) => {
    const match = /^(\d[\d,]*) characters(?:, over (\d[\d,]*))?$/.exec(detail);
    if (!match) return null;
    return `the address is ${match[1] ?? ''} characters long${match[2] ? `, more than ${match[2]}` : ''}`;
  },
  'url-tracking-parameters': (detail) => {
    const names = detail.split(', ').map((name) => `“${name}”`);
    return `the address carries ${names.length === 1 ? 'a tracking code' : 'tracking codes'}: ${listed(names)}`;
  },
  'url-multiple-slashes': () => null,
  'url-space': () => null,
  'url-parameters': (detail) => {
    const names = detail.split(', ').map((name) => `“${name}”`);
    return `the address has extra settings after a “?”: ${listed(names)}`;
  },
  'url-non-ascii': () => null,
  'url-repetitive-path': () => null,
  'url-internal-search': (detail) => {
    const names = detail.split(', ').map((name) => `“${name}”`);
    return `it’s a page of results from your site’s own search (${listed(names)})`;
  },
  // Performance
  'slow-response': (detail) => {
    // An older engine's leaves out the limit.
    const match = /^(\d[\d,]*) ms(?:, over (\d[\d,]*) ms)?(?: \(first byte after (\d[\d,]*) ms\))?$/.exec(detail);
    if (!match) return null;
    const took = numberOf(match[1] ?? '');
    const first = match[3] === undefined ? null : numberOf(match[3]);
    const began =
      first !== null && plainSeconds(first) !== plainSeconds(took)
        ? `; it began to answer after ${plainSeconds(first)}`
        : '';
    const over = match[2] === undefined ? '' : `, more than ${plainSeconds(numberOf(match[2]))}`;
    return `it took ${plainSeconds(took)} to open${over}${began}`;
  },
  'large-page': (detail) => {
    const match = /^([\d.]+ [KMG]B)(?:, over ([\d.]+ [KMG]B))?$/.exec(detail);
    return match ? `the page is ${match[1] ?? ''}${match[2] ? `, more than ${match[2]}` : ''}` : null;
  },
  'html-past-crawl-limit': (detail) => pastGoogle(detail, 'page'),
  'resource-past-crawl-limit': (detail) => pastGoogle(detail, 'file'),
  // HTML
  'viewport-missing': () => null,
  'lang-missing': () => null,
  'charset-missing': (detail) =>
    detail.startsWith('A meta tag declares it, but ends at byte')
      ? 'the page names its text encoding too far down its code, past where browsers look'
      : null,
  'open-graph-missing': (detail) => {
    const match = /^(?:Missing ([^;]+?))?(?:(?:; )?[Ee]mpty (.+))?$/.exec(detail);
    if (!match || (!match[1] && !match[2])) return null;
    const names = (tags: string | undefined) =>
      (tags ?? '').split(', ').map((tag) => SHARE_TAGS[tag.trim()]?.[0].replace(/^share /, '') ?? null);
    const missing = match[1] ? names(match[1]) : [];
    const empty = match[2] ? names(match[2]) : [];
    if ([...missing, ...empty].some((name) => name === null)) return null;
    const parts = [
      missing.length > 0 ? `it has no ${listed(missing as string[], 'or')}` : null,
      empty.length > 0 ? `its ${listed(empty as string[])} ${empty.length === 1 ? 'is' : 'are'} empty` : null,
    ].filter((part): part is string => part !== null);
    return `for sharing, ${parts.join(', and ')}`;
  },
  'x-card-missing': () => null,
  'x-card-incomplete': (detail) => {
    const faults = detail.split('; ').map((fault) => {
      if (fault.startsWith('No title (')) return 'no title';
      if (fault.startsWith('No image (')) return 'no picture';
      if (fault === 'No twitter:player') return 'no player';
      const card = /^Card type "(.*)" isn't one X shows$/.exec(fault);
      if (card) return `a card type X doesn’t show (“${card[1] ?? ''}”)`;
      if (fault.startsWith('twitter: tags written as property=')) return 'settings written in a way X doesn’t read';
      return null;
    });
    return faults.every((fault): fault is string => fault !== null) ? `the X card has ${listed(faults)}` : null;
  },
  'social-image-relative': (detail) => {
    const match = /^((?:og|twitter):\S+) is (.+)$/.exec(detail);
    return match ? `${shareImage(match[1] ?? '')}’s address leaves out the site: ${match[2] ?? ''}` : null;
  },
  'og-url-not-canonical': (detail) => {
    const match = /^og:url is (.+)$/.exec(detail);
    return match ? `the address it gives for sharing is ${match[1] ?? ''}, not its main address` : null;
  },
  'social-tags-multiple': repeatedShareTags,
  'og-title-duplicate': (detail) => others(detail, 'share title'),
  'og-description-duplicate': (detail) => others(detail, 'share description'),
  'social-image-broken': (detail) => {
    const match = /^((?:og|twitter):\S+) (.+)$/.exec(detail);
    if (!match) return null;
    const image = shareImage(match[1] ?? '');
    const rest = match[2] ?? '';
    if (rest === 'is a page, not an image') return `${image} is a page, not a picture`;
    if (rest === "isn't sent as an image") return `${image} isn’t sent as a picture`;
    const said = statusPhrase(rest);
    return said ? `${image} ${said}` : null;
  },
  'social-image-too-large': (detail) => {
    const match = /^((?:og|twitter):\S+) is ([\d.]+ [KMG]B), over ([\d.]+ [KMG]B)$/.exec(detail);
    return match ? `${shareImage(match[1] ?? '')} is ${match[2] ?? ''}, more than ${match[3] ?? ''}` : null;
  },
  'social-image-too-small': (detail) => {
    const match = /^((?:og|twitter):\S+) is (\d[\d,]*) × (\d[\d,]*) px, under (\d+) × (\d+)(?: \((.+)\))?$/.exec(
      detail,
    );
    if (!match) return null;
    const who = match[6] ? `${match[6].replace("'", '’')} needs` : 'sites need';
    return `${shareImage(match[1] ?? '')} is ${match[2] ?? ''}×${match[3] ?? ''} pixels; ${who} at least ${match[4] ?? ''}×${match[5] ?? ''}`;
  },
  'head-tags-in-body': (detail) => {
    const match = /^(.+?) (is|are) in the body(.*)$/.exec(detail);
    if (!match) return headEndCause(detail);
    const names = (match[1] ?? '').split(/, | and /).map(headTagName);
    const many = match[2] === 'are';
    const lead = `${listed(names)} ${many ? 'are' : 'is'} below the top part of the page’s code, where ${many ? 'they' : 'it'} may be ignored`;
    const cause = match[3] ?? '';
    if (cause === '') return lead;
    const line = /line (\d[\d,]*)/.exec(cause)?.[1];
    if (cause.startsWith(', after <body>')) return line ? `${lead} (from line ${line})` : lead;
    return `${lead}: ${line ? `code on line ${line}` : 'code on the page'} ends the top part early`;
  },
  'html-structure-errors': (detail) => {
    const parts = eachPart(detail, (part) => {
      const twice = /^(\d+) <(head|body)> tags(?: \(the second on line (\d[\d,]*)\))?$/.exec(part);
      if (twice) {
        const name = twice[2] === 'head' ? 'top sections (head)' : 'main sections (body)';
        return `${twice[1] ?? ''} ${name}${twice[3] ? `, the second on line ${twice[3]}` : ''}`;
      }
      if (part === '<html> after content') return 'content before its outer section (html)';
      if (part === '<head> after content') return 'content before its top section (head)';
      return null;
    });
    return parts ? `the page’s code has ${parts}` : null;
  },
  'viewport-invalid': (detail) =>
    eachPart(detail, (part) => {
      if (part.startsWith('user-scalable=')) return 'it stops visitors zooming in';
      if (part.startsWith('maximum-scale=')) return 'it limits how far visitors can zoom in';
      if (part === 'no width or initial-scale') return 'it doesn’t tell phones how wide to show the page';
      const differ = /^(\d+) viewport tags that differ$/.exec(part);
      return differ ? `it gives phones ${differ[1] ?? ''} different screen settings` : null;
    }),
  'charset-invalid': (detail) => {
    // An older engine's, for what charset-missing now says.
    if (detail.startsWith('<meta charset> after the first')) {
      return 'the page names its text encoding too far down its code, past where browsers look';
    }
    const declares = /^Declares (.+), not UTF-8$/.exec(detail);
    if (declares) return `it says its text encoding is “${declares[1] ?? ''}”, not the standard one (UTF-8)`;
    const differ = /^The header says (.+); the page says (.+)$/.exec(detail);
    if (differ)
      return `the server says its text encoding is “${differ[1] ?? ''}”, but the page says “${differ[2] ?? ''}”`;
    const several = /^(\d+) declarations: (.+)$/.exec(detail);
    if (!several) return null;
    const names = (several[2] ?? '').split(', ').map((name) => `“${name}”`);
    return `it names ${several[1] ?? ''} different text encodings: ${listed(names)}`;
  },
  'lang-invalid': (detail) => `it gives its language as “${detail}”, which isn’t a language code such as “en”`,
  'mobile-alternate': (detail) => {
    const match = /^Mobile version: (.+)$/.exec(detail);
    return match ? `it has a separate mobile version: ${match[1] ?? ''}` : null;
  },
};

function brokenLinks(detail: string): string | null {
  const match = /^(\d[\d,]*) broken URLs?(?: \((it|the first) (.+)\))?$/.exec(detail);
  if (!match) return null;
  const tail = brokenTail(match[2], match[3]);
  return tail === null ? null : `${counted(match[1] ?? '', 'broken link', 'broken links')}${tail}`;
}

/** A page or file past what Google reads: "2.1 MB, past the 2 MB Google reads"; an older engine's "2.1 MB". */
function pastGoogle(detail: string, what: 'page' | 'file'): string | null {
  const match = /^([\d.]+ [KMG]B)(?:, past the ([\d.]+ [KMG]B) Google reads)?$/.exec(detail);
  if (!match) return null;
  return `the ${what} is ${match[1] ?? ''}; Google reads only the first ${match[2] ?? 'part'}`;
}

/** A series' next or previous page and how it answered: `rel="next" returns 404 Not Found`. */
function pagedTarget(detail: string): string | null {
  const match = /^rel="(next|prev)" (.+)$/.exec(detail);
  const said = match ? statusPhrase(match[2] ?? '') : null;
  return said && match ? `its ${match[1] === 'next' ? 'next' : 'previous'} page ${said}` : null;
}

/**
 * A check's detail in plain words: "It goes from the main heading to a level-3 heading, skipping level 2". Null for a
 * check that doesn't write one, or a form of it this doesn't know: the engine's own text is said then.
 */
export function plainDetailWords(checkId: string, detail: string): string | null {
  const say = DETAIL_WORDS[checkId];
  const said = say ? say(detail.trim()) : null;
  return said ? sentence(said) : null;
}
