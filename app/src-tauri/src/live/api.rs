//! The few sidecar calls the shell makes itself: the event stream, a crawl's library item, an export's file, pause or
//! resume from the tray, and a crawl's problems for Simple's notice. Plain HTTP to 127.0.0.1, never through a proxy,
//! with the bearer token and no Origin header.

use std::fmt;
use std::time::Duration;

use ureq::unversioned::resolver::DefaultResolver;
use ureq::unversioned::transport::{Buffers, ConnectionDetails, Connector, NextTimeout, TcpConnector, Transport};
use ureq::{Agent, BodyReader};

use std::collections::{HashMap, HashSet};

use serde::Deserialize;

use super::model::{CrawlItem, ExportJob};
use crate::sidecar::Connection;

/// The sidecar sends a comment every 15 s on an idle stream; this long without a byte and the stream is dead.
pub const STREAM_IDLE_TIMEOUT: Duration = Duration::from_secs(35);

/// A call that takes longer than this has hit a stuck sidecar.
const CALL_TIMEOUT: Duration = Duration::from_secs(5);

/// Quitting asks what is live, and won't keep the user waiting long for the answer.
pub const QUICK_CALL_TIMEOUT: Duration = Duration::from_secs(2);

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CrawlAction {
    Pause,
    Resume,
}

#[derive(Debug)]
pub enum ApiError {
    Http(ureq::Error),
    Json(serde_json::Error),
    BadCrawlId,
}

impl fmt::Display for ApiError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Http(error) => write!(formatter, "{error}"),
            Self::Json(error) => write!(formatter, "unexpected JSON: {error}"),
            Self::BadCrawlId => formatter.write_str("not a crawl id"),
        }
    }
}

impl From<ureq::Error> for ApiError {
    fn from(error: ureq::Error) -> Self {
        Self::Http(error)
    }
}

impl From<serde_json::Error> for ApiError {
    fn from(error: serde_json::Error) -> Self {
        Self::Json(error)
    }
}

pub struct Api {
    calls: Agent,
    stream: Agent,
}

impl Default for Api {
    fn default() -> Self {
        let calls = Agent::config_builder()
            .proxy(None)
            .max_redirects(0)
            .user_agent("shouting-iguana-shell")
            .timeout_global(Some(CALL_TIMEOUT))
            .build();
        let stream = Agent::config_builder()
            .proxy(None)
            .max_redirects(0)
            .user_agent("shouting-iguana-shell")
            .timeout_connect(Some(Duration::from_secs(2)))
            .timeout_recv_response(Some(CALL_TIMEOUT))
            .build();
        let stream_connector = ().chain(TcpConnector::default()).chain(IdleTimeout(STREAM_IDLE_TIMEOUT));
        Self {
            calls: Agent::new_with_config(calls),
            stream: Agent::with_parts(stream, stream_connector, DefaultResolver::default()),
        }
    }
}

