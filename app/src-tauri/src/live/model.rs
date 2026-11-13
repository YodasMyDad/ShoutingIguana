//! The shell's own view of what the engine is doing, kept from the sidecar's event stream: the live crawls, their
//! names and progress. The tray shows it and the quit flow asks it; it never depends on the webview.

use std::collections::{HashMap, HashSet};

use serde::Deserialize;

/// The statuses during which a crawl is live in the sidecar.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum LiveStatus {
    Running,
    Paused,
    Stopping,
    Analyzing,
}

impl LiveStatus {
    /// None for every status that isn't live: stopped, completed, failed, interrupted, inUse, unreadable.
    pub fn parse(status: &str) -> Option<Self> {
        match status {
            "running" => Some(Self::Running),
            "paused" => Some(Self::Paused),
            "stopping" => Some(Self::Stopping),
            "analyzing" => Some(Self::Analyzing),
            _ => None,
        }
    }

    pub fn is_crawling(self) -> bool {
        self != Self::Analyzing
    }
}

/// `hello`: the first event on every stream, naming the crawls running, paused or stopping (not analysing).
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Hello {
    #[serde(default)]
    pub live: Vec<String>,
}

/// `progress`, twice a second per live crawl. Only what the tray needs is read.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Progress {
    pub crawl_id: String,
    pub status: String,
    #[serde(default)]
    pub fetched: u64,
    #[serde(default)]
    pub queued: u64,
    #[serde(default)]
    pub in_flight: u64,
}

/// `crawl`: a status change, with the library's statuses.
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CrawlChange {
    pub crawl_id: String,
    pub status: String,
    pub error: Option<String>,
}

#[derive(Debug)]
pub enum Event {
    Hello(Hello),
    Progress(Progress),
    Crawl(CrawlChange),
    /// `alert`, `library` and anything newer: the tray has no use for them.
    Other,
}

impl Event {
    pub fn parse(name: &str, data: &str) -> serde_json::Result<Self> {
        Ok(match name {
            "hello" => Self::Hello(serde_json::from_str(data)?),
            "progress" => Self::Progress(serde_json::from_str(data)?),
            "crawl" => Self::Crawl(serde_json::from_str(data)?),
            _ => Self::Other,
        })
    }
}

/// A library item (`GET /api/crawls/{id}`), as far as the shell reads it.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CrawlItem {
    pub id: String,
    /// Where the crawl's file is.
    #[serde(default)]
    pub path: String,
    #[serde(default)]
    pub file_name: String,
    pub site: Option<String>,
    pub status: String,
    pub error: Option<String>,
    /// The URLs fetched. Older sidecars call it `urlCount`.
    pub fetched_url_count: Option<u64>,
    pub url_count: Option<u64>,
    pub issues: Option<IssueCounts>,
    pub analyzed_at: Option<String>,
    pub finished_at: Option<String>,
    #[serde(default)]
    pub out_of_date: bool,
    /// The score in brief, for Simple's notice ("Score 85: 6 problems to fix"). None before an analysis, with no
    /// page read, or from a sidecar from before the score.
    #[serde(default)]
    pub score: Option<ScoreSummary>,
    /// The pages it read: none means no score can be given.
    #[serde(default)]
    pub page_count: Option<u64>,
}

/// A library item's score: its value, and whether it covers only part of the site ("so far").
#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq, Eq)]
pub struct ScoreSummary {
    pub value: u64,
    #[serde(default)]
    pub partial: bool,
}

/// Checks with at least one issue, by severity.
#[derive(Clone, Copy, Debug, Default, Deserialize, PartialEq, Eq)]
pub struct IssueCounts {
    #[serde(default)]
    pub error: u64,
    #[serde(default)]
    pub warning: u64,
    #[serde(default)]
    pub notice: u64,
}

impl CrawlItem {
    /// The site, else the file's name without its extension.
    pub fn display_name(&self) -> String {
        if let Some(site) = self.site.as_deref().map(str::trim).filter(|site| !site.is_empty()) {
            return site.to_owned();
        }
        let name = self.file_name.trim();
        let stem = name
            .len()
            .checked_sub(".sicrawl".len())
            .filter(|&end| end > 0 && name.is_char_boundary(end) && name[end..].eq_ignore_ascii_case(".sicrawl"));
        let name = stem.map_or(name, |end| &name[..end]);
        if name.is_empty() { UNNAMED } else { name }.to_owned()
    }

