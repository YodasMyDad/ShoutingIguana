"""Checks plain-words.json against the design's template and rules (docs/v2-ui-simple.md, "Plain words").

  python check.py [--partial] [--table] [--read] [--skipped]

--partial  don't fail on catalog checks that aren't written yet
--table    print each check's measurements
--read     print every check in the order Simple shows them (severity, then catalog order), for a read-through
--skipped  print every check as the "couldn't be checked" list names it (plainLooksFor, or the title), then "Not checked"
--file X   check X instead of plain-words.json (selftest.py uses it to prove the rules fail when broken)

Reads, and never writes, the repo: the catalog for the ids, severities and subjects, the skip reason codes, the
analysis settings for the limits a token may name, and the design document for the ten checks it writes out. Those
ten must be the document's, word for word, except the changes ACCEPTED_REFERENCE_CHANGES lists. Exit code 1 when a
rule that can be tested fails.

A limit that is an analysis setting is written as a token, {limit:<setting>}, which the engine fills with the number
and its unit ("60 characters", "200 KB", "1 second"). Every length rule here measures the text with each token filled
with its setting's default.
"""
import io
import json
import re
import sys
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

HERE = Path(__file__).resolve().parent
REPO = Path(r"C:\Projects\ShoutingIguana\.claude\worktrees\ui-step-3")
CATALOG = REPO / "engine/src/ShoutingIguana.Engine/Analysis/IssueCatalog.cs"
CATEGORIES = REPO / "engine/src/ShoutingIguana.Engine/Analysis/Models/IssueCategory.cs"
REASONS = REPO / "engine/src/ShoutingIguana.Engine/Analysis/SkippedCheckReasons.cs"
DESIGN = REPO / "docs/v2-ui-simple.md"
SETTINGS = REPO / "engine/src/ShoutingIguana.Engine/Analysis/Models/AnalysisSettings.cs"

PARTIAL = "--partial" in sys.argv
TABLE = "--table" in sys.argv
READ = "--read" in sys.argv
SKIPPED = "--skipped" in sys.argv

# Where a reference check's text is no longer the design document's, by the orchestrator's decision after the review
# (2026-11-11): (check, field) -> (the document's text, the text here). Four good-news lines that were untrue beside
# another check's failure, and the "when it is fine" sentence reference 4's Notice lacked. The document is to be
# brought into line; once it is, the text equals the document's and the entry is no longer needed.
ACCEPTED_REFERENCE_CHANGES = {
    ("title-duplicate", "plainPassed"): (
        "Every page has its own title",
        "No two pages share a title"),
    ("broken-internal-links", "plainPassed"): (
        "Every link on the site leads to a working page",
        "Every link between your pages leads to a working page"),
    ("empty-anchor-text", "plainPassed"): (
        "Every link has words to read",
        "Every link between your pages has words to read"),
    ("internal-client-error", "plainPassed"): (
        "Every page and file on the site can be found",
        'No page or file answers with a "not found" error'),
    ("empty-anchor-text", "plainWhat"): (
        "Some links on your pages have no words: usually an image or icon, such as a logo, with no description. "
        'People who use a screen reader hear only "link", and search engines learn nothing about the page it leads to. '
        "A logo or icon in the page design is the usual cause, so one fix often clears every page.",
        "Some links on your pages have no words: usually an image or icon, such as a logo, with no description. "
        'People who use a screen reader hear only "link", and search engines learn nothing about the page it leads to. '
        "A logo or icon in the page design is the usual cause, so one fix often clears every page. "
        "Pages are still found either way, so it can wait."),
}

TOKEN = re.compile(r"\{limit:([A-Za-z]+)\}")
# The unit the engine gives a setting, by how its name ends.
UNITS = {"Length": "characters", "WordCount": "words", "Bytes": "bytes", "Ms": "ms", "Bits": "bits"}

# The check the design adds, which the catalog doesn't hold yet (docs/v2-ui-simple.md, "What the engine needs").
ADDED = {"site-hidden-from-search": {"category": "indexability", "severity": "Error", "subject": "Url", "grouping": "None",
                                     "title": "(new) Site hidden from search"}}

TITLE_MAX, PASSED_MAX, LOOKS_FOR_MAX, FIRST_SENTENCE_MAX = 60, 60, 60, 120
WHAT_WORDS_MAX, STEP_WORDS_MAX, SENTENCE_WORDS_MAX = 70, 25, 30
FIXERS = {"you", "developer", "either"}

