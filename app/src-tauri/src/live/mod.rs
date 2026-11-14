//! The shell's own line to the engine: a thread that follows `GET /api/events` and keeps the view of the live crawls
//! the tray and the quit question use. It never depends on the webview, so both work with the window hidden.

pub mod api;
pub mod model;
pub mod notify;
pub mod sse;

use std::collections::{HashMap, HashSet};
use std::io::{self, Read};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use tauri::{AppHandle, Manager};

use crate::shell::Shell;
use crate::sidecar::{Connection, Link, Next};
use crate::sync::lock;
use crate::tray::TrayHandle;
use crate::window;
use crate::{profile, settings};
use api::Api;
use model::{Change, CrawlItem, Ended, Event, LiveStatus, LiveView};
use notify::Words;
use sse::SseParser;

const MIN_BACKOFF: Duration = Duration::from_millis(250);
const MAX_BACKOFF: Duration = Duration::from_secs(4);

/// Starts following the engine: it waits for the first sidecar to be ready, then follows every one after it.
pub fn start(app: AppHandle, shell: &Shell) {
    let follower = Follower {
        app,
        link: Arc::clone(shell.sidecar.link()),
        view: Arc::clone(&shell.live),
        api: Arc::clone(&shell.api),
        tray: shell.tray.clone(),
        told: HashMap::new(),
        baselines: HashMap::new(),
        lost: HashSet::new(),
        exports_told: HashSet::new(),
    };
    if let Err(error) = thread::Builder::new().name("live-events".to_owned()).spawn(move || follower.run()) {
        log::error!("Couldn't start the thread that follows the engine's events: {error}");
    }
}

struct Follower {
    app: AppHandle,
    link: Arc<Link>,
    view: Arc<Mutex<LiveView>>,
    api: Arc<Api>,
    tray: TrayHandle,
    /// The ending last told for each crawl, so one is never told twice.
    told: HashMap<String, (String, Option<String>, Option<String>)>,
    /// When each crawl had last been analysed as an analysis of it began, to tell a cancelled one.
    baselines: HashMap<String, Option<String>>,
    /// Crawls that were crawling when their sidecar went: told about once the next one says they were interrupted.
    lost: HashSet<String>,
    /// Exports whose ending has been told (or seen in front of the window): a stream that connects again replays an
    /// export's latest event, which must not be told twice.
    exports_told: HashSet<String>,
}

impl Follower {
    fn run(mut self) {
        let mut followed = None;
        let mut retry_at = None;
        let mut backoff = MIN_BACKOFF;
        loop {
            match self.link.next(followed, retry_at) {
                Next::Closed => break,
                Next::Gone => {
                    self.sidecar_lost();
                    (followed, retry_at, backoff) = (None, None, MIN_BACKOFF);
                }
                Next::Connected(connection) => {
                    if followed != Some(connection.generation) {
                        if followed.is_some() {
                            self.sidecar_lost();
                        }
                        followed = Some(connection.generation);
                        backoff = MIN_BACKOFF;
                    }
                    match self.follow(&connection) {
                        // It got as far as events, so the next drop starts the backoff afresh.
                        Ok(true) => backoff = MIN_BACKOFF,
                        Ok(false) => {}
                        Err(error) => log::debug!("The engine's event stream couldn't be read: {error}"),
                    }
                    retry_at = Some(Instant::now() + backoff);
                    backoff = (backoff * 2).min(MAX_BACKOFF);
                }
            }
        }
        lock(&self.view).clear();
        self.tray.changed(true);
    }

    /// The followed sidecar went: nothing it said holds any more. The crawls it was crawling are remembered, so the
    /// next one can say how they ended: an engine that crashed in the tray mustn't lose a crawl without a word.
    fn sidecar_lost(&mut self) {
        let change = {
            let mut view = lock(&self.view);
            self.lost.extend(view.crawling().map(str::to_owned));
            view.clear()
        };
        self.tray.changed(change.menu);
    }

    /// After a new sidecar's hello, with its library: tells each crawl the last sidecar took with it that is now
    /// interrupted, unless the window is in front (its banner and the crawl's status say so there).
    fn tell_interrupted(&mut self, items: &[CrawlItem]) {
        if self.lost.is_empty() {
            return;
        }
        let lost = std::mem::take(&mut self.lost);
        if self.app.try_state::<Shell>().is_none_or(|shell| shell.is_quitting()) {
            return;
        }
        let simple = self.preferences().simple;
        for (item, notice) in notify::interrupted(&lost, items, simple) {
            let key = notify::ending_key(item);
            if self.told.get(&item.id) == Some(&key) {
                continue;
            }
            self.told.insert(item.id.clone(), key);
            if window::is_in_front(&self.app) {
                log::debug!("No notification for \"{}\": the window is in front and says it itself", notice.title);
                continue;
            }
            notify::show(&self.app, &notice.title, &notice.body);
        }
    }

    /// The page's preferences as they are now: the mode the notices speak in, and the checks the user has hidden.
    fn preferences(&self) -> settings::Preferences {
        let stored =
            profile::settings_folder(&self.app).and_then(|folder| settings::read(&folder.join(settings::FILE_NAME)));
        settings::preferences(stored.as_ref())
    }

