#!/usr/bin/env python3
"""Measures the browser fallback against hosts that block crawlers. Light on purpose: a few URLs a host, one at a time.

For each address it runs `si crawl` three ways and reads the crawl file:

  plain     no fallback: what plain HTTP gets
  headless  --browser-fallback --no-installed-browser: the headless browser only, which is the fallback as 2.1.0 had it
  browser   --browser-fallback: the computer's own Chrome or Edge for a check

Usage:
  python fallback-probe.py --si <path to si> [--urls 4] [--modes plain,headless,browser] [--solve] URL [URL ...]

--solve adds --solve-challenges, so a window opens on a check that needs a person and the crawl waits for you.
Prints a Markdown table. A run that blocks stops there: the crawl has nothing more to ask.
"""
import argparse
import os
import sqlite3
import subprocess
import sys
import tempfile

MODES = {
    "plain": [],
    "headless": ["--browser-fallback", "--no-installed-browser"],
    "browser": ["--browser-fallback"],
}


def crawl(si, url, extra, urls, timeout):
    database = os.path.join(tempfile.mkdtemp(prefix="si-fallback-probe-"), "probe.sicrawl")
    command = [si, "crawl", url, "--db", database, "--max-urls", str(urls), "--any-folder", "--no-resources", "--no-external",
               "--no-sitemaps", "--no-analyze", "-c", "1", "--rate", "0.5", "-q", *extra]
    try:
        subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=timeout, check=False)
    except subprocess.TimeoutExpired:
        return "timed out"
    if not os.path.exists(database):
        return "no crawl file"
    connection = sqlite3.connect(database)
    try:
        start = connection.execute(
            "SELECT r.status_code, r.block_reason, r.title FROM urls u JOIN responses r ON r.url_id = u.id ORDER BY u.id LIMIT 1").fetchone()
        pages = connection.execute(
            "SELECT COUNT(*) FROM responses WHERE status_code BETWEEN 200 AND 299 AND is_html = 1 AND block_reason IS NULL").fetchone()[0]
        blocked = connection.execute("SELECT COUNT(*) FROM responses WHERE block_reason IS NOT NULL").fetchone()[0]
        in_browser = connection.execute("SELECT COUNT(*) FROM notices WHERE code = 'browser-fallback'").fetchone()[0]
        asked = connection.execute("SELECT COUNT(*) FROM notices WHERE code = 'human-check'").fetchone()[0]
    finally:
        connection.close()
    if start is None:
        return "nothing fetched"
    status, reason, title = start
    said = f"{status}" + (f" {reason}" if reason else "")
    said += f"; {pages} page{'' if pages == 1 else 's'} read"
    if blocked:
        said += f", {blocked} blocked"
    if in_browser:
        said += ", in a browser"
    if asked:
        said += ", a person was asked"
    if title and not reason:
        said += f" (\"{title[:40]}\")"
    return said


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--si", required=True)
    parser.add_argument("--urls", type=int, default=4)
    parser.add_argument("--modes", default="plain,headless,browser")
    parser.add_argument("--solve", action="store_true")
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("urls_to_probe", nargs="+", metavar="URL")
    arguments = parser.parse_args()
    modes = [mode for mode in arguments.modes.split(",") if mode in MODES]
    print("| Address | " + " | ".join(modes) + " |")
    print("|---|" + "---|" * len(modes))
    for url in arguments.urls_to_probe:
        cells = []
        for mode in modes:
            extra = list(MODES[mode])
            if arguments.solve and mode == "browser":
                extra.append("--solve-challenges")
            cells.append(crawl(arguments.si, url, extra, arguments.urls, arguments.timeout).replace("|", "/"))
        print(f"| {url} | " + " | ".join(cells) + " |")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
