//! System notifications for crawls that ended while the window was out of sight. The webview shows its own toasts
//! when it is in front, so these are only sent when the window is hidden or not focused.

use std::collections::HashSet;

use tauri::AppHandle;
use tauri_plugin_notification::NotificationExt;

use super::model::{CrawlItem, Ended, LiveStatus};
use crate::tray::text::{checks_found, urls};

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Notice {
    pub title: &'static str,
    pub body: String,
}

/// What to tell about a crawl that left the live ones, from what the library says about it now. None when there is
/// nothing to tell: it went on to an analysis, an analysis was cancelled, or another program took it over.
///
/// `analysed_before` is when the crawl had last been analysed as an analysis of it began, if that is known: an
/// analysis that ends with the same time stored nothing, so it was cancelled.
pub fn notice(ended: &Ended, item: &CrawlItem, analysed_before: Option<&Option<String>>) -> Option<Notice> {
    if LiveStatus::parse(&item.status).is_some() {
        return None;
    }
    let name = item.display_name();
    let error = ended.error.as_deref().or(item.error.as_deref()).map(str::trim).filter(|error| !error.is_empty());
    if item.status == "failed" || ended.status == "failed" {
        let body = format!("{name}: {}", error.unwrap_or("see the library"));
        return Some(Notice { title: "Crawl failed", body });
    }
    if ended.was_analysing {
        if let Some(error) = error {
            return Some(Notice { title: "Analysis failed", body: format!("{name}: {error}") });
        }
    }
    match item.status.as_str() {
        "completed" if ended.crawled => {
            let issues =
                item.current_issue_checks().map(|checks| format!(", {}", checks_found(checks))).unwrap_or_default();
            Some(Notice { title: "Crawl finished", body: format!("{name}: {}{issues}", urls(item.fetched())) })
        }
        "stopped" if ended.crawled && !ended.was_analysing => Some(Notice {
            title: "Crawl stopped",
            body: format!("{name}: {}. Continue it from the library.", urls(item.fetched())),
        }),
        "completed" | "stopped" if ended.was_analysing => {
            let stored_nothing = analysed_before.is_some_and(|before| before.as_deref() == item.analyzed_at.as_deref());
            if stored_nothing {
                return None;
            }
            let body = match item.current_issue_checks() {
                Some(checks) => format!("{name}: {}", checks_found(checks)),
                None => name,
            };
            Some(Notice { title: "Analysis finished", body })
        }
        _ => None,
    }
}