impl Api {
    /// `GET /api/events`, read as it arrives.
    pub fn events(&self, connection: &Connection) -> Result<BodyReader<'static>, ApiError> {
        let response = self
            .stream
            .get(url(connection, "/api/events"))
            .header("Authorization", bearer(connection))
            .header("Accept", "text/event-stream")
            .call()?;
        Ok(response.into_body().into_reader())
    }

    /// `GET /api/crawls`: the whole library.
    pub fn crawls(&self, connection: &Connection, timeout: Option<Duration>) -> Result<Vec<CrawlItem>, ApiError> {
        let mut request = self.calls.get(url(connection, "/api/crawls")).header("Authorization", bearer(connection));
        if let Some(timeout) = timeout {
            request = request.config().timeout_global(Some(timeout)).build();
        }
        let body = request.call()?.body_mut().read_to_string()?;
        Ok(serde_json::from_str(&body)?)
    }

    /// `GET /api/crawls/{id}`.
    pub fn crawl(&self, connection: &Connection, id: &str) -> Result<CrawlItem, ApiError> {
        let path = format!("/api/crawls/{}", checked_id(id)?);
        let body = self
            .calls
            .get(url(connection, &path))
            .header("Authorization", bearer(connection))
            .call()?
            .body_mut()
            .read_to_string()?;
        Ok(serde_json::from_str(&body)?)
    }

    /// The engine's state folder, from `GET /api/health`: its log is in `logs` there.
    pub fn state_dir(&self, connection: &Connection) -> Result<String, ApiError> {
        let health: serde_json::Value = serde_json::from_str(&self.get(connection, "/api/health")?)?;
        Ok(health.get("stateDir").and_then(serde_json::Value::as_str).unwrap_or_default().to_owned())
    }

    /// `GET /api/exports/{id}`: one running, or one of the last 64 that ended.
    pub fn export(&self, connection: &Connection, id: &str) -> Result<ExportJob, ApiError> {
        let path = format!("/api/exports/{}", checked_id(id)?);
        Ok(serde_json::from_str(&self.get(connection, &path)?)?)
    }

    /// The crawl's problems as Simple counts them, from `GET /api/checks` and its `GET /api/crawls/{id}/analysis`: the
    /// checks with issues that are Errors or Warnings, less those the user has hidden (`hidden`).
    pub fn problems(&self, connection: &Connection, id: &str, hidden: &[String]) -> Result<u64, ApiError> {
        let checks: Catalog = serde_json::from_str(&self.get(connection, "/api/checks")?)?;
        let path = format!("/api/crawls/{}/analysis", checked_id(id)?);
        let analysis: AnalysisCounts = serde_json::from_str(&self.get(connection, &path)?)?;
        Ok(problems(&checks, &analysis, hidden))
    }

    fn get(&self, connection: &Connection, path: &str) -> Result<String, ApiError> {
        Ok(self
            .calls
            .get(url(connection, path))
            .header("Authorization", bearer(connection))
            .call()?
            .body_mut()
            .read_to_string()?)
    }

    /// `POST /api/crawls/{id}/pause` or `/resume`.
    pub fn control(&self, connection: &Connection, id: &str, action: CrawlAction) -> Result<(), ApiError> {
        let verb = match action {
            CrawlAction::Pause => "pause",
            CrawlAction::Resume => "resume",
        };
        let path = format!("/api/crawls/{}/{verb}", checked_id(id)?);
        self.calls.post(url(connection, &path)).header("Authorization", bearer(connection)).send_empty()?;
        Ok(())
    }
}

/// The catalog, as far as counting problems needs it: each check's severity.
#[derive(Deserialize)]
struct Catalog {
    checks: Vec<CatalogCheck>,
}

#[derive(Deserialize)]
struct CatalogCheck {
    id: String,
    severity: String,
}