# "Never bare" (the rules). Each may appear only after it has been given once in double quotes, in the same check.
BANNED = {
    "canonical": r"\bcanonical\w*", "indexable": r"\b(?:non-)?indexable\b", "crawl": r"\bcrawl(?:s|ed|ing|er|ers)?\b",
    "status code": r"\bstatus codes?\b", "3xx": r"\b[345]xx\b", "redirect": r"\bredirect(?:s|ed|ing|ion|ions)?\b",
    "noindex": r"\bnoindex\b", "nofollow": r"\bnofollow\b", "robots.txt": r"robots\.txt", "meta": r"\bmeta\b",
    "tag": r"\btags?\b", "attribute": r"\battributes?\b", "header": r"\bheaders?\b", "anchor text": r"\banchor text\b",
    "hreflang": r"\bhreflang\b", "schema": r"\bschema\b", "JSON-LD": r"\bJSON-LD\b", "TTFB": r"\bTTFB\b",
    "DOM": r"(?-i:\bDOM\b)", "render": r"\brender(?:s|ed|ing)?\b", "directive": r"\bdirectives?\b",
    "link equity": r"\blink equity\b", "crawl budget": r"\bcrawl budget\b",
}

# Never at all.
FORBIDDEN = {
    "investigate": r"\binvestigat\w+", "look into": r"\blook(?:s|ed|ing)? into\b", "we": r"(?-i:\b(?:we|We|our|Our|us)\b)",
    "URL": r"\bURLs?\b", "e.g.": r"\b(?:e\.g\.|i\.e\.|etc\b)", "smart quotes": r"[\u2018\u2019\u201c\u201d]",
    "double space": r"  ", "issue (a Simple 'problem')": r"\bissues?\b", "audit": r"\baudit(?:s|ed)?\b",
}

# Not in the rules' list, but words a shop owner would have to look up: the same test, as warnings to read.
JARGON = {
    "HTML": r"(?-i:\bHTML\b)", "CSS": r"(?-i:\bCSS\b)", "CMS": r"(?-i:\bCMS\b)", "CDN": r"(?-i:\bCDN\b)",
    "template": r"\btemplates?\b", "markup": r"\bmarkup\b", "snippet": r"\bsnippets?\b", "index": r"\bindex(?:ed|ing|es)?\b",
    "viewport": r"\bviewport\b", "DNS": r"(?-i:\bDNS\b)", "TLS/SSL": r"(?-i:\b(?:TLS|SSL)\b)", "XML": r"(?-i:\bXML\b)",
    "H1": r"(?-i:\bH1s?\b)", "alt text": r"\balt text\b", "Open Graph": r"\bOpen Graph\b", "structured data": r"\bstructured data\b",
    "subdomain": r"\bsubdomains?\b", "domain": r"\bdomains?\b", "origin": r"\borigins?\b", "parameter": r"\bparameters?\b",
    "protocol": r"\bprotocols?\b", "user agent": r"\buser agent\b", "bot": r"\bbots?\b", "cache": r"\bcach(?:e|es|ed|ing)\b",
    "JavaScript": r"\bJavaScript\b", "rich result": r"\brich results?\b", "host": r"\bhosts?\b",
    "server-side": r"\bserver-side\b", "encoding": r"\bencoding\b", "syntax": r"\bsyntax\b", "aria-label": r"\baria-label\b",
    "id": r"(?-i:\bid\b)", "hop": r"\bhops?\b", "payload": r"\bpayload\b", "lazy": r"\blazy\b", "srcset": r"\bsrcset\b",
}

US_SPELLINGS = r"\b(?:optimi[zs]ation|optimiz\w+|organiz\w+|recogniz\w+|analyz\w+|color\w*|center\w*|behavior\w*|favorite\w*|" \
               r"catalog\b|canceled|labeled|traveled|license[sd]?\b(?= (?:is|are|was))|gray\b|program\b(?! ))"