/// The crawls a lost sidecar took with it (`lost`), as the library of the one after it reports them: a notice for
/// each that ended interrupted. One already continued, or no longer in the library, tells nothing.
pub fn interrupted<'a>(
    lost: &'a HashSet<String>,
    items: &'a [CrawlItem],
) -> impl Iterator<Item = (&'a CrawlItem, Notice)> + 'a {
    items.iter().filter(|item| lost.contains(&item.id) && item.status == INTERRUPTED).map(|item| {
        let body = format!("{}: the engine stopped. Continue it from the library.", item.display_name());
        (item, Notice { title: "Crawl interrupted", body })
    })
}

/// The library's status for a crawl whose sidecar stopped while it ran.
const INTERRUPTED: &str = "interrupted";

/// What identifies one ending of a crawl, so the same one is never told twice.
pub fn ending_key(item: &CrawlItem) -> (String, Option<String>, Option<String>) {
    (item.status.clone(), item.finished_at.clone(), item.analyzed_at.clone())
}

pub fn show(app: &AppHandle, title: &str, body: &str) {
    log::info!("Notification: {title}: {body}");
    if let Err(error) = app.notification().builder().title(title).body(body).show() {
        log::warn!("Couldn't show a notification: {error}");
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::live::model::IssueCounts;

    fn item(status: &str) -> CrawlItem {
        CrawlItem {
            id: "a".into(),
            site: Some("example.com".into()),
            status: status.into(),
            fetched_url_count: Some(4_521),
            issues: Some(IssueCounts { error: 3, warning: 12, notice: 3 }),
            analyzed_at: Some("2026-11-01T10:30:00Z".into()),
            ..CrawlItem::default()
        }
    }

    fn ended(status: &str, was_analysing: bool, crawled: bool) -> Ended {
        Ended { id: "a".into(), status: status.into(), error: None, was_analysing, crawled }
    }

    fn told(title: &'static str, body: &str) -> Option<Notice> {
        Some(Notice { title, body: body.into() })
    }

    #[test]
    fn a_crawl_that_goes_on_to_its_analysis_waits() {
        assert_eq!(notice(&ended("completed", false, true), &item("analyzing"), None), None);
    }

    #[test]
    fn a_finished_crawl_gives_its_urls_and_issues() {
        assert_eq!(
            notice(&ended("completed", true, true), &item("completed"), None),
            told("Crawl finished", "example.com: 4,521 URLs, 18 checks found issues")
        );
        let mut unanalysed = item("completed");
        unanalysed.issues = None;
        assert_eq!(
            notice(&ended("completed", false, true), &unanalysed, None),
            told("Crawl finished", "example.com: 4,521 URLs")
        );
        let mut stale = item("completed");
        stale.out_of_date = true;
        assert_eq!(
            notice(&ended("completed", false, true), &stale, None),
            told("Crawl finished", "example.com: 4,521 URLs")
        );
        let mut one = item("completed");
        one.issues = Some(IssueCounts { error: 0, warning: 1, notice: 0 });
        assert_eq!(
            notice(&ended("completed", true, true), &one, None),
            told("Crawl finished", "example.com: 4,521 URLs, 1 check found issues")
        );
    }

    #[test]
    fn a_stopped_crawl_says_it_can_continue() {
        let mut stopped = item("stopped");
        stopped.fetched_url_count = Some(1_234);
        assert_eq!(
            notice(&ended("stopped", false, true), &stopped, None),
            told("Crawl stopped", "example.com: 1,234 URLs. Continue it from the library.")
        );
    }

    #[test]
    fn an_analysis_on_its_own_gives_its_issues() {
        let before = Some("2026-10-01T09:00:00Z".to_owned());
        assert_eq!(
            notice(&ended("completed", true, false), &item("completed"), Some(&before)),
            told("Analysis finished", "example.com: 18 checks found issues")
        );
        assert_eq!(
            notice(&ended("stopped", true, false), &item("stopped"), None),
            told("Analysis finished", "example.com: 18 checks found issues")
        );
    }

    #[test]
    fn a_cancelled_analysis_tells_nothing() {
        let before = Some("2026-11-01T10:30:00Z".to_owned());
        assert_eq!(notice(&ended("completed", true, false), &item("completed"), Some(&before)), None);
    }

    #[test]
    fn failures_carry_the_error_or_point_to_the_library() {
        let failed = Ended { error: Some("The site couldn't be reached.".into()), ..ended("failed", false, true) };
        assert_eq!(
            notice(&failed, &item("failed"), None),
            told("Crawl failed", "example.com: The site couldn't be reached.")
        );
        assert_eq!(
            notice(&ended("failed", false, true), &item("failed"), None),
            told("Crawl failed", "example.com: see the library")
        );
        let analysis = Ended { error: Some("Disk full".into()), ..ended("completed", true, true) };
        assert_eq!(notice(&analysis, &item("completed"), None), told("Analysis failed", "example.com: Disk full"));
    }

    /// The review's case (M2): a crawl running in the tray when the engine crashed came back interrupted, and nobody
    /// said so. Only the crawls the lost sidecar took with it are told, and only those it left interrupted.
    #[test]
    fn crawls_a_lost_engine_left_interrupted_are_told_once_each() {
        let lost: HashSet<String> = ["a", "b", "c"].into_iter().map(String::from).collect();
        let mut continued = item("running");
        continued.id = "b".into();
        let mut unrelated = item("interrupted");
        unrelated.id = "d".into();
        let mut other = item("interrupted");
        other.id = "c".into();
        other.site = None;
        other.file_name = "shop.sicrawl".into();
        let items = [item("interrupted"), continued, unrelated, other];
        let told: Vec<_> = interrupted(&lost, &items).map(|(item, notice)| (item.id.clone(), notice)).collect();
        assert_eq!(
            told,
            [
                (
                    "a".to_owned(),
                    Notice {
                        title: "Crawl interrupted",
                        body: "example.com: the engine stopped. Continue it from the library.".into()
                    }
                ),
                (
                    "c".to_owned(),
                    Notice {
                        title: "Crawl interrupted",
                        body: "shop: the engine stopped. Continue it from the library.".into()
                    }
                ),
            ]
        );
        assert_eq!(interrupted(&HashSet::new(), &items).count(), 0);
    }

    #[test]
    fn crawls_taken_over_or_interrupted_tell_nothing() {
        assert_eq!(notice(&ended("inUse", false, true), &item("inUse"), None), None);
        assert_eq!(notice(&ended("interrupted", false, true), &item("interrupted"), None), None);
    }
}
