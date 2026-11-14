//! The words the shell shows outside the window: the tray's tooltip and menu, the hide-to-tray notice, and system
//! notifications. UK English, in the page's mode: Simple's words in Simple (docs/v2-ui-simple.md, "Simple's words"),
//! Advanced's in Advanced.

use crate::live::model::{LiveKinds, LiveStatus, TrayEntry};
use crate::mode::Mode;

pub const APP_NAME: &str = "Shouting Iguana";

/// Windows shows 127 UTF-16 units of a tray tooltip (128 with the terminator).
pub const TOOLTIP_MAX: usize = 127;

/// "1,204".
pub fn thousands(value: u64) -> String {
    let digits = value.to_string();
    let mut grouped = String::with_capacity(digits.len() + digits.len() / 3);
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            grouped.push(',');
        }
        grouped.push(digit);
    }
    grouped
}

/// "1 URL", "4,521 URLs".
pub fn urls(count: u64) -> String {
    if count == 1 {
        "1 URL".to_owned()
    } else {
        format!("{} URLs", thousands(count))
    }
}

/// "1 address", "4,521 addresses": Simple's word for a URL.
pub fn addresses(count: u64) -> String {
    if count == 1 {
        "1 address".to_owned()
    } else {
        format!("{} addresses", thousands(count))
    }
}

/// "18 checks found issues", "1 check found issues", "no checks found issues".
pub fn checks_found(count: u64) -> String {
    match count {
        0 => "no checks found issues".to_owned(),
        1 => "1 check found issues".to_owned(),
        _ => format!("{} checks found issues", thousands(count)),
    }
}

pub fn tooltip(entries: &[TrayEntry], mode: Mode) -> String {
    if mode == Mode::Simple {
        return simple_tooltip(entries);
    }
    match entries {
        [] => APP_NAME.to_owned(),
        [entry] => fit(&entry.name, |name| match entry.status {
            LiveStatus::Running => format!("{APP_NAME}: {name}, {} crawled", urls(entry.fetched)),
            LiveStatus::Paused => format!("{APP_NAME}: {name} paused, {} crawled", urls(entry.fetched)),
            LiveStatus::Stopping => format!("{APP_NAME}: {name} stopping, {} crawled", urls(entry.fetched)),
            LiveStatus::Analyzing => format!("{APP_NAME}: analysing {name}"),
        }),
        entries => {
            let crawling: Vec<&TrayEntry> = entries.iter().filter(|entry| entry.status.is_crawling()).collect();
            if crawling.is_empty() {
                return format!("{APP_NAME}: analysing {} crawls", entries.len());
            }
            let fetched: u64 = crawling.iter().map(|entry| entry.fetched).sum();
            let paused = if crawling.iter().all(|entry| entry.status == LiveStatus::Paused) { " paused" } else { "" };
            format!("{APP_NAME}: {} crawls{paused}, {} crawled", entries.len(), urls(fetched))
        }
    }
}

/// The same in Simple's words: a site is checked, never crawled, and its results worked out, never analysed.
fn simple_tooltip(entries: &[TrayEntry]) -> String {
    match entries {
        [] => APP_NAME.to_owned(),
        [entry] => fit(&entry.name, |name| match entry.status {
            LiveStatus::Running => format!("{APP_NAME}: checking {name}, {} checked", addresses(entry.fetched)),
            LiveStatus::Paused => {
                format!("{APP_NAME}: the check of {name} is paused, {} checked", addresses(entry.fetched))
            }
            LiveStatus::Stopping => {
                format!("{APP_NAME}: stopping the check of {name}, {} checked", addresses(entry.fetched))
            }
            LiveStatus::Analyzing => format!("{APP_NAME}: working out the results for {name}"),
        }),
        entries => {
            let checking: Vec<&TrayEntry> = entries.iter().filter(|entry| entry.status.is_crawling()).collect();
            if checking.is_empty() {
                return format!("{APP_NAME}: working out the results for {} sites", entries.len());
            }
            let fetched: u64 = checking.iter().map(|entry| entry.fetched).sum();
            if checking.iter().all(|entry| entry.status == LiveStatus::Paused) {
                return format!("{APP_NAME}: {} checks paused, {} checked", checking.len(), addresses(fetched));
            }
            format!("{APP_NAME}: checking {} sites, {} checked", entries.len(), addresses(fetched))
        }
    }
}

