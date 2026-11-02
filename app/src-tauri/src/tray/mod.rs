//! The tray icon, owned by Rust: it is there while a crawl or an analysis runs, or while the window is hidden (so a
//! hidden window can always be reopened). It shows progress in a ring and its tooltip, and its menu pauses and
//! resumes crawls without the window. One thread applies every change, so the main thread never waits on it.

pub mod ring;
pub mod text;

use std::sync::atomic::Ordering;
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::thread;
use std::time::{Duration, Instant};

use tauri::image::Image;
use tauri::menu::{IsMenuItem, Menu, MenuEvent, MenuItem, PredefinedMenuItem, Submenu};
use tauri::tray::{MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Manager, Wry};

use crate::live::api::CrawlAction;
use crate::live::model::{LiveStatus, Ring, TrayEntry};
use crate::shell::Shell;
use crate::sync::lock;
use crate::{quit, window};

const TRAY_ID: &str = "main";
const OPEN: &str = "open";
const QUIT: &str = "quit";
const PAUSE_PREFIX: &str = "pause:";
const RESUME_PREFIX: &str = "resume:";

/// The ring and the tooltip change at most this often.
const REDRAW_INTERVAL: Duration = Duration::from_secs(1);

/// The app icon, for the plain tray icon and the logo inside the ring, decoded at compile time: the app reads no
/// image at run time, so it needs no PNG decoder (nor the webview a command to read one from a path).
const ICON_32: Image<'static> = tauri::include_image!("icons/32x32.png");
const ICON_64: Image<'static> = tauri::include_image!("icons/64x64.png");

enum Message {
    /// The live crawls or the window changed; `menu` when the menu may need rebuilding.
    Changed { menu: bool },
    /// The app is quitting: the icon goes.
    Quit,
}

/// Sends changes to the tray's thread. Cheap to clone, and never blocks.
#[derive(Clone)]
pub struct TrayHandle(Sender<Message>);

impl TrayHandle {
    pub fn start(app: AppHandle) -> Self {
        let (sender, receiver) = mpsc::channel();
        let started = thread::Builder::new().name("tray".to_owned()).spawn(move || Tray::new(app).run(&receiver));
        if let Err(error) = started {
            log::error!("Couldn't start the tray's thread: {error}");
        }
        Self(sender)
    }

    pub fn changed(&self, menu: bool) {
        let _ = self.0.send(Message::Changed { menu });
    }

    pub fn quit(&self) {
        let _ = self.0.send(Message::Quit);
    }
}

/// What the icon shows, compared before anything is redrawn.
#[derive(Clone, PartialEq, Eq)]
struct Look {
    tooltip: String,
    ring: Option<Ring>,
}

struct Tray {
    app: AppHandle,
    tray: Option<TrayIcon<Wry>>,
    shown: bool,
    quitting: bool,
    /// The crawls the menu was built for: (id, name, status).
    menu: Option<Vec<(String, String, LiveStatus)>>,
    look: Option<Look>,
    drawn_at: Option<Instant>,
    /// A redraw held back by the interval, due at this time.
    due: Option<Instant>,
}

impl Tray {
    fn new(app: AppHandle) -> Self {
        Self { app, tray: None, shown: false, quitting: false, menu: None, look: None, drawn_at: None, due: None }
    }

    fn run(mut self, receiver: &Receiver<Message>) {
        loop {
            let message = match self.due {
                Some(due) => match receiver.recv_timeout(due.saturating_duration_since(Instant::now())) {
                    Ok(message) => Some(message),
                    Err(RecvTimeoutError::Timeout) => None,
                    Err(RecvTimeoutError::Disconnected) => return,
                },
                None => match receiver.recv() {
                    Ok(message) => Some(message),
                    Err(_) => return,
                },
            };
            let mut menu = false;
            // Everything queued meanwhile is applied in one go.
            for message in message.into_iter().chain(receiver.try_iter()) {
                match message {
                    Message::Changed { menu: rebuild } => menu |= rebuild,
                    Message::Quit => self.quitting = true,
                }
            }
            self.sync(menu);
        }
    }