STEP_STARTS = {
    "Open", "Send", "Ask", "Check", "Look", "Decide", "Point", "Find", "Give", "Resize", "Save", "Write", "Skip", "Put", "Change",
    "Remove", "Click", "Leave", "Add", "Move", "Cut", "Keep", "Trim", "Say", "Select", "See", "Load", "Make", "Choose", "Turn",
    "Try", "Use", "Tick", "Set", "Search", "Test", "Replace", "Swap", "Shorten", "Start", "Read", "Compare", "Type", "Go",
    "Update", "Delete", "Fix", "Merge", "Rename", "Copy", "Paste", "Tell", "Wait", "Watch", "Pick", "Mark", "Switch", "Let",
    "Link", "Forward", "Upload", "Name", "Restore", "Treat", "Share", "Stop", "Get", "Show", "Press", "Combine", "Include", "List",
    "Correct", "Rewrite", "Describe", "Count", "Visit", "Follow", "Take", "Enter", "Repeat", "Bring", "Confirm", "Expand", "Run",
    "Tidy", "Plan", "Spread", "Lower", "Raise", "Drop", "Close", "Join", "Do", "Hand", "Work", "Scroll", "Hold", "Sort", "Publish",
    "Note", "Build", "Fill", "Play", "Zoom", "Rotate", "View", "Sign", "Email", "Shrink", "Pull", "Clear", "Spot",
    "If",
}
# Words a sentence-case title may capitalise after its first.
PROPER = {"Google", "HTTPS", "Shouting", "Iguana", "I"}

FINE_MARKERS = r"nothing to do|there's nothing|is fine|are fine|it's fine|fine as|fine to|no harm|harmless|leave (?:it|them|the)|" \
               r"only if|only when|only for|only matters|matters only|skip |optional|no need|doesn't need|don't need|needs nothing|" \
               r"need nothing|can stay|can wait|is right for|are right for|that's right|does little harm|do little harm|rarely matters|unless"

errors, warnings = [], []


def error(where, message):
    errors.append(f"{where}: {message}")


def warn(where, message):
    warnings.append(f"{where}: {message}")


def words(text):
    return [token for token in text.split() if re.search(r"[A-Za-z0-9]", token)]


def syllables(word):
    word = re.sub(r"[^a-z]", "", word.lower())
    if not word:
        return 1
    count = len(re.findall(r"[aeiouy]+", word))
    if word.endswith("e") and not word.endswith(("le", "ee")) and count > 1:
        count -= 1
    return max(count, 1)


def grade(sentences):
    """Flesch-Kincaid grade of a list of sentences: rough, for comparing with the ten references."""
    tokens = [token for sentence in sentences for token in words(sentence)]
    if not tokens:
        return 0.0
    return 0.39 * len(tokens) / len(sentences) + 11.8 * sum(syllables(token) for token in tokens) / len(tokens) - 15.59


def quoted_spans(text):
    return [(match.start(), match.end()) for match in re.finditer(r'"[^"]*"', text)]


def term_order(where, texts, table, report):
    """Each term's first use, over the texts in the order a reader meets them, must be inside double quotes."""
    for name, pattern in table.items():
        for index, text in enumerate(texts):
            match = re.search(pattern, text, re.IGNORECASE)
            if not match:
                continue
            if not any(start <= match.start() < end for start, end in quoted_spans(text)):
                report(where, f'"{name}" is used bare before it is given in quotes (text {index + 1}): "{text}"')
            break


def never(where, text):
    for name, pattern in FORBIDDEN.items():
        if re.search(pattern, text, re.IGNORECASE if not pattern.startswith("(?-i") else 0):
            error(where, f'holds "{name}": "{text}"')
    if re.search(US_SPELLINGS, text, re.IGNORECASE):
        error(where, f'US spelling: "{text}"')
    if text != text.strip() or "\n" in text or "\t" in text:
        error(where, f'stray whitespace: "{text}"')


def no_terms(where, text, quoted_ok=False):
    for name, pattern in BANNED.items():
        match = re.search(pattern, text, re.IGNORECASE)
        if match and not (quoted_ok and any(start <= match.start() < end for start, end in quoted_spans(text))):
            error(where, f'holds the term "{name}": "{text}"')


def one_sentence(where, text, ends=".?"):
    if not text or text[-1] not in ends:
        error(where, f'doesn\'t end as a sentence: "{text}"')
    if re.search(r'[.!?]["\')]*\s+["\'(]*[A-Z]', text):
        error(where, f'reads as more than one sentence: "{text}"')
    if len(words(text)) > SENTENCE_WORDS_MAX:
        error(where, f'{len(words(text))} words, over {SENTENCE_WORDS_MAX}: "{text}"')