/// The text with `name` in it, the name shortened with an ellipsis if the whole would be too long for a tooltip.
fn fit(name: &str, text: impl Fn(&str) -> String) -> String {
    let full = text(name);
    let length = utf16_len(&full);
    if length <= TOOLTIP_MAX {
        return full;
    }
    let keep = utf16_len(name).saturating_sub(length - TOOLTIP_MAX + 1);
    let mut short = String::new();
    for character in name.chars() {
        if utf16_len(&short) + character.len_utf16() > keep {
            break;
        }
        short.push(character);
    }
    short.push('…');
    text(&short)
}

fn utf16_len(text: &str) -> usize {
    text.encode_utf16().count()
}

/// A crawl's entry in the tray menu.
pub fn menu_label(entry: &TrayEntry, mode: Mode) -> String {
    let name = menu_text(&entry.name);
    match (entry.status, mode) {
        (LiveStatus::Running, _) => name,
        (LiveStatus::Paused, _) => format!("{name} (paused)"),
        (LiveStatus::Stopping, _) => format!("{name} (stopping)"),
        (LiveStatus::Analyzing, Mode::Advanced) => format!("{name} (analysing)"),
        (LiveStatus::Analyzing, Mode::Simple) => format!("{name} (working out the results)"),
    }
}

/// The notice the first time the window hides to the tray, (title, body): what carries on, in the mode's words.
pub fn tray_notice(kinds: LiveKinds, mode: Mode) -> (&'static str, &'static str) {
    match (kinds.crawling, mode) {
        (true, Mode::Advanced) => ("Still crawling", "Shouting Iguana keeps crawling in the tray. Quit from its menu."),
        (false, Mode::Advanced) => {
            ("Still analysing", "Shouting Iguana keeps analysing in the tray. Quit from its menu.")
        }
        (true, Mode::Simple) => ("Still checking", "Shouting Iguana keeps checking in the tray. Quit from its menu."),
        (false, Mode::Simple) => (
            "Still working out the results",
            "Shouting Iguana keeps working out the results in the tray. Quit from its menu.",
        ),
    }
}

/// A tooltip's words, its numbers aside ("1,204" and "1,300" alike are "#"): a change of words is a change of state,
/// shown and logged at once, where a change of numbers waits for the next redraw.
pub fn words_of(text: &str) -> String {
    let mut words = String::with_capacity(text.len());
    let mut characters = text.chars().peekable();
    while let Some(character) = characters.next() {
        if !character.is_ascii_digit() {
            words.push(character);
            continue;
        }
        // The number, its thousands' commas with it.
        while let Some(&next) = characters.peek() {
            let mut ahead = characters.clone();
            ahead.next();
            if next.is_ascii_digit() || (next == ',' && ahead.peek().is_some_and(char::is_ascii_digit)) {
                characters.next();
            } else {
                break;
            }
        }
        words.push('#');
    }
    words
}

/// Every label of the tray's menu, in order, as the shell's log gives them (the smoke test reads them there).
pub fn menu_labels(entries: &[TrayEntry], mode: Mode) -> Vec<String> {
    let mut labels = vec![format!("Open {APP_NAME}")];
    labels.extend(entries.iter().map(|entry| menu_label(entry, mode)));
    labels.push("Quit".to_owned());
    labels
}

/// Menus read `&` as a mnemonic marker; `&&` is a literal ampersand.
fn menu_text(text: &str) -> String {
    text.replace('&', "&&")
}

#[cfg(test)]
mod tests {
    use super::*;

    fn entry(name: &str, status: LiveStatus, fetched: u64) -> TrayEntry {
        TrayEntry { id: name.to_owned(), name: name.to_owned(), status, fetched }
    }