    fn sync(&mut self, rebuild: bool) {
        let Some(shell) = self.app.try_state::<Shell>() else { return };
        let (entries, ring) = {
            let view = lock(&shell.live);
            (view.entries(), view.ring())
        };
        let hidden = shell.window_hidden.load(Ordering::SeqCst);
        let wanted = !self.quitting && (!entries.is_empty() || hidden);
        if !wanted {
            self.due = None;
            if self.shown {
                if let Some(tray) = &self.tray {
                    if let Err(error) = tray.set_visible(false) {
                        log::warn!("Couldn't remove the tray icon: {error}");
                    }
                }
                self.shown = false;
                log::info!("Tray icon removed");
            }
            return;
        }

        let look = Look { tooltip: text::tooltip(&entries), ring };
        let menu_key: Vec<_> =
            entries.iter().map(|entry| (entry.id.clone(), entry.name.clone(), entry.status)).collect();
        if self.tray.is_none() {
            self.build(&entries, &look);
            self.menu = Some(menu_key);
            return;
        }
        if rebuild || self.menu.as_ref() != Some(&menu_key) {
            self.set_menu(&entries);
            self.menu = Some(menu_key);
        }
        if self.look.as_ref() != Some(&look) {
            let now = Instant::now();
            match self.drawn_at.map(|at| at + REDRAW_INTERVAL).filter(|next| *next > now) {
                Some(next) => self.due = Some(next),
                None => self.redraw(look),
            }
        } else {
            self.due = None;
        }
        if !self.shown {
            if let Some(tray) = &self.tray {
                if let Err(error) = tray.set_visible(true) {
                    log::warn!("Couldn't show the tray icon: {error}");
                }
            }
            self.shown = true;
            log::info!("Tray icon shown");
        }
    }

    fn build(&mut self, entries: &[TrayEntry], look: &Look) {
        let menu = match build_menu(&self.app, entries) {
            Ok(menu) => menu,
            Err(error) => {
                log::error!("Couldn't build the tray menu: {error}");
                return;
            }
        };
        let mut builder = TrayIconBuilder::with_id(TRAY_ID)
            .tooltip(&look.tooltip)
            .menu(&menu)
            .show_menu_on_left_click(false)
            .on_tray_icon_event(on_tray_event)
            .on_menu_event(on_menu_event);
        if let Some(icon) = self.image(look.ring) {
            builder = builder.icon(icon);
        }
        match builder.build(&self.app) {
            Ok(tray) => {
                self.tray = Some(tray);
                self.shown = true;
                self.look = Some(look.clone());
                self.drawn_at = Some(Instant::now());
                self.due = None;
                log::info!("Tray icon shown");
            }
            Err(error) => log::error!("Couldn't create the tray icon: {error}"),
        }
    }

    fn set_menu(&self, entries: &[TrayEntry]) {
        let Some(tray) = &self.tray else { return };
        match build_menu(&self.app, entries) {
            Ok(menu) => {
                if let Err(error) = tray.set_menu(Some(menu)) {
                    log::warn!("Couldn't update the tray menu: {error}");
                }
            }
            Err(error) => log::warn!("Couldn't build the tray menu: {error}"),
        }
    }

    fn redraw(&mut self, look: Look) {
        let Some(tray) = &self.tray else { return };
        let previous = self.look.as_ref();
        if previous.map(|previous| &previous.tooltip) != Some(&look.tooltip) {
            if let Err(error) = tray.set_tooltip(Some(&look.tooltip)) {
                log::warn!("Couldn't update the tray tooltip: {error}");
            }
        }
        if previous.map(|previous| previous.ring) != Some(look.ring) {
            if let Err(error) = tray.set_icon(self.image(look.ring)) {
                log::warn!("Couldn't update the tray icon: {error}");
            }
        }
        self.look = Some(look);
        self.drawn_at = Some(Instant::now());
        self.due = None;
    }