def sentence_case(where, text):
    if not text[:1].isupper():
        error(where, f'doesn\'t start with a capital: "{text}"')
    for token in re.findall(r"[A-Za-z][A-Za-z']*", text)[1:]:
        if token[0].isupper() and token not in PROPER and not token.isupper():
            error(where, f'isn\'t sentence case ("{token}"): "{text}"')
    if text.endswith("."):
        error(where, f'ends with a full stop: "{text}"')


def read_settings():
    """Each analysis setting a token may name, in camelCase, with its default as the engine would print it."""
    settings = {}
    for name, default in re.findall(r"public (?:int|long) (\w+) \{ get; init; \} = ([^;]+);",
                                    SETTINGS.read_text(encoding="utf-8")):
        if not re.fullmatch(r"[\d_ *]+", default):
            raise ValueError(f"{name}'s default isn't a number this script can read: {default}")
        value = 1
        for factor in default.replace("_", "").split("*"):
            value *= int(factor)
        unit = next((unit for ending, unit in UNITS.items() if name.endswith(ending)), None)
        if unit is None:
            raise ValueError(f"no unit is known for the setting {name}")
        settings[name[0].lower() + name[1:]] = with_unit(value, unit)
    return settings


def with_unit(value, unit):
    """A limit as the engine fills a token: "60 characters", "200 KB", "1 MB", "1 second", "1.5 seconds"."""
    number = lambda amount: f"{amount:,.1f}".rstrip("0").rstrip(".")
    if unit == "bytes":
        return f"{number(value / 1024 / 1024)} MB" if value >= 1024 * 1024 else f"{number(value / 1024)} KB"
    if unit == "ms":
        return f"{number(value / 1000)} {'second' if value == 1000 else 'seconds'}"
    return f"{number(value)} {unit[:-1] if value == 1 else unit}"


def filled(text, settings):
    """The text as a reader sees it, each token filled with its setting's default. An unknown token is left."""
    return TOKEN.sub(lambda match: settings.get(match[1], match[0]), text)


def check_tokens(where, text, settings):
    for name in TOKEN.findall(text):
        if name not in settings:
            error(where, f'the token {{limit:{name}}} names no analysis setting ({", ".join(settings)}): "{text}"')
    if re.search(r"[{}]", TOKEN.sub("", text)):
        error(where, f'holds a brace that isn\'t part of a {{limit:<setting>}} token: "{text}"')
    for name, default in settings.items():
        if re.search(rf"(?<![\d.,]){re.escape(default)}\b", text):
            error(where, f'holds "{default}", the default of the setting {name}: write it as {{limit:{name}}}: "{text}"')


def strings_of(value):
    """Every string in a check, a category or a skip reason."""
    if isinstance(value, str):
        yield value
    elif isinstance(value, list):
        for item in value:
            yield from strings_of(item)
    elif isinstance(value, dict):
        for item in value.values():
            yield from strings_of(item)


def fill_all(value, settings):
    if isinstance(value, str):
        return filled(value, settings)
    if isinstance(value, list):
        return [fill_all(item, settings) for item in value]
    if isinstance(value, dict):
        return {key: fill_all(item, settings) for key, item in value.items()}
    return value


def read_catalog():
    source = CATALOG.read_text(encoding="utf-8")
    checks = {}
    pattern = re.compile(
        r'new\("(?P<id>[a-z0-9-]+)", IssueCategory\.(?P<category>\w+), IssueSeverity\.(?P<severity>\w+),\s*"(?P<title>(?:[^"\\]|\\.)*)"'
        r'(?P<rest>.*?)Grouping = IssueGrouping\.(?P<grouping>\w+)', re.S)
    for match in pattern.finditer(source):
        subject = re.search(r"Subject = IssueSubject\.(\w+)", match["rest"])
        category = match["category"][0].lower() + match["category"][1:]
        checks[match["id"]] = {"category": category, "severity": match["severity"], "title": match["title"],
                               "subject": subject[1] if subject else "?", "grouping": match["grouping"]}
        if match["id"] == "robots-txt-missing":
            # The added check is an Indexability one, so it sits at the end of that category.
            checks.update(ADDED)
    categories = [name[0].lower() + name[1:] for name in re.findall(r"^\s+(\w+),?\s*$", CATEGORIES.read_text(encoding="utf-8"), re.M)]
    reasons = re.findall(r'public const string \w+ = "([a-z-]+)";', REASONS.read_text(encoding="utf-8"))
    return checks, categories, reasons