    pub fn fetched(&self) -> u64 {
        self.fetched_url_count.or(self.url_count).unwrap_or(0)
    }

    /// Checks that found issues, when the counts describe the crawl as it is now.
    pub fn current_issue_checks(&self) -> Option<u64> {
        self.issues.filter(|_| !self.out_of_date).map(|issues| issues.error + issues.warning + issues.notice)
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LiveCrawl {
    pub id: String,
    pub status: LiveStatus,
    pub fetched: u64,
    pub queued: u64,
    pub in_flight: u64,
}

/// A crawl that left the live ones.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Ended {
    pub id: String,
    pub status: String,
    pub error: Option<String>,
    /// It was being analysed, rather than crawled, when it ended.
    pub was_analysing: bool,
    /// It crawled while this shell watched, so its end is a crawl's end even after an analysis.
    pub crawled: bool,
}

/// What an event changed, for the tray and the notifications.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct Change {
    /// The set of live crawls, a status or a name changed: the tray's menu is rebuilt.
    pub menu: bool,
    /// Numbers changed: the tooltip and the ring.
    pub progress: bool,
    /// Ask the library about this crawl: its name isn't known, or an analysis began that needs its baseline.
    pub look_up: Option<String>,
    pub ended: Option<Ended>,
}

/// A live crawl as the tray shows it.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TrayEntry {
    pub id: String,
    pub name: String,
    pub status: LiveStatus,
    pub fetched: u64,
}

/// The progress ring: the whole percent fetched of what is known, and whether only paused crawls make it up.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Ring {
    pub percent: u8,
    pub paused: bool,
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct LiveKinds {
    pub crawling: bool,
    pub analysing: bool,
}

impl LiveKinds {
    pub fn any(self) -> bool {
        self.crawling || self.analysing
    }

    pub fn of_items(items: &[CrawlItem]) -> Self {
        items.iter().filter_map(|item| LiveStatus::parse(&item.status)).fold(Self::default(), |kinds, status| Self {
            crawling: kinds.crawling || status.is_crawling(),
            analysing: kinds.analysing || !status.is_crawling(),
        })
    }
}

/// Shown until the library has named a crawl.
pub const UNNAMED: &str = "Crawl";

#[derive(Debug, Default)]
pub struct LiveView {
    crawls: Vec<LiveCrawl>,
    /// Names outlive a crawl's time in the list: a finished crawl comes back at once to be analysed.
    names: HashMap<String, String>,
    /// Crawls seen crawling, until their end has been told.
    crawled: HashSet<String>,
}

impl LiveView {
    /// A new stream's `hello`: the crawling crawls it lists are live; others it doesn't list have ended unseen.
    /// Analyses aren't listed, so they stay until the library says otherwise.
    pub fn hello(&mut self, live: &[String]) -> Change {
        let mut change = Change::default();
        let before = self.crawls.len();
        self.crawls.retain(|crawl| !crawl.status.is_crawling() || live.contains(&crawl.id));
        change.menu = self.crawls.len() != before;
        for id in live {
            self.crawled.insert(id.clone());
            if !self.crawls.iter().any(|crawl| &crawl.id == id) {
                self.crawls.push(LiveCrawl {
                    id: id.clone(),
                    status: LiveStatus::Running,
                    fetched: 0,
                    queued: 0,
                    in_flight: 0,
                });
                change.menu = true;
                if !self.names.contains_key(id) {
                    change.look_up.get_or_insert_with(|| id.clone());
                }
            }
        }
        change
    }

