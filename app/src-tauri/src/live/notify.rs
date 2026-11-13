//! System notifications for crawls that ended while the window was out of sight. The webview shows its own toasts
//! when it is in front, so these are only sent when the window is hidden or not focused.

use std::collections::HashSet;

use tauri::AppHandle;
use tauri_plugin_notification::NotificationExt;

use super::model::{CrawlItem, Ended, LiveStatus};
use crate::tray::text::{checks_found, thousands, urls};

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Notice {
    pub title: String,
    pub body: String,
}

impl Notice {
    fn new(title: impl Into<String>, body: impl Into<String>) -> Self {
        Self { title: title.into(), body: body.into() }
    }
}

/// Which words a notice is in: Advanced's, or Simple's (docs/v2-ui-simple.md, "The report at the end" 2), which carry
/// the score and the problems to fix, as the page's toast does (src/simple/finishedWords.ts).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum Words {
    #[default]
    Advanced,
    /// `problems`: the checks with issues that are Serious or Worth fixing, less those the user has hidden; None when
    /// they aren't known.
    Simple { problems: Option<u64> },
}

/// What to tell about a crawl that left the live ones, from what the library says about it now. None when there is
/// nothing to tell: it went on to an analysis, an analysis was cancelled, or another program took it over.
///
/// `analysed_before` is when the crawl had last been analysed as an analysis of it began, if that is known: an
/// analysis that ends with the same time stored nothing, so it was cancelled.
pub fn notice(
    ended: &Ended,
    item: &CrawlItem,
    analysed_before: Option<&Option<String>>,
    words: Words,
) -> Option<Notice> {
    if LiveStatus::parse(&item.status).is_some() {
        return None;
    }
    if let Words::Simple { problems } = words {
        return simple_notice(ended, item, analysed_before, problems);
    }
    let name = item.display_name();
    let error = ended.error.as_deref().or(item.error.as_deref()).map(str::trim).filter(|error| !error.is_empty());
    if item.status == "failed" || ended.status == "failed" {
        let body = format!("{name}: {}", error.unwrap_or("see the library"));
        return Some(Notice::new("Crawl failed", body));
    }
    if ended.was_analysing {
        if let Some(error) = error {
            return Some(Notice::new("Analysis failed", format!("{name}: {error}")));
        }
    }
    match item.status.as_str() {
        "completed" if ended.crawled => {
            let issues =
                item.current_issue_checks().map(|checks| format!(", {}", checks_found(checks))).unwrap_or_default();
            Some(Notice::new("Crawl finished", format!("{name}: {}{issues}", urls(item.fetched()))))
        }
        "stopped" if ended.crawled && !ended.was_analysing => Some(Notice::new(
            "Crawl stopped",
            format!("{name}: {}. Continue it from the library.", urls(item.fetched())),
        )),
        "completed" | "stopped" if ended.was_analysing => {
            if stored_nothing(item, analysed_before) {
                return None;
            }
            let body = match item.current_issue_checks() {
                Some(checks) => format!("{name}: {}", checks_found(checks)),
                None => name,
            };
            Some(Notice::new("Analysis finished", body))
        }
        _ => None,
    }
}

/// An analysis that ends with the time it began with stored nothing: it was cancelled.
fn stored_nothing(item: &CrawlItem, analysed_before: Option<&Option<String>>) -> bool {
    analysed_before.is_some_and(|before| before.as_deref() == item.analyzed_at.as_deref())
}

/// Simple's words for the same endings: "gardenfurnituresets.co.uk is checked", "Score 85: 6 problems to fix".
fn simple_notice(
    ended: &Ended,
    item: &CrawlItem,
    analysed_before: Option<&Option<String>>,
    problems: Option<u64>,
) -> Option<Notice> {
    let name = item.display_name();
    let error = ended.error.as_deref().or(item.error.as_deref()).map(str::trim).filter(|error| !error.is_empty());
    if item.status == "failed" || ended.status == "failed" {
        let body = error.map_or_else(|| "See Your sites.".to_owned(), str::to_owned);
        return Some(Notice::new(format!("The check of {name} couldn't finish"), body));
    }
    if ended.was_analysing {
        if let Some(error) = error {
            return Some(Notice::new(format!("The results for {name} couldn't be worked out"), error));
        }
    }
    match item.status.as_str() {
        "completed" if ended.crawled => Some(Notice::new(format!("{name} is checked"), result_words(item, problems))),
        "stopped" if ended.crawled && !ended.was_analysing => Some(Notice::new(
            format!("The check of {name} was stopped"),
            format!("{} checked. Continue it from Your sites.", addresses(item.fetched())),
        )),
        "completed" | "stopped" if ended.was_analysing => {
            if stored_nothing(item, analysed_before) {
                return None;
            }
            Some(Notice::new(format!("The results for {name} are updated"), result_words(item, problems)))
        }
        _ => None,
    }
}