def read_references():
    """The ten checks the design document writes out, and the skip reason it writes with them."""
    source = DESIGN.read_text(encoding="utf-8")
    section = source[source.index("### Ten checks, written out"):source.index("## The report")]
    references, skips = {}, {}
    for block in re.split(r"\n(?=\*\*\d+\. `)", section)[1:]:
        check_id = re.match(r"\*\*\d+\. `([a-z0-9-]+)`", block)[1]
        fields = {}
        for item in re.split(r"\n- ", block)[1:]:
            name, _, value = item.partition(": ")
            fields[name] = " ".join(line.strip() for line in value.strip().splitlines())
        reference = {"fixer": re.search(r"Fixer: (\w+)\.", block)[1], "plainTitle": fields["Title"], "what": fields["What"],
                     "plainFix": [step.strip() for step in re.split(r"\(\d\) ", fields["Fix"]) if step.strip()],
                     "plainPassed": fields["Passed"]}
        if "One fix" in fields:
            reference["plainOneFix"] = fields["One fix"]
        if "Looks for" in fields:
            reference["plainLooksFor"] = fields["Looks for"]
        references[check_id] = reference
        for name, value in fields.items():
            skipped = re.match(r"When skipped \(`([a-z-]+)`\)", name)
            if skipped:
                skips[skipped[1]] = value
    return references, skips


def same_as_document(check_id, field, written, as_read, document):
    """
    Whether a reference check's text is the design document's: word for word as written (the document may come to hold
    a token) or as read with its tokens filled, or the one change ACCEPTED_REFERENCE_CHANGES allows for it.
    """
    accepted = ACCEPTED_REFERENCE_CHANGES.get((check_id, field))
    if accepted is not None:
        # The change must be made here, whether or not the document has been brought into line yet.
        return as_read == accepted[1] and document in accepted
    return document in (written, as_read)