    /// The library's word on every crawl, after `hello`: the live statuses and every name, in one request.
    pub fn resync(&mut self, items: &[CrawlItem]) -> Change {
        let mut change = Change::default();
        for item in items {
            let name = item.display_name();
            if self.names.get(&item.id) != Some(&name) {
                change.menu |= self.crawls.iter().any(|crawl| crawl.id == item.id);
                self.names.insert(item.id.clone(), name);
            }
        }
        let live: Vec<(&CrawlItem, LiveStatus)> =
            items.iter().filter_map(|item| LiveStatus::parse(&item.status).map(|status| (item, status))).collect();
        let before = self.crawls.len();
        self.crawls.retain(|crawl| live.iter().any(|(item, _)| item.id == crawl.id));
        change.menu |= self.crawls.len() != before;
        for (item, status) in live {
            if status.is_crawling() {
                self.crawled.insert(item.id.clone());
            }
            match self.crawls.iter_mut().find(|crawl| crawl.id == item.id) {
                Some(crawl) => {
                    change.menu |= crawl.status != status;
                    crawl.status = status;
                }
                None => {
                    self.crawls.push(LiveCrawl {
                        id: item.id.clone(),
                        status,
                        fetched: item.fetched(),
                        queued: 0,
                        in_flight: 0,
                    });
                    change.menu = true;
                }
            }
        }
        change
    }

    pub fn progress(&mut self, progress: &Progress) -> Change {
        let Some(status) = LiveStatus::parse(&progress.status) else {
            return Change::default();
        };
        let mut change = Change { progress: true, ..Change::default() };
        if status.is_crawling() {
            self.crawled.insert(progress.crawl_id.clone());
        }
        let crawl = match self.crawls.iter_mut().position(|crawl| crawl.id == progress.crawl_id) {
            Some(index) => &mut self.crawls[index],
            None => {
                change.menu = true;
                if !self.names.contains_key(&progress.crawl_id) {
                    change.look_up = Some(progress.crawl_id.clone());
                }
                self.crawls.push(LiveCrawl {
                    id: progress.crawl_id.clone(),
                    status,
                    fetched: 0,
                    queued: 0,
                    in_flight: 0,
                });
                self.crawls.last_mut().expect("just pushed")
            }
        };
        change.menu |= crawl.status != status;
        crawl.status = status;
        crawl.fetched = progress.fetched;
        crawl.queued = progress.queued;
        crawl.in_flight = progress.in_flight;
        change
    }

    pub fn crawl(&mut self, event: &CrawlChange) -> Change {
        let mut change = Change::default();
        let index = self.crawls.iter().position(|crawl| crawl.id == event.crawl_id);
        match (LiveStatus::parse(&event.status), index) {
            (Some(status), Some(index)) => {
                let crawl = &mut self.crawls[index];
                if crawl.status != status {
                    change.menu = true;
                    if status == LiveStatus::Analyzing && !self.crawled.contains(&crawl.id) {
                        change.look_up = Some(crawl.id.clone());
                    }
                }
                crawl.status = status;
                if status.is_crawling() {
                    self.crawled.insert(event.crawl_id.clone());
                }
            }
            (Some(status), None) => {
                change.menu = true;
                if status.is_crawling() {
                    self.crawled.insert(event.crawl_id.clone());
                }
                let analysing_unseen = !status.is_crawling() && !self.crawled.contains(&event.crawl_id);
                if analysing_unseen || !self.names.contains_key(&event.crawl_id) {
                    change.look_up = Some(event.crawl_id.clone());
                }
                self.crawls.push(LiveCrawl { id: event.crawl_id.clone(), status, fetched: 0, queued: 0, in_flight: 0 });
            }
            (None, Some(index)) => {
                let crawl = self.crawls.remove(index);
                change.menu = true;
                change.ended = Some(Ended {
                    id: crawl.id,
                    status: event.status.clone(),
                    error: event.error.clone(),
                    was_analysing: crawl.status == LiveStatus::Analyzing,
                    crawled: self.crawled.contains(&event.crawl_id),
                });
            }
            // The end of a crawl this shell never saw live: nothing to tell.
            (None, None) => {}
        }
        change
    }

    /// The crawls that are crawling (running, paused or stopping): if the sidecar goes, these are what it took with it.
    pub fn crawling(&self) -> impl Iterator<Item = &str> {
        self.crawls.iter().filter(|crawl| crawl.status.is_crawling()).map(|crawl| crawl.id.as_str())
    }

    /// The sidecar went away: nothing it said holds any more.
    pub fn clear(&mut self) -> Change {
        let menu = !self.crawls.is_empty();
        self.crawls.clear();
        self.crawled.clear();
        Change { menu, progress: menu, ..Change::default() }
    }

