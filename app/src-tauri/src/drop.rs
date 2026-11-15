//! Files dragged onto the window. WebView2 and WebKit hand a drop to the shell, not the page (a page in a webview
//! gets no paths from a drop), so the shell forwards it to the page as `shell://drag-drop`: the page shows where a
//! drop would land while files are held over the window, then opens the crawl files dropped, and says why for any
//! other file (docs/v2-ui-architecture.md, "Opening crawl files").
//!
//! The payload, one of:
//! - `{ "type": "enter", "paths": [..], "crawlFiles": [..] }`: files are held over the window.
//! - `{ "type": "drop", "paths": [..], "crawlFiles": [..] }`: they were dropped.
//! - `{ "type": "leave" }`: they were taken away, or the drop is over.
//!
//! `paths` is every path dragged, in order; `crawlFiles` the ones that are `.sicrawl` files. Moves over the window
//! aren't sent: the page needs no position, and they arrive at the mouse's rate.

use std::path::{Path, PathBuf};

use serde::Serialize;
use tauri::{AppHandle, DragDropEvent, Emitter};

use crate::open_files::EXTENSION;
use crate::window::MAIN_WINDOW;

pub const DRAG_DROP_EVENT: &str = "shell://drag-drop";

#[derive(Clone, Debug, PartialEq, Eq, Serialize)]
#[serde(tag = "type", rename_all = "camelCase")]
pub enum DragDrop {
    #[serde(rename_all = "camelCase")]
    Enter {
        paths: Vec<String>,
        crawl_files: Vec<String>,
    },
    #[serde(rename_all = "camelCase")]
    Drop {
        paths: Vec<String>,
        crawl_files: Vec<String>,
    },
    Leave,
}

/// What to tell the page of a drag event, if anything.
pub fn payload(event: &DragDropEvent) -> Option<DragDrop> {
    match event {
        DragDropEvent::Enter { paths, .. } => {
            let (paths, crawl_files) = split(paths);
            Some(DragDrop::Enter { paths, crawl_files })
        }
        DragDropEvent::Drop { paths, .. } => {
            let (paths, crawl_files) = split(paths);
            Some(DragDrop::Drop { paths, crawl_files })
        }
        DragDropEvent::Leave => Some(DragDrop::Leave),
        _ => None,
    }
}

fn split(paths: &[PathBuf]) -> (Vec<String>, Vec<String>) {
    let crawl_files = paths.iter().filter(|path| is_crawl_file(path)).map(|path| text(path)).collect();
    (paths.iter().map(|path| text(path)).collect(), crawl_files)
}

/// A `.sicrawl` file, not a folder named like one.
fn is_crawl_file(path: &Path) -> bool {
    path.extension().is_some_and(|extension| extension.eq_ignore_ascii_case(EXTENSION)) && !path.is_dir()
}

fn text(path: &Path) -> String {
    path.to_string_lossy().into_owned()
}

/// Forwards a drag event on the main window to its page.
pub fn forward(app: &AppHandle, event: &DragDropEvent) {
    let Some(payload) = payload(event) else { return };
    if let DragDrop::Drop { paths, crawl_files } = &payload {
        log::info!("{} file(s) dropped on the window, {} of them crawl files", paths.len(), crawl_files.len());
    }
    if let Err(error) = app.emit_to(MAIN_WINDOW, DRAG_DROP_EVENT, &payload) {
        log::warn!("Couldn't tell the page about a drop: {error}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tauri::PhysicalPosition;

    fn paths(list: &[&str]) -> Vec<PathBuf> {
        list.iter().map(PathBuf::from).collect()
    }

    #[test]
    fn a_drop_names_every_path_and_which_are_crawl_files() {
        let event = DragDropEvent::Drop {
            paths: paths(&[r"C:\crawls\garden.sicrawl", r"C:\notes.txt", r"C:\crawls\SHOP.SICRAWL"]),
            position: PhysicalPosition::new(10.0, 20.0),
        };
        let payload = payload(&event).unwrap();
        assert_eq!(
            payload,
            DragDrop::Drop {
                paths: vec![
                    r"C:\crawls\garden.sicrawl".into(),
                    r"C:\notes.txt".into(),
                    r"C:\crawls\SHOP.SICRAWL".into()
                ],
                crawl_files: vec![r"C:\crawls\garden.sicrawl".into(), r"C:\crawls\SHOP.SICRAWL".into()],
            }
        );
        let json = serde_json::to_value(&payload).unwrap();
        assert_eq!(json["type"], "drop");
        assert_eq!(json["crawlFiles"][1], r"C:\crawls\SHOP.SICRAWL");
    }

    #[test]
    fn enter_and_leave_are_told_and_moves_are_not() {
        let enter = DragDropEvent::Enter { paths: paths(&["/a/b.txt"]), position: PhysicalPosition::new(0.0, 0.0) };
        assert_eq!(
            serde_json::to_value(payload(&enter).unwrap()).unwrap(),
            serde_json::json!({ "type": "enter", "paths": ["/a/b.txt"], "crawlFiles": [] })
        );
        assert_eq!(serde_json::to_value(payload(&DragDropEvent::Leave).unwrap()).unwrap()["type"], "leave");
        assert_eq!(payload(&DragDropEvent::Over { position: PhysicalPosition::new(1.0, 1.0) }), None);
    }

    #[test]
    fn a_folder_named_like_a_crawl_file_is_not_one() {
        let folder = std::env::temp_dir().join(format!("si-drop-test-{}.sicrawl", std::process::id()));
        std::fs::create_dir_all(&folder).unwrap();
        assert!(!is_crawl_file(&folder));
        std::fs::remove_dir(&folder).unwrap();
        assert!(is_crawl_file(&folder));
    }
}