def check_one(check_id, raw, info, reference, settings):
    severity = info["severity"]
    allowed = {"plainTitle", "plainLooksFor", "plainWhat", "plainFix", "fixer", "plainPassed", "plainOneFix"}
    for field in allowed - {"plainOneFix", "plainLooksFor"}:
        if field not in raw:
            error(check_id, f"missing {field}")
            return None
    for field in set(raw) - allowed:
        error(check_id, f"unknown field {field}")

    # Tokens are judged as written; everything else as a reader sees the text, each token filled with its default.
    for text in strings_of(raw):
        check_tokens(check_id, text, settings)
    check = fill_all(raw, settings)

    title, what, fix, passed = check["plainTitle"], check["plainWhat"], check["plainFix"], check["plainPassed"]
    one_fix, looks_for = check.get("plainOneFix"), check.get("plainLooksFor")

    if len(title) > TITLE_MAX:
        error(check_id, f'plainTitle is {len(title)} characters, over {TITLE_MAX}: "{title}"')
    sentence_case(f"{check_id} plainTitle", title)
    if re.search(r"\d", title):
        error(check_id, f'plainTitle holds a number: "{title}"')
    no_terms(f"{check_id} plainTitle", title)

    if looks_for is not None:
        # A noun phrase for what the check looks for, shown where the title, a sentence, would read as a finding.
        where = f"{check_id} plainLooksFor"
        if len(looks_for) > LOOKS_FOR_MAX:
            error(where, f'{len(looks_for)} characters, over {LOOKS_FOR_MAX}: "{looks_for}"')
        sentence_case(where, looks_for)
        no_terms(where, looks_for)
        never(where, looks_for)
        if re.search(r"[.!?:]", looks_for) or re.search(r"\d", looks_for):
            error(where, f'isn\'t a plain noun phrase (punctuation or a number): "{looks_for}"')
        first = re.match(r"[A-Za-z']+", looks_for)
        if first and first[0] in STEP_STARTS | {"Your", "No", "There", "This", "These", "It"}:
            error(where, f'starts like a sentence or an instruction ("{first[0]}"), not a noun phrase: "{looks_for}"')
        if looks_for == title:
            error(where, "is the title: leave the field out")

    if not isinstance(what, list) or not 2 <= len(what) <= 4:
        error(check_id, f"plainWhat has {len(what)} sentences, not 2 to 4")
    for index, sentence in enumerate(what):
        one_sentence(f"{check_id} plainWhat[{index + 1}]", sentence)
    if len(what[0]) > FIRST_SENTENCE_MAX:
        error(check_id, f'sentence 1 is {len(what[0])} characters, over {FIRST_SENTENCE_MAX}: "{what[0]}"')
    total = sum(len(words(sentence)) for sentence in what)
    if total > WHAT_WORDS_MAX:
        error(check_id, f"plainWhat is {total} words, over {WHAT_WORDS_MAX}")

    if not isinstance(fix, list) or not 1 <= len(fix) <= 4:
        error(check_id, f"plainFix has {len(fix)} steps, not 1 to 4")
    for index, step in enumerate(fix):
        where = f"{check_id} plainFix[{index + 1}]"
        one_sentence(where, step, ends=".")
        if len(words(step)) > STEP_WORDS_MAX:
            error(where, f'{len(words(step))} words, over {STEP_WORDS_MAX}: "{step}"')
        first = re.match(r"[A-Za-z']+", step)
        if not first or first[0] not in STEP_STARTS:
            error(where, f'doesn\'t start with a verb (or "If"): "{step}"')

    if check["fixer"] not in FIXERS:
        error(check_id, f'fixer is "{check["fixer"]}"')

    if len(passed) > PASSED_MAX:
        error(check_id, f'plainPassed is {len(passed)} characters, over {PASSED_MAX}: "{passed}"')
    sentence_case(f"{check_id} plainPassed", passed)
    no_terms(f"{check_id} plainPassed", passed)

    if one_fix is not None:
        one_sentence(f"{check_id} plainOneFix", one_fix, ends=".")
        if info["grouping"] not in ("RelatedUrl", "PageDetail"):
            warn(check_id, f'has a plainOneFix, but its grouping ({info["grouping"]}) never marks a likely template')

    texts = [*what, *fix, *([one_fix] if one_fix else [])]
    for text in [title, passed, *texts]:
        never(check_id, text)
    term_order(check_id, texts, BANNED, error)
    if reference is None:
        term_order(check_id, texts, JARGON, warn)

    if severity == "Notice" and not re.search(FINE_MARKERS, " ".join(texts), re.IGNORECASE):
        (warn if reference else error)(check_id, "a Notice that doesn't say when it is fine")
    if info["subject"] == "Url" and re.match(r"(?:These|Some|Some of your) pages\b", what[0]):
        warn(check_id, f'its subject is Url (addresses, not only pages), but sentence 1 says: "{what[0]}"')

    if reference is not None:
        pairs = [(field, raw[field], check[field], reference[field]) for field in ("plainTitle", "plainFix", "plainPassed", "fixer")]
        pairs.append(("plainWhat", " ".join(raw["plainWhat"]), " ".join(what), reference["what"]))
        pairs.append(("plainOneFix", raw.get("plainOneFix"), one_fix, reference.get("plainOneFix")))
        if "plainLooksFor" in reference:
            pairs.append(("plainLooksFor", raw.get("plainLooksFor"), looks_for, reference["plainLooksFor"]))
        for field, written, as_read, document in pairs:
            if not same_as_document(check_id, field, written, as_read, document):
                accepted = ACCEPTED_REFERENCE_CHANGES.get((check_id, field))
                wanted = f"\n      accepted change: {accepted[1]}" if accepted else ""
                error(check_id, f"{field} isn't the design document's, word for word:\n      doc:  {document}{wanted}\n      json: {as_read}")

    lengths = [len(words(sentence)) for sentence in what]
    return {"title": len(title), "first": len(what[0]), "what": total, "longest": max(lengths), "sentences": lengths,
            "steps": [len(words(step)) for step in fix], "passed": len(passed), "grade": grade(texts),
            "tokens": sum(len(TOKEN.findall(text)) for text in strings_of(raw))}


