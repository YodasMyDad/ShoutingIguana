//! Which of the webview's default context menu items stay: the editing ones only, and Inspect in debug builds. The
//! rest (back, reload, save as, print, share…) make no sense in an app.

// Only WebView2 has a context menu to trim; elsewhere this is kept for its tests.
#![cfg_attr(not(windows), allow(dead_code))]

/// WebView2's names for the items kept (`ICoreWebView2ContextMenuItem::Name`), compared ignoring case: the docs say
/// "spellCheck" for spelling suggestions, and the runtime (WebView2 2026) says "spellcheck".
const EDITING: [&str; 6] = ["cut", "copy", "paste", "selectAll", "spellCheck", "emoji"];
const INSPECT: &str = "inspectElement";

fn is_named(item: &MenuItem<'_>, names: &[&str]) -> bool {
    names.iter().any(|name| name.eq_ignore_ascii_case(item.name))
}

pub struct MenuItem<'a> {
    pub name: &'a str,
    pub separator: bool,
}

/// For each item, whether it stays. A separator stays only between two groups of kept items, once.
pub fn kept(items: &[MenuItem<'_>], inspect: bool) -> Vec<bool> {
    let mut keep: Vec<bool> = items
        .iter()
        .map(|item| !item.separator && (is_named(item, &EDITING) || (inspect && is_named(item, &[INSPECT]))))
        .collect();
    let mut after_kept = false;
    let mut separator = None;
    for (index, item) in items.iter().enumerate() {
        if item.separator {
            if after_kept && separator.is_none() {
                separator = Some(index);
            }
        } else if keep[index] {
            if let Some(separator) = separator.take() {
                keep[separator] = true;
            }
            after_kept = true;
        }
    }
    keep
}

#[cfg(test)]
mod tests {
    use super::*;

    /// "-" is a separator, as WebView2 names them "other" (so do some other items).
    fn menu(names: &[&'static str]) -> Vec<MenuItem<'static>> {
        names
            .iter()
            .map(|&name| MenuItem { name: if name == "-" { "other" } else { name }, separator: name == "-" })
            .collect()
    }

    fn kept_names(names: &[&'static str], inspect: bool) -> Vec<&'static str> {
        let items = menu(names);
        names.iter().zip(kept(&items, inspect)).filter(|(_, keep)| *keep).map(|(name, _)| *name).collect()
    }

    #[test]
    fn a_text_field_keeps_its_editing_items() {
        let field = [
            "undo",
            "redo",
            "-",
            "cut",
            "copy",
            "paste",
            "pasteAndMatchStyle",
            "-",
            "selectAll",
            "-",
            "emoji",
            "-",
            "inspectElement",
        ];
        assert_eq!(kept_names(&field, false), ["cut", "copy", "paste", "-", "selectAll", "-", "emoji"]);
        assert_eq!(
            kept_names(&field, true),
            ["cut", "copy", "paste", "-", "selectAll", "-", "emoji", "-", "inspectElement"]
        );
    }

    #[test]
    fn a_page_loses_its_browser_items() {
        let page = ["back", "forward", "reload", "-", "saveAs", "print", "share", "-", "inspectElement"];
        assert!(kept_names(&page, false).is_empty());
        assert_eq!(kept_names(&page, true), ["inspectElement"]);
    }

    /// The menus WebView2 gave on a text field and on a misspelt word, as logged.
    #[test]
    fn the_runtimes_own_menus_are_trimmed_as_meant() {
        let field = [
            "emoji",
            "-",
            "undo",
            "redo",
            "-",
            "cut",
            "copy",
            "paste",
            "pasteAndMatchStyle",
            "selectAll",
            "-",
            "-",
            "-",
            "moreTools",
            "-",
            "inspectElement",
        ];
        assert_eq!(kept_names(&field, false), ["emoji", "-", "cut", "copy", "paste", "selectAll"]);
        let misspelt = [
            "spellcheck",
            "spellcheck",
            "spellcheck",
            "-",
            "cut",
            "copy",
            "paste",
            "pasteAndMatchStyle",
            "-",
            "moreTools",
            "-",
            "inspectElement",
        ];
        assert_eq!(
            kept_names(&misspelt, false),
            ["spellcheck", "spellcheck", "spellcheck", "-", "cut", "copy", "paste"]
        );
        assert_eq!(
            kept_names(&misspelt, true),
            ["spellcheck", "spellcheck", "spellcheck", "-", "cut", "copy", "paste", "-", "inspectElement"]
        );
    }

    #[test]
    fn names_match_whatever_their_case() {
        assert_eq!(kept_names(&["SpellCheck", "SELECTALL", "Print"], false), ["SpellCheck", "SELECTALL"]);
    }

    #[test]
    fn spelling_suggestions_stay() {
        let misspelt = ["spellCheck", "spellCheck", "-", "cut", "-", "-", "copy", "-"];
        assert_eq!(kept_names(&misspelt, false), ["spellCheck", "spellCheck", "-", "cut", "-", "copy"]);
    }
}