    /// The words a crawl's notice is in: Simple's carry its problems to fix, counted as What to fix counts them.
    fn words(&self, connection: &Connection, item: &CrawlItem) -> Words {
        let preferences = self.preferences();
        if !preferences.simple {
            return Words::Advanced;
        }
        let problems = item.issues.and_then(|issues| {
            if preferences.hidden_checks.is_empty() {
                return Some(issues.error + issues.warning);
            }
            self.api
                .problems(connection, &item.id, &preferences.hidden_checks)
                .inspect_err(|error| log::debug!("Couldn't count crawl {}'s problems: {error}", item.id))
                .ok()
        });
        Words::Simple { problems }
    }

    /// Reads the stream until it ends. True if any event arrived.
    fn follow(&mut self, connection: &Connection) -> Result<bool, String> {
        let mut reader = self.api.events(connection).map_err(|error| error.to_string())?;
        log::info!("Following the engine's events (generation {})", connection.generation);
        let mut parser = SseParser::default();
        let mut buffer = vec![0u8; 16 * 1024];
        let mut received = false;
        loop {
            let read = match reader.read(&mut buffer) {
                Ok(0) => return Ok(received),
                Ok(read) => read,
                Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
                Err(error) => {
                    log::debug!("The engine's event stream ended: {error}");
                    return Ok(received);
                }
            };
            for event in parser.push(&buffer[..read]) {
                received = true;
                self.handle(connection, &event.event, &event.data);
            }
            if !self.link.is_current(connection.generation) {
                return Ok(received);
            }
        }
    }

    fn handle(&mut self, connection: &Connection, name: &str, data: &str) {
        let event = match Event::parse(name, data) {
            Ok(event) => event,
            Err(error) => {
                log::debug!("Ignored a {name} event the shell can't read: {error}");
                return;
            }
        };
        match event {
            Event::Hello(hello) => {
                let change = {
                    let mut view = lock(&self.view);
                    view.exports_running(&hello.exports);
                    view.hello(&hello.live)
                };
                // Analyses aren't in hello, and names never are: the library brings the view up to date in one call.
                let change = match self.api.crawls(connection, None) {
                    Ok(items) => {
                        let resynced = lock(&self.view).resync(&items);
                        self.tell_interrupted(&items);
                        Change { menu: change.menu || resynced.menu, progress: true, ..Change::default() }
                    }
                    Err(error) => {
                        log::warn!("Couldn't read the library after connecting to the engine: {error}");
                        change
                    }
                };
                self.apply(connection, change);
            }
            Event::Progress(progress) => {
                let change = lock(&self.view).progress(&progress);
                self.apply(connection, change);
            }
            Event::Crawl(crawl) => {
                let change = lock(&self.view).crawl(&crawl);
                self.apply(connection, change);
            }
            Event::Export(job) => {
                lock(&self.view).export(&job);
                self.export_ended(&job);
            }
            Event::Other => {}
        }
    }

    fn apply(&mut self, connection: &Connection, change: Change) {
        let named = change.look_up.as_deref().is_some_and(|id| self.look_up(connection, id));
        if change.menu || change.progress || named {
            self.tray.changed(change.menu || named);
        }
        if let Some(ended) = change.ended {
            self.ended(connection, &ended);
        }
    }

    /// Reads a crawl's name and, as an analysis of it begins, when it was last analysed. True if the menu changes.
    fn look_up(&mut self, connection: &Connection, id: &str) -> bool {
        let item = match self.api.crawl(connection, id) {
            Ok(item) => item,
            Err(error) => {
                log::debug!("Couldn't look up crawl {id}: {error}");
                return false;
            }
        };
        let mut view = lock(&self.view);
        // Only while it is still analysing: one already done has its new time, which would read as cancelled.
        if item.status == "analyzing" && !view.was_crawled(id) {
            self.baselines.insert(id.to_owned(), item.analyzed_at.clone());
        }
        view.set_name(id, item.display_name())
    }

    /// An export that ended while the window was out of sight is told once: saved, or failed. The page's toast says it
    /// when the window is in front; one cancelled was the user's own doing.
    fn export_ended(&mut self, job: &model::ExportJob) {
        let Some(notice) = notify::export_notice(job) else { return };
        if !self.exports_told.insert(job.export_id.clone()) {
            return;
        }
        if self.app.try_state::<Shell>().is_none_or(|shell| shell.is_quitting()) {
            return;
        }
        if window::is_in_front(&self.app) {
            log::debug!("No notification for \"{}\": the window is in front and says it itself", notice.title);
            return;
        }
        notify::show(&self.app, &notice.title, &notice.body);
    }

    /// A crawl left the live ones: tell the user if the window can't, once its outcome is settled.
    fn ended(&mut self, connection: &Connection, ended: &Ended) {
        if self.app.try_state::<Shell>().is_none_or(|shell| shell.is_quitting()) {
            return;
        }
        let item = match self.api.crawl(connection, &ended.id) {
            Ok(item) => item,
            Err(error) => {
                log::debug!("Couldn't read how crawl {} ended: {error}", ended.id);
                return;
            }
        };
        // A completed crawl goes straight on to its analysis: its notice waits for the analysis to end.
        if LiveStatus::parse(&item.status).is_some() {
            return;
        }
        lock(&self.view).finish(&ended.id);
        let baseline = self.baselines.remove(&ended.id);
        let key = notify::ending_key(&item);
        if self.told.get(&ended.id) == Some(&key) {
            return;
        }
        let words = self.words(connection, &item);
        let Some(notice) = notify::notice(ended, &item, baseline.as_ref(), words) else { return };
        self.told.insert(ended.id.clone(), key);
        if window::is_in_front(&self.app) {
            log::debug!("No notification for \"{}\": the window is in front and says it itself", notice.title);
            return;
        }
        notify::show(&self.app, &notice.title, &notice.body);
    }
}