    /// Round 5's review (R2-11): the tooltip's state is told apart from its numbers, so a change of state is shown and
    /// logged at once, and the menu's labels are logged in the mode's words.
    #[test]
    fn a_tooltips_words_leave_its_numbers_out() {
        let checking = simple(&[entry("a.com", LiveStatus::Running, 1_204)]);
        assert_eq!(words_of(&checking), "Shouting Iguana: checking a.com, # addresses checked");
        assert_eq!(words_of(&checking), words_of(&simple(&[entry("a.com", LiveStatus::Running, 7)])));
        assert_ne!(words_of(&checking), words_of(&simple(&[entry("a.com", LiveStatus::Analyzing, 7)])));
        assert_eq!(words_of("site2.com, 1,2 and 3"), "site#.com, # and #");
        assert_eq!(words_of("a, b"), "a, b");

        let labels = menu_labels(&[entry("a.com", LiveStatus::Analyzing, 0)], Mode::Simple);
        assert_eq!(labels, ["Open Shouting Iguana", "a.com (working out the results)", "Quit"]);
        assert_eq!(menu_labels(&[], Mode::Advanced), ["Open Shouting Iguana", "Quit"]);
    }

    #[test]
    fn numbers_group_in_thousands() {
        assert_eq!(thousands(0), "0");
        assert_eq!(thousands(999), "999");
        assert_eq!(thousands(1_204), "1,204");
        assert_eq!(thousands(12_345_678), "12,345,678");
        assert_eq!(urls(1), "1 URL");
        assert_eq!(urls(4_521), "4,521 URLs");
        assert_eq!(urls(0), "0 URLs");
        assert_eq!(addresses(1), "1 address");
        assert_eq!(addresses(4_521), "4,521 addresses");
        assert_eq!(checks_found(0), "no checks found issues");
        assert_eq!(checks_found(1), "1 check found issues");
        assert_eq!(checks_found(1_018), "1,018 checks found issues");
    }

    fn tooltip(entries: &[TrayEntry]) -> String {
        super::tooltip(entries, Mode::Advanced)
    }

    fn simple(entries: &[TrayEntry]) -> String {
        super::tooltip(entries, Mode::Simple)
    }

    #[test]
    fn one_crawl_is_named() {
        assert_eq!(tooltip(&[]), "Shouting Iguana");
        assert_eq!(
            tooltip(&[entry("example.com", LiveStatus::Running, 1_204)]),
            "Shouting Iguana: example.com, 1,204 URLs crawled"
        );
        assert_eq!(
            tooltip(&[entry("example.com", LiveStatus::Paused, 1)]),
            "Shouting Iguana: example.com paused, 1 URL crawled"
        );
        assert_eq!(
            tooltip(&[entry("example.com", LiveStatus::Stopping, 7)]),
            "Shouting Iguana: example.com stopping, 7 URLs crawled"
        );
        assert_eq!(
            tooltip(&[entry("example.com", LiveStatus::Analyzing, 7)]),
            "Shouting Iguana: analysing example.com"
        );
    }

    #[test]
    fn several_crawls_are_counted() {
        let entries = [entry("a.com", LiveStatus::Running, 3_000), entry("b.com", LiveStatus::Paused, 456)];
        assert_eq!(tooltip(&entries), "Shouting Iguana: 2 crawls, 3,456 URLs crawled");
        let paused = [
            entry("a.com", LiveStatus::Paused, 1),
            entry("b.com", LiveStatus::Paused, 1),
            entry("c.com", LiveStatus::Analyzing, 50),
        ];
        assert_eq!(tooltip(&paused), "Shouting Iguana: 3 crawls paused, 2 URLs crawled");
        let analysing = [entry("a.com", LiveStatus::Analyzing, 1), entry("b.com", LiveStatus::Analyzing, 1)];
        assert_eq!(tooltip(&analysing), "Shouting Iguana: analysing 2 crawls");
    }