    pub fn set_name(&mut self, id: &str, name: String) -> bool {
        let live = self.crawls.iter().any(|crawl| crawl.id == id);
        let changed = self.names.get(id) != Some(&name);
        self.names.insert(id.to_owned(), name);
        live && changed
    }

    pub fn was_crawled(&self, id: &str) -> bool {
        self.crawled.contains(id)
    }

    /// The crawl's end has been told (or it went on to something else): its next end starts afresh.
    pub fn finish(&mut self, id: &str) {
        self.crawled.remove(id);
    }

    pub fn kinds(&self) -> LiveKinds {
        LiveKinds {
            crawling: self.crawls.iter().any(|crawl| crawl.status.is_crawling()),
            analysing: self.crawls.iter().any(|crawl| !crawl.status.is_crawling()),
        }
    }

    pub fn entries(&self) -> Vec<TrayEntry> {
        self.crawls
            .iter()
            .map(|crawl| TrayEntry {
                id: crawl.id.clone(),
                name: self.names.get(&crawl.id).cloned().unwrap_or_else(|| UNNAMED.to_owned()),
                status: crawl.status,
                fetched: crawl.fetched,
            })
            .collect()
    }

    /// The share fetched of what is known (fetched, queued and in flight) over the crawling crawls; with none
    /// crawling, over the paused ones, drawn grey. None when only analyses run, or nothing does.
    pub fn ring(&self) -> Option<Ring> {
        let active: Vec<&LiveCrawl> = self
            .crawls
            .iter()
            .filter(|crawl| matches!(crawl.status, LiveStatus::Running | LiveStatus::Stopping))
            .collect();
        let (crawls, paused) = if active.is_empty() {
            (self.crawls.iter().filter(|crawl| crawl.status == LiveStatus::Paused).collect(), true)
        } else {
            (active, false)
        };
        if crawls.is_empty() {
            return None;
        }
        let fetched: u64 = crawls.iter().map(|crawl| crawl.fetched).sum();
        let known: u64 = crawls.iter().map(|crawl| crawl.fetched + crawl.queued + crawl.in_flight).sum();
        let percent = if known == 0 { 0 } else { (u128::from(fetched) * 100 / u128::from(known)).min(100) as u8 };
        Some(Ring { percent, paused })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn progress(id: &str, status: &str, fetched: u64, queued: u64, in_flight: u64) -> Progress {
        Progress { crawl_id: id.into(), status: status.into(), fetched, queued, in_flight }
    }

    fn change(id: &str, status: &str) -> CrawlChange {
        CrawlChange { crawl_id: id.into(), status: status.into(), error: None }
    }

    fn item(id: &str, site: Option<&str>, status: &str) -> CrawlItem {
        CrawlItem { id: id.into(), site: site.map(Into::into), status: status.into(), ..CrawlItem::default() }
    }

    #[test]
    fn events_parse_with_the_sidecars_names() {
        let hello = Event::parse("hello", r#"{"live":["a","b"]}"#).unwrap();
        assert!(matches!(hello, Event::Hello(Hello { live }) if live == ["a", "b"]));
        let data = r#"{"crawlId":"a","status":"running","elapsedMs":5,"discovered":9,"queued":4,"inFlight":2,"fetched":3,
            "htmlPages":1,"redirects":0,"errors":0,"blocked":0,"robotsDisallowed":0,"skipped":0,"bytesDownloaded":9,"urlsPerSecond":1.5}"#;
        let Event::Progress(progress) = Event::parse("progress", data).unwrap() else { panic!("not progress") };
        assert_eq!((progress.fetched, progress.queued, progress.in_flight), (3, 4, 2));
        let old =
            Event::parse("progress", r#"{"crawlId":"a","status":"paused","fetched":1,"pagesPerSecond":2}"#).unwrap();
        assert!(matches!(old, Event::Progress(Progress { fetched: 1, .. })));
        let crawl = Event::parse("crawl", r#"{"crawlId":"a","status":"failed","error":"Boom"}"#).unwrap();
        assert!(matches!(crawl, Event::Crawl(CrawlChange { error: Some(error), .. }) if error == "Boom"));
        assert!(matches!(Event::parse("library", "{}").unwrap(), Event::Other));
        assert!(Event::parse("crawl", "{}").is_err());
    }

    #[test]
    fn items_read_new_and_old_url_counts() {
        let new: CrawlItem = serde_json::from_str(
            r#"{"id":"a","fileName":"f","status":"completed","fetchedUrlCount":4521,"urlCount":1}"#,
        )
        .unwrap();
        assert_eq!(new.fetched(), 4521);
        let old: CrawlItem =
            serde_json::from_str(r#"{"id":"a","fileName":"f","status":"completed","urlCount":12,"issues":null}"#)
                .unwrap();
        assert_eq!(old.fetched(), 12);
        assert_eq!(old.current_issue_checks(), None);
    }

    #[test]
    fn issue_checks_count_only_when_up_to_date() {
        let mut item = item("a", Some("example.com"), "completed");
        item.issues = Some(IssueCounts { error: 3, warning: 12, notice: 3 });
        assert_eq!(item.current_issue_checks(), Some(18));
        item.out_of_date = true;
        assert_eq!(item.current_issue_checks(), None);
    }

    #[test]
    fn names_fall_back_to_the_file_name_without_its_extension() {
        assert_eq!(item("a", Some("example.com"), "running").display_name(), "example.com");
        let mut unnamed = item("a", None, "running");
        unnamed.file_name = "example.com-20261101-101500.SICRAWL".into();
        assert_eq!(unnamed.display_name(), "example.com-20261101-101500");
        unnamed.file_name = "notes.txt".into();
        assert_eq!(unnamed.display_name(), "notes.txt");
        unnamed.site = Some("  ".into());
        assert_eq!(unnamed.display_name(), "notes.txt");
    }

    #[test]
    fn a_crawl_appears_with_its_first_event_and_is_looked_up() {
        let mut view = LiveView::default();
        let change = view.crawl(&change("a", "running"));
        assert!(change.menu);
        assert_eq!(change.look_up.as_deref(), Some("a"));
        assert!(view.set_name("a", "example.com".into()));
        assert_eq!(view.entries()[0].name, "example.com");

        let change = view.progress(&progress("a", "running", 10, 30, 2));
        assert_eq!(change, Change { progress: true, ..Change::default() });
        assert_eq!(view.ring(), Some(Ring { percent: 23, paused: false }));
    }

    #[test]
    fn progress_rebuilds_the_menu_only_when_a_status_changes() {
        let mut view = LiveView::default();
        view.progress(&progress("a", "running", 1, 1, 0));
        assert!(!view.progress(&progress("a", "running", 2, 1, 0)).menu);
        assert!(view.progress(&progress("a", "paused", 2, 1, 0)).menu);
        assert_eq!(view.ring(), Some(Ring { percent: 66, paused: true }));
        assert_eq!(view.progress(&progress("a", "completed", 3, 0, 0)), Change::default());
    }

    #[test]
    fn a_crawl_then_its_analysis_ends_as_a_crawl() {
        let mut view = LiveView::default();
        view.crawl(&change("a", "running"));
        view.set_name("a", "example.com".into());
        let finished = view.crawl(&change("a", "completed"));
        let ended = finished.ended.expect("ended");
        assert!(ended.crawled && !ended.was_analysing);
        assert_eq!(view.kinds(), LiveKinds::default());

        // The analysis follows at once: the name is remembered, so no lookup.
        let analysing = view.crawl(&change("a", "analyzing"));
        assert_eq!(analysing.look_up, None);
        assert_eq!(view.entries()[0].name, "example.com");
        assert_eq!(view.kinds(), LiveKinds { crawling: false, analysing: true });
        assert_eq!(view.ring(), None);

        let analysed = view.crawl(&change("a", "completed")).ended.expect("ended");
        assert!(analysed.crawled && analysed.was_analysing);
        view.finish("a");
        assert!(!view.was_crawled("a"));
    }

    #[test]
    fn an_analysis_on_its_own_is_looked_up_for_its_baseline() {
        let mut view = LiveView::default();
        view.set_name("a", "example.com".into());
        let change = view.crawl(&change("a", "analyzing"));
        assert_eq!(change.look_up.as_deref(), Some("a"));
        let ended = view.crawl(&CrawlChange {
            crawl_id: "a".into(),
            status: "stopped".into(),
            error: Some("Disk full".into()),
        });
        let ended = ended.ended.expect("ended");
        assert!(!ended.crawled && ended.was_analysing);
        assert_eq!(ended.error.as_deref(), Some("Disk full"));
    }

    #[test]
    fn the_end_of_a_crawl_never_seen_live_tells_nothing() {
        let mut view = LiveView::default();
        assert_eq!(view.crawl(&change("x", "completed")), Change::default());
    }

    #[test]
    fn hello_keeps_listed_crawls_and_analyses_and_drops_the_rest() {
        let mut view = LiveView::default();
        view.crawl(&change("gone", "running"));
        view.crawl(&change("kept", "paused"));
        view.crawl(&change("analysing", "analyzing"));
        let change = view.hello(&["kept".into(), "new".into()]);
        assert!(change.menu);
        assert_eq!(change.look_up.as_deref(), Some("new"));
        let ids: Vec<_> = view.entries().into_iter().map(|entry| entry.id).collect();
        assert_eq!(ids, ["kept", "analysing", "new"]);
    }

    #[test]
    fn resync_takes_the_librarys_statuses_and_names() {
        let mut view = LiveView::default();
        view.crawl(&change("a", "running"));
        view.crawl(&change("b", "analyzing"));
        let items = [
            item("a", Some("example.com"), "paused"),
            item("b", Some("other.org"), "completed"),
            item("c", None, "analyzing"),
        ];
        let change = view.resync(&items);
        assert!(change.menu);
        let entries = view.entries();
        assert_eq!(entries.len(), 2);
        assert_eq!((entries[0].name.as_str(), entries[0].status), ("example.com", LiveStatus::Paused));
        assert_eq!((entries[1].id.as_str(), entries[1].status), ("c", LiveStatus::Analyzing));
        assert_eq!(view.resync(&items), Change::default());
    }

    #[test]
    fn the_ring_counts_crawling_crawls_before_paused_ones() {
        let mut view = LiveView::default();
        view.progress(&progress("a", "running", 50, 50, 0));
        view.progress(&progress("b", "paused", 0, 100, 0));
        view.crawl(&change("c", "analyzing"));
        assert_eq!(view.ring(), Some(Ring { percent: 50, paused: false }));
        view.progress(&progress("a", "stopping", 75, 0, 25));
        assert_eq!(view.ring(), Some(Ring { percent: 75, paused: false }));
        view.crawl(&change("a", "stopped"));
        assert_eq!(view.ring(), Some(Ring { percent: 0, paused: true }));
        view.crawl(&change("b", "stopped"));
        assert_eq!(view.ring(), None);
        view.crawl(&change("c", "completed"));
        assert_eq!(view.ring(), None);
    }

    #[test]
    fn nothing_known_is_zero_percent_not_a_division_by_zero() {
        let mut view = LiveView::default();
        view.crawl(&change("a", "running"));
        assert_eq!(view.ring(), Some(Ring { percent: 0, paused: false }));
    }

    #[test]
    fn kinds_tell_crawling_from_analysing() {
        let items = [item("a", None, "completed"), item("b", None, "analyzing")];
        assert_eq!(LiveKinds::of_items(&items), LiveKinds { crawling: false, analysing: true });
        let items = [item("a", None, "stopping"), item("b", None, "inUse")];
        assert_eq!(LiveKinds::of_items(&items), LiveKinds { crawling: true, analysing: false });
        assert!(!LiveKinds::of_items(&[]).any());
    }

    #[test]
    fn crawling_names_the_crawls_a_lost_sidecar_takes_with_it_but_not_analyses() {
        let mut view = LiveView::default();
        view.crawl(&change("running", "running"));
        view.crawl(&change("paused", "paused"));
        view.crawl(&change("stopping", "stopping"));
        view.crawl(&change("analysing", "analyzing"));
        assert_eq!(view.crawling().collect::<Vec<_>>(), ["running", "paused", "stopping"]);
        view.clear();
        assert_eq!(view.crawling().count(), 0);
    }

    #[test]
    fn clearing_forgets_the_live_crawls_but_keeps_names() {
        let mut view = LiveView::default();
        view.crawl(&change("a", "running"));
        view.set_name("a", "example.com".into());
        assert!(view.clear().menu);
        assert!(view.entries().is_empty());
        assert!(!view.was_crawled("a"));
        view.crawl(&change("a", "running"));
        assert_eq!(view.entries()[0].name, "example.com");
    }
}