/// "Score 85: 6 problems to fix", "Score 71 so far: 1 problem to fix", or why there's no score.
fn result_words(item: &CrawlItem, problems: Option<u64>) -> String {
    let to_fix = problems.map(|count| match count {
        0 => "nothing to fix".to_owned(),
        1 => "1 problem to fix".to_owned(),
        _ => format!("{} problems to fix", thousands(count)),
    });
    if let Some(score) = item.score {
        let value = format!("Score {}{}", thousands(score.value), if score.partial { " so far" } else { "" });
        return match to_fix {
            Some(to_fix) => format!("{value}: {to_fix}"),
            None => value,
        };
    }
    if item.page_count == Some(0) {
        return "No pages could be read, so there's no score.".to_owned();
    }
    match to_fix {
        Some(to_fix) => {
            let mut letters = to_fix.chars();
            letters.next().map_or_else(String::new, |first| format!("{}{}.", first.to_uppercase(), letters.as_str()))
        }
        None => "The results are ready.".to_owned(),
    }
}

/// "1 address", "4,521 addresses".
fn addresses(count: u64) -> String {
    if count == 1 {
        "1 address".to_owned()
    } else {
        format!("{} addresses", thousands(count))
    }
}

/// The crawls a lost sidecar took with it (`lost`), as the library of the one after it reports them: a notice for
/// each that ended interrupted, in Simple's words when `simple`. One already continued, or no longer in the library,
/// tells nothing.
pub fn interrupted<'a>(
    lost: &'a HashSet<String>,
    items: &'a [CrawlItem],
    simple: bool,
) -> impl Iterator<Item = (&'a CrawlItem, Notice)> + 'a {
    items.iter().filter(|item| lost.contains(&item.id) && item.status == INTERRUPTED).map(move |item| {
        let name = item.display_name();
        let notice = if simple {
            Notice::new(
                format!("The check of {name} was interrupted"),
                "Shouting Iguana's engine stopped. Continue it from Your sites.",
            )
        } else {
            Notice::new("Crawl interrupted", format!("{name}: the engine stopped. Continue it from the library."))
        };
        (item, notice)
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
    use crate::live::model::{IssueCounts, ScoreSummary};

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

    fn told(title: &str, body: &str) -> Option<Notice> {
        Some(Notice::new(title, body))
    }

    #[test]
    fn a_crawl_that_goes_on_to_its_analysis_waits() {
        assert_eq!(notice(&ended("completed", false, true), &item("analyzing"), None, Words::Advanced), None);
    }

    #[test]
    fn a_finished_crawl_gives_its_urls_and_issues() {
        assert_eq!(
            notice(&ended("completed", true, true), &item("completed"), None, Words::Advanced),
            told("Crawl finished", "example.com: 4,521 URLs, 18 checks found issues")
        );
        let mut unanalysed = item("completed");
        unanalysed.issues = None;
        assert_eq!(
            notice(&ended("completed", false, true), &unanalysed, None, Words::Advanced),
            told("Crawl finished", "example.com: 4,521 URLs")
        );
        let mut stale = item("completed");
        stale.out_of_date = true;
        assert_eq!(
            notice(&ended("completed", false, true), &stale, None, Words::Advanced),
            told("Crawl finished", "example.com: 4,521 URLs")
        );
        let mut one = item("completed");
        one.issues = Some(IssueCounts { error: 0, warning: 1, notice: 0 });
        assert_eq!(
            notice(&ended("completed", true, true), &one, None, Words::Advanced),
            told("Crawl finished", "example.com: 4,521 URLs, 1 check found issues")
        );
    }

    #[test]
    fn a_stopped_crawl_says_it_can_continue() {
        let mut stopped = item("stopped");
        stopped.fetched_url_count = Some(1_234);
        assert_eq!(
            notice(&ended("stopped", false, true), &stopped, None, Words::Advanced),
            told("Crawl stopped", "example.com: 1,234 URLs. Continue it from the library.")
        );
    }

    #[test]
    fn an_analysis_on_its_own_gives_its_issues() {
        let before = Some("2026-10-01T09:00:00Z".to_owned());
        assert_eq!(
            notice(&ended("completed", true, false), &item("completed"), Some(&before), Words::Advanced),
            told("Analysis finished", "example.com: 18 checks found issues")
        );
        assert_eq!(
            notice(&ended("stopped", true, false), &item("stopped"), None, Words::Advanced),
            told("Analysis finished", "example.com: 18 checks found issues")
        );
    }

    #[test]
    fn a_cancelled_analysis_tells_nothing() {
        let before = Some("2026-11-01T10:30:00Z".to_owned());
        assert_eq!(notice(&ended("completed", true, false), &item("completed"), Some(&before), Words::Advanced), None);
    }

    #[test]
    fn failures_carry_the_error_or_point_to_the_library() {
        let failed = Ended { error: Some("The site couldn't be reached.".into()), ..ended("failed", false, true) };
        assert_eq!(
            notice(&failed, &item("failed"), None, Words::Advanced),
            told("Crawl failed", "example.com: The site couldn't be reached.")
        );
        assert_eq!(
            notice(&ended("failed", false, true), &item("failed"), None, Words::Advanced),
            told("Crawl failed", "example.com: see the library")
        );
        let analysis = Ended { error: Some("Disk full".into()), ..ended("completed", true, true) };
        assert_eq!(
            notice(&analysis, &item("completed"), None, Words::Advanced),
            told("Analysis failed", "example.com: Disk full")
        );
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
        let told: Vec<_> = interrupted(&lost, &items, false).map(|(item, notice)| (item.id.clone(), notice)).collect();
        assert_eq!(
            told,
            [
                (
                    "a".to_owned(),
                    Notice::new("Crawl interrupted", "example.com: the engine stopped. Continue it from the library.")
                ),
                (
                    "c".to_owned(),
                    Notice::new("Crawl interrupted", "shop: the engine stopped. Continue it from the library.")
                ),
            ]
        );
        assert_eq!(interrupted(&HashSet::new(), &items, false).count(), 0);
        let simple: Vec<_> = interrupted(&lost, &items, true).map(|(_, notice)| notice.title).collect();
        assert_eq!(simple, ["The check of example.com was interrupted", "The check of shop was interrupted"]);
    }

    fn simple(problems: Option<u64>) -> Words {
        Words::Simple { problems }
    }

    fn body(notice: Option<Notice>) -> Option<String> {
        notice.map(|notice| notice.body)
    }

    #[test]
    fn simple_says_the_site_is_checked_with_its_score_and_the_problems_to_fix() {
        let mut garden = item("completed");
        garden.site = Some("gardenfurnituresets.co.uk".into());
        garden.score = Some(ScoreSummary { value: 85, partial: false });
        garden.page_count = Some(265);
        let done = ended("completed", true, true);
        assert_eq!(
            notice(&done, &garden, None, simple(Some(6))),
            told("gardenfurnituresets.co.uk is checked", "Score 85: 6 problems to fix")
        );
        let mut partial = garden.clone();
        partial.score = Some(ScoreSummary { value: 71, partial: true });
        assert_eq!(
            body(notice(&done, &partial, None, simple(Some(1)))).as_deref(),
            Some("Score 71 so far: 1 problem to fix")
        );
        let mut clean = garden.clone();
        clean.score = Some(ScoreSummary { value: 100, partial: false });
        assert_eq!(body(notice(&done, &clean, None, simple(Some(0)))).as_deref(), Some("Score 100: nothing to fix"));
        let mut empty = garden.clone();
        empty.score = None;
        empty.page_count = Some(0);
        assert_eq!(
            body(notice(&done, &empty, None, simple(Some(0)))).as_deref(),
            Some("No pages could be read, so there's no score.")
        );
        let mut unscored = garden.clone();
        unscored.score = None;
        assert_eq!(body(notice(&done, &unscored, None, simple(Some(4)))).as_deref(), Some("4 problems to fix."));
        assert_eq!(
            notice(&ended("completed", true, false), &garden, None, simple(None)),
            told("The results for gardenfurnituresets.co.uk are updated", "Score 85")
        );
    }

    #[test]
    fn simple_says_a_stop_a_failure_and_a_cancel_in_its_words() {
        let mut stopped = item("stopped");
        stopped.fetched_url_count = Some(1_234);
        assert_eq!(
            notice(&ended("stopped", false, true), &stopped, None, simple(None)),
            told("The check of example.com was stopped", "1,234 addresses checked. Continue it from Your sites.")
        );
        assert_eq!(
            notice(&ended("failed", false, true), &item("failed"), None, simple(None)),
            told("The check of example.com couldn't finish", "See Your sites.")
        );
        let before = Some("2026-11-01T10:30:00Z".to_owned());
        assert_eq!(notice(&ended("completed", true, false), &item("completed"), Some(&before), simple(None)), None);
        assert_eq!(notice(&ended("completed", false, true), &item("analyzing"), None, simple(None)), None);
    }

    #[test]
    fn crawls_taken_over_or_interrupted_tell_nothing() {
        assert_eq!(notice(&ended("inUse", false, true), &item("inUse"), None, Words::Advanced), None);
        assert_eq!(notice(&ended("interrupted", false, true), &item("interrupted"), None, Words::Advanced), None);
    }
}