/// An analysis, as far as counting problems needs it: what each check found, and which were skipped.
#[derive(Deserialize)]
struct AnalysisCounts {
    counts: Vec<CheckCount>,
    #[serde(default)]
    skipped: Vec<SkippedCheck>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct CheckCount {
    check_id: String,
    count: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SkippedCheck {
    check_id: String,
}

/// The checks that found something and are an Error or a Warning, as the page's check entries count them
/// (screens/issues/checkList.ts): a skipped check found nothing, and a hidden one isn't counted.
fn problems(checks: &Catalog, analysis: &AnalysisCounts, hidden: &[String]) -> u64 {
    let counts: HashMap<&str, u64> =
        analysis.counts.iter().map(|entry| (entry.check_id.as_str(), entry.count)).collect();
    let skipped: HashSet<&str> = analysis.skipped.iter().map(|entry| entry.check_id.as_str()).collect();
    let found = checks.checks.iter().filter(|check| {
        (check.severity == "error" || check.severity == "warning")
            && !skipped.contains(check.id.as_str())
            && !hidden.contains(&check.id)
            && counts.get(check.id.as_str()).is_some_and(|count| *count > 0)
    });
    found.count() as u64
}

fn url(connection: &Connection, path: &str) -> String {
    format!("http://127.0.0.1:{}{path}", connection.port)
}

fn bearer(connection: &Connection) -> String {
    format!("Bearer {}", connection.token)
}

/// Crawl ids are hex: anything else never goes into a URL.
fn checked_id(id: &str) -> Result<&str, ApiError> {
    let valid = !id.is_empty() && id.len() <= 64 && id.bytes().all(|byte| byte.is_ascii_alphanumeric());
    if valid {
        Ok(id)
    } else {
        Err(ApiError::BadCrawlId)
    }
}

/// Fails a read that waits longer than the limit for a byte, however long the response as a whole may take: an
/// event stream never ends, so ureq's whole-body timeouts can't tell a quiet stream from a dead one.
#[derive(Debug)]
struct IdleTimeout(Duration);

impl<In: Transport> Connector<In> for IdleTimeout {
    type Out = IdleTransport<In>;

    fn connect(&self, _: &ConnectionDetails, chained: Option<In>) -> Result<Option<Self::Out>, ureq::Error> {
        Ok(chained.map(|inner| IdleTransport { inner, idle: self.0 }))
    }
}

#[derive(Debug)]
struct IdleTransport<T> {
    inner: T,
    idle: Duration,
}

impl<T: Transport> Transport for IdleTransport<T> {
    fn buffers(&mut self) -> &mut dyn Buffers {
        self.inner.buffers()
    }

    fn transmit_output(&mut self, amount: usize, timeout: NextTimeout) -> Result<(), ureq::Error> {
        self.inner.transmit_output(amount, timeout)
    }

    fn await_input(&mut self, timeout: NextTimeout) -> Result<bool, ureq::Error> {
        let idle = self.idle.into();
        let timeout = if timeout.after > idle { NextTimeout { after: idle, reason: timeout.reason } } else { timeout };
        self.inner.await_input(timeout)
    }

    fn is_open(&mut self) -> bool {
        self.inner.is_open()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::{Read, Write};
    use std::net::TcpListener;
    use std::thread;
    use std::time::Instant;

    fn connection(port: u16) -> Connection {
        Connection { generation: 1, port, token: "t0ken".into() }
    }

    #[test]
    fn problems_are_the_errors_and_warnings_that_found_something_less_the_hidden_ones() {
        let checks: Catalog = serde_json::from_value(serde_json::json!({ "checks": [
            { "id": "broken-internal-links", "severity": "error" },
            { "id": "title-duplicate", "severity": "warning" },
            { "id": "slow-response", "severity": "warning" },
            { "id": "image-missing-alt", "severity": "notice" },
            { "id": "orphan-pages", "severity": "warning" },
            { "id": "no-https", "severity": "error" },
        ], "categories": [] }))
        .unwrap();
        let analysis: AnalysisCounts = serde_json::from_value(serde_json::json!({
            "counts": [
                { "checkId": "broken-internal-links", "count": 5 },
                { "checkId": "title-duplicate", "count": 6 },
                { "checkId": "slow-response", "count": 3 },
                { "checkId": "image-missing-alt", "count": 40 },
                { "checkId": "no-https", "count": 0 },
            ],
            "skipped": [{ "checkId": "orphan-pages", "reason": { "code": "no-sitemap" } }],
        }))
        .unwrap();
        assert_eq!(problems(&checks, &analysis, &[]), 3);
        assert_eq!(problems(&checks, &analysis, &["slow-response".to_owned()]), 2);
    }

    #[test]
    fn crawl_ids_are_checked_before_they_reach_a_url() {
        assert!(checked_id("0123456789abcdef").is_ok());
        for bad in ["", "../shutdown", "a/b", "a?b", "a b", "%2e"] {
            assert!(checked_id(bad).is_err(), "{bad}");
        }
    }

    /// A server that sends the headers and one event, then nothing: the idle timeout ends the read, though the
    /// response never ends.
    #[test]
    fn a_silent_stream_times_out_between_bytes() {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let server = thread::spawn(move || {
            let (mut socket, _) = listener.accept().unwrap();
            let mut request = [0u8; 4096];
            let read = socket.read(&mut request).unwrap();
            let request = String::from_utf8_lossy(&request[..read]).to_string();
            socket
                .write_all(b"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\n\r\n")
                .unwrap();
            let event = b"event: hello\ndata: {}\n\n";
            socket.write_all(format!("{:x}\r\n", event.len()).as_bytes()).unwrap();
            socket.write_all(event).unwrap();
            socket.write_all(b"\r\n").unwrap();
            thread::sleep(Duration::from_secs(3));
            request
        });

        let api = Api {
            stream: Agent::with_parts(
                Agent::config_builder().proxy(None).build(),
                ().chain(TcpConnector::default()).chain(IdleTimeout(Duration::from_millis(300))),
                DefaultResolver::default(),
            ),
            ..Api::default()
        };
        let mut reader = api.events(&connection(port)).unwrap();
        let mut buffer = [0u8; 256];
        let read = reader.read(&mut buffer).unwrap();
        assert_eq!(&buffer[..read], b"event: hello\ndata: {}\n\n");
        let started = Instant::now();
        assert!(reader.read(&mut buffer).is_err());
        let waited = started.elapsed();
        assert!(waited >= Duration::from_millis(250) && waited < Duration::from_secs(2), "{waited:?}");

        let request = server.join().unwrap();
        assert!(request.starts_with("GET /api/events HTTP/1.1\r\n"), "{request}");
        let lower = request.to_ascii_lowercase();
        assert!(lower.contains(&format!("\r\nhost: 127.0.0.1:{port}\r\n")), "{request}");
        assert!(lower.contains("\r\nauthorization: bearer t0ken\r\n"), "{request}");
        assert!(lower.contains("\r\naccept: text/event-stream\r\n"), "{request}");
        assert!(!lower.contains("\r\norigin:"), "{request}");
    }
}
