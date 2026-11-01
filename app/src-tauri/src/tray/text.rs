//! The words the shell shows outside the window: the tray's tooltip and menu, and system notifications. UK English.

use crate::live::model::{LiveStatus, TrayEntry};

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

/// "18 checks found issues", "1 check found issues", "no checks found issues".
pub fn checks_found(count: u64) -> String {
    match count {
        0 => "no checks found issues".to_owned(),
        1 => "1 check found issues".to_owned(),
        _ => format!("{} checks found issues", thousands(count)),
    }
}

pub fn tooltip(entries: &[TrayEntry]) -> String {
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
pub fn menu_label(entry: &TrayEntry) -> String {
    let name = menu_text(&entry.name);
    match entry.status {
        LiveStatus::Running => name,
        LiveStatus::Paused => format!("{name} (paused)"),
        LiveStatus::Stopping => format!("{name} (stopping)"),
        LiveStatus::Analyzing => format!("{name} (analysing)"),
    }
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

    #[test]
    fn numbers_group_in_thousands() {
        assert_eq!(thousands(0), "0");
        assert_eq!(thousands(999), "999");
        assert_eq!(thousands(1_204), "1,204");
        assert_eq!(thousands(12_345_678), "12,345,678");
        assert_eq!(urls(1), "1 URL");
        assert_eq!(urls(4_521), "4,521 URLs");
        assert_eq!(urls(0), "0 URLs");
        assert_eq!(checks_found(0), "no checks found issues");
        assert_eq!(checks_found(1), "1 check found issues");
        assert_eq!(checks_found(1_018), "1,018 checks found issues");
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
        assert_eq!(menu_label(&entry("example.com", LiveStatus::Running, 0)), "example.com");
        assert_eq!(menu_label(&entry("example.com", LiveStatus::Paused, 0)), "example.com (paused)");
        assert_eq!(menu_label(&entry("a&b", LiveStatus::Analyzing, 0)), "a&&b (analysing)");
    }
}