def main():
    catalog, categories, reasons = read_catalog()
    references, reference_skips = read_references()
    settings = read_settings()
    path = Path(sys.argv[sys.argv.index("--file") + 1]) if "--file" in sys.argv else HERE / "plain-words.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exception:
        print(f"FAIL: {path.name} isn't valid JSON: {exception}")
        return 1

    for key in ("checks", "categories", "skipReasons"):
        if key not in data:
            error("file", f"missing {key}")
    for key in set(data) - {"checks", "categories", "skipReasons"}:
        error("file", f"unknown key {key}")
    checks, plain_categories, skips = data.get("checks", {}), data.get("categories", {}), data.get("skipReasons", {})

    if len(catalog) != 87 or len(categories) != 17 or len(references) != 10:
        error("catalog", f"read {len(catalog)} checks, {len(categories)} categories, {len(references)} references: expected 87, 17, 10")

    for check_id in checks:
        if check_id not in catalog:
            error(check_id, "isn't a check in the catalog")
    missing = [check_id for check_id in catalog if check_id not in checks]
    if missing and not PARTIAL:
        error("checks", f"{len(missing)} catalog checks aren't written: {', '.join(missing)}")
    if list(checks) != [check_id for check_id in catalog if check_id in checks]:
        warn("checks", "aren't in catalog order")

    measured = {}
    for check_id, check in checks.items():
        if check_id in catalog:
            result = check_one(check_id, check, catalog[check_id], references.get(check_id), settings)
            if result:
                measured[check_id] = result

    # Titles and good-news lines must each be one of a kind: two checks with one title can't be told apart. So must
    # the names the "couldn't be checked" list uses: plainLooksFor where a check has one, its title where it hasn't.
    for field in ("plainTitle", "plainPassed"):
        seen = {}
        for check_id, check in checks.items():
            if check.get(field) in seen:
                error(check_id, f'{field} is the same as {seen[check[field]]}\'s: "{check[field]}"')
            seen[check.get(field)] = check_id
    seen = {}
    for check_id, check in checks.items():
        name = check.get("plainLooksFor", check.get("plainTitle"))
        if name in seen:
            error(check_id, f'is named as {seen[name]} is in the "couldn\'t be checked" list: "{name}"')
        seen[name] = check_id

    # Categories and skip reasons name no limit, so they hold no token; a brace or a default's number there is a slip.
    for where, entry in [*((f"category {key}", value) for key, value in plain_categories.items()),
                         *((f"skip {key}", value) for key, value in skips.items())]:
        for text in strings_of(entry):
            check_tokens(where, text, settings)
            if TOKEN.search(text):
                error(where, f'holds a limit token, which only a check\'s texts may: "{text}"')

    for category in categories:
        if category not in plain_categories:
            error("categories", f"missing {category}")
        else:
            title = plain_categories[category].get("plainTitle", "")
            sentence_case(f"category {category}", title)
            no_terms(f"category {category}", title)
            never(f"category {category}", title)
            if set(plain_categories[category]) != {"plainTitle"}:
                error(f"category {category}", "must hold plainTitle and nothing else")
    for category in set(plain_categories) - set(categories):
        error("categories", f"{category} isn't a category")
    for category, fixed in {"responseCodes": "Pages that load", "canonicals": "Duplicate addresses", "hreflang": "Languages"}.items():
        if plain_categories.get(category, {}).get("plainTitle") != fixed:
            error(f"category {category}", f'the design document says "{fixed}"')
    titles = [entry.get("plainTitle") for entry in plain_categories.values()]
    if len(set(titles)) != len(titles):
        error("categories", "two categories share a title")

    for reason in reasons:
        if reason not in skips:
            error("skipReasons", f"missing {reason}")
            continue
        entry = skips[reason]
        if set(entry) != {"plainText", "plainAction"}:
            error(f"skip {reason}", "must hold plainText and plainAction and nothing else")
            continue
        for field in ("plainText", "plainAction"):
            one_sentence(f"skip {reason} {field}", entry[field], ends=".")
            never(f"skip {reason}", entry[field])
        term_order(f"skip {reason}", [entry["plainText"], entry["plainAction"]], BANNED, error)
        term_order(f"skip {reason}", [entry["plainText"], entry["plainAction"]], JARGON, warn)
    for reason in set(skips) - set(reasons):
        error("skipReasons", f"{reason} isn't a reason code")
    for reason, text in reference_skips.items():
        entry = skips.get(reason, {})
        if f'{entry.get("plainText")} {entry.get("plainAction")}' != text:
            error(f"skip {reason}", f"isn't the design document's, word for word: {text}")

    if TABLE:
        print(f'{"check":38} {"sev":7} {"subj":4} title first what  sentences     steps          passed grade')
        for check_id, row in measured.items():
            info = catalog[check_id]
            print(f'{check_id:38} {info["severity"]:7} {info["subject"]:4} {row["title"]:5} {row["first"]:5} {row["what"]:4}  '
                  f'{str(row["sentences"]):13} {str(row["steps"]):14} {row["passed"]:6} {row["grade"]:5.1f}'
                  f'{"  (reference)" if check_id in references else ""}')

    if READ:
        order = {"Error": 0, "Warning": 1, "Notice": 2}
        word = {"Error": "SERIOUS", "Warning": "WORTH FIXING", "Notice": "WORTH A LOOK"}
        for check_id in sorted(checks, key=lambda item: order[catalog[item]["severity"]]):
            check, info = fill_all(checks[check_id], settings), catalog[check_id]
            print(f'\n[{word[info["severity"]]}] {check["plainTitle"]}   <{check_id} | {info["title"]} | {info["subject"]} | fixer: {check["fixer"]}>')
            if "plainLooksFor" in check:
                print(f'    Looks for: {check["plainLooksFor"]}')
            for sentence in check["plainWhat"]:
                print(f"    {sentence}")
            for index, step in enumerate(check["plainFix"]):
                print(f"    {index + 1}. {step}")
            if "plainOneFix" in check:
                print(f'    One fix: {check["plainOneFix"]}')
            print(f'    Passed: {check["plainPassed"]}')

    if SKIPPED:
        # Every check can be skipped (a rule that failed, a check newer than the results), so every name must read as
        # the thing looked for, not as something found.
        for check_id, check in checks.items():
            mark = "looks for" if "plainLooksFor" in check else "title    "
            print(f'{mark}  {check.get("plainLooksFor", check["plainTitle"])} · Not checked   <{check_id}>')

    read_as = [fill_all(check, settings) for check in checks.values()]
    all_sentences = [len(words(sentence)) for check in read_as for sentence in check.get("plainWhat", [])]
    all_steps = [len(words(step)) for check in read_as for step in check.get("plainFix", [])]
    reference_grades = [measured[check_id]["grade"] for check_id in references if check_id in measured]
    own_grades = {check_id: row["grade"] for check_id, row in measured.items() if check_id not in references}
    if reference_grades:
        ceiling = max(reference_grades)
        for check_id, value in own_grades.items():
            if value > ceiling + 0.5:
                warn(check_id, f"reads harder (grade {value:.1f}) than the hardest of the ten references ({ceiling:.1f})")
    average = sum(all_sentences) / len(all_sentences) if all_sentences else 0
    if average > 20.5:
        error("all", f"sentences average {average:.1f} words, over 20")

    print(f"\n{len(checks)} of {len(catalog)} checks, {len(plain_categories)} of {len(categories)} categories, "
          f"{len(skips)} of {len(reasons)} skip reasons; {sum(1 for check in checks.values() if 'plainOneFix' in check)} with plainOneFix, "
          f"{sum(1 for check in checks.values() if 'plainLooksFor' in check)} with plainLooksFor")
    used = sorted({name for check in checks.values() for text in strings_of(check) for name in TOKEN.findall(text)})
    places = sum(row["tokens"] for row in measured.values())
    print(f"limit tokens: {places} in {sum(1 for row in measured.values() if row['tokens'])} checks, naming {len(used)} of "
          f"{len(settings)} settings: {', '.join(f'{name} ({settings.get(name, chr(63))})' for name in used)}")
    behind = [f"{check_id} {field}" for (check_id, field), (document, _) in ACCEPTED_REFERENCE_CHANGES.items()
              if check_id in references and (references[check_id]["what"] if field == "plainWhat" else references[check_id].get(field)) == document]
    print(f"references: {len(references)} compared with the design document; {len(ACCEPTED_REFERENCE_CHANGES)} accepted changes, "
          f"{len(behind)} of them not in the document yet{': ' + ', '.join(behind) if behind else ''}")
    if all_sentences:
        print(f"plainWhat sentences: {len(all_sentences)}, average {average:.1f} words, longest {max(all_sentences)}; "
              f"steps: {len(all_steps)}, average {sum(all_steps) / len(all_steps):.1f} words, longest {max(all_steps)}")
    if reference_grades and own_grades:
        print(f"reading grade (Flesch-Kincaid, rough): references {min(reference_grades):.1f} to {max(reference_grades):.1f}, "
              f"mean {sum(reference_grades) / len(reference_grades):.1f}; the rest {min(own_grades.values()):.1f} to "
              f"{max(own_grades.values()):.1f}, mean {sum(own_grades.values()) / len(own_grades):.1f}")
    for message in warnings:
        print(f"warn  {message}")
    for message in errors:
        print(f"ERROR {message}")
    print(f"\n{'FAIL' if errors else 'PASS'}: {len(errors)} errors, {len(warnings)} warnings")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