    fn image(&self, ring: Option<Ring>) -> Option<Image<'static>> {
        Some(match ring {
            Some(ring) => {
                let rgba = ring::draw(ICON_64.rgba(), ICON_64.width() as usize, ring.percent, ring.paused);
                Image::new_owned(rgba, ring::SIZE as u32, ring::SIZE as u32)
            }
            None => ICON_32,
        })
    }
}

/// "Open Shouting Iguana"; each live crawl, with Pause and Resume as its status allows (an analysis can't be
/// controlled, so it shows disabled); "Quit".
fn build_menu(app: &AppHandle, entries: &[TrayEntry]) -> tauri::Result<Menu<Wry>> {
    let open = MenuItem::with_id(app, OPEN, "Open Shouting Iguana", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, QUIT, "Quit", true, None::<&str>)?;
    let mut crawls: Vec<Box<dyn IsMenuItem<Wry>>> = Vec::with_capacity(entries.len());
    for entry in entries {
        let label = text::menu_label(entry);
        if entry.status == LiveStatus::Analyzing {
            crawls.push(Box::new(MenuItem::with_id(
                app,
                format!("analysing:{}", entry.id),
                label,
                false,
                None::<&str>,
            )?));
            continue;
        }
        let pause = MenuItem::with_id(
            app,
            format!("{PAUSE_PREFIX}{}", entry.id),
            "Pause",
            entry.status == LiveStatus::Running,
            None::<&str>,
        )?;
        let resume = MenuItem::with_id(
            app,
            format!("{RESUME_PREFIX}{}", entry.id),
            "Resume",
            entry.status == LiveStatus::Paused,
            None::<&str>,
        )?;
        crawls.push(Box::new(Submenu::with_id_and_items(
            app,
            format!("crawl:{}", entry.id),
            label,
            true,
            &[&pause, &resume],
        )?));
    }
    let menu = Menu::with_items(app, &[&open])?;
    if !crawls.is_empty() {
        menu.append(&PredefinedMenuItem::separator(app)?)?;
        for crawl in &crawls {
            menu.append(crawl.as_ref())?;
        }
    }
    menu.append(&PredefinedMenuItem::separator(app)?)?;
    menu.append(&quit)?;
    Ok(menu)
}

fn on_tray_event(tray: &TrayIcon<Wry>, event: TrayIconEvent) {
    match event {
        TrayIconEvent::Click { button: MouseButton::Left, button_state: MouseButtonState::Up, .. }
        | TrayIconEvent::DoubleClick { button: MouseButton::Left, .. } => window::show(tray.app_handle()),
        _ => {}
    }
}

fn on_menu_event(app: &AppHandle, event: MenuEvent) {
    let id = event.id().as_ref();
    match id {
        OPEN => window::show(app),
        QUIT => quit::request(app),
        _ => {
            let action = if let Some(crawl) = id.strip_prefix(PAUSE_PREFIX) {
                Some((crawl, CrawlAction::Pause))
            } else {
                id.strip_prefix(RESUME_PREFIX).map(|crawl| (crawl, CrawlAction::Resume))
            };
            if let Some((crawl, action)) = action {
                control(app, crawl.to_owned(), action);
            }
        }
    }
}

/// Pauses or resumes a crawl from the menu, off the main thread.
fn control(app: &AppHandle, crawl: String, action: CrawlAction) {
    let Some(shell) = app.try_state::<Shell>() else { return };
    let Some(connection) = shell.sidecar.link().current() else { return };
    let api = shell.api.clone();
    let spawned = thread::Builder::new().name("tray-control".to_owned()).spawn(move || {
        if let Err(error) = api.control(&connection, &crawl, action) {
            // A crawl that ended as the menu was used says it isn't running here: nothing to do.
            log::info!("Couldn't {action:?} crawl {crawl} from the tray: {error}");
        }
    });
    if let Err(error) = spawned {
        log::warn!("Couldn't start a thread to control a crawl: {error}");
    }
}