    #[test]
    fn a_long_name_is_shortened_to_fit() {
        let name = format!("{}.example.com", "sub".repeat(60));
        let text = tooltip(&[entry(&name, LiveStatus::Running, 1_234_567)]);
        assert_eq!(utf16_len(&text), TOOLTIP_MAX);
        assert!(text.starts_with("Shouting Iguana: subsub"), "{text}");
        assert!(text.ends_with("…, 1,234,567 URLs crawled"), "{text}");

        let wide = "例".repeat(200);
        let text = tooltip(&[entry(&wide, LiveStatus::Analyzing, 0)]);
        assert!(utf16_len(&text) <= TOOLTIP_MAX);
        assert!(text.ends_with('…'));
    }

    #[test]
    fn menu_labels_carry_the_status_and_escape_ampersands() {
        let label = |entry: &TrayEntry| menu_label(entry, Mode::Advanced);
        assert_eq!(label(&entry("example.com", LiveStatus::Running, 0)), "example.com");
        assert_eq!(label(&entry("example.com", LiveStatus::Paused, 0)), "example.com (paused)");
        assert_eq!(label(&entry("a&b", LiveStatus::Analyzing, 0)), "a&&b (analysing)");
    }

    /// The audit's gap 5: the shell spoke Advanced in Simple.
    #[test]
    fn simple_says_checking_never_crawled_or_analysed() {
        assert_eq!(simple(&[]), "Shouting Iguana");
        assert_eq!(
            simple(&[entry("example.com", LiveStatus::Running, 1_204)]),
            "Shouting Iguana: checking example.com, 1,204 addresses checked"
        );
        assert_eq!(
            simple(&[entry("example.com", LiveStatus::Paused, 1)]),
            "Shouting Iguana: the check of example.com is paused, 1 address checked"
        );
        assert_eq!(
            simple(&[entry("example.com", LiveStatus::Stopping, 7)]),
            "Shouting Iguana: stopping the check of example.com, 7 addresses checked"
        );
        assert_eq!(
            simple(&[entry("example.com", LiveStatus::Analyzing, 7)]),
            "Shouting Iguana: working out the results for example.com"
        );
        let entries = [entry("a.com", LiveStatus::Running, 3_000), entry("b.com", LiveStatus::Paused, 456)];
        assert_eq!(simple(&entries), "Shouting Iguana: checking 2 sites, 3,456 addresses checked");
        let paused = [entry("a.com", LiveStatus::Paused, 1), entry("b.com", LiveStatus::Paused, 1)];
        assert_eq!(simple(&paused), "Shouting Iguana: 2 checks paused, 2 addresses checked");
        let working = [entry("a.com", LiveStatus::Analyzing, 1), entry("b.com", LiveStatus::Analyzing, 1)];
        assert_eq!(simple(&working), "Shouting Iguana: working out the results for 2 sites");
        let name = format!("{}.example.com", "sub".repeat(60));
        assert_eq!(utf16_len(&simple(&[entry(&name, LiveStatus::Paused, 1_234_567)])), TOOLTIP_MAX);
        assert_eq!(menu_label(&entry("a&b", LiveStatus::Analyzing, 0), Mode::Simple), "a&&b (working out the results)");
        assert_eq!(menu_label(&entry("a.com", LiveStatus::Paused, 0), Mode::Simple), "a.com (paused)");
    }

    #[test]
    fn the_tray_notice_says_what_carries_on_in_the_modes_words() {
        let crawling = LiveKinds { crawling: true, analysing: true };
        let analysing = LiveKinds { crawling: false, analysing: true };
        assert_eq!(tray_notice(crawling, Mode::Advanced).0, "Still crawling");
        assert_eq!(tray_notice(analysing, Mode::Advanced).0, "Still analysing");
        assert_eq!(
            tray_notice(crawling, Mode::Simple),
            ("Still checking", "Shouting Iguana keeps checking in the tray. Quit from its menu.")
        );
        assert_eq!(tray_notice(analysing, Mode::Simple).0, "Still working out the results");
        for kinds in [crawling, analysing] {
            let (title, body) = tray_notice(kinds, Mode::Simple);
            for word in ["crawl", "analys", "URL"] {
                assert!(!title.contains(word) && !body.contains(word), "{title} {body}");
            }
        }
    }
}
