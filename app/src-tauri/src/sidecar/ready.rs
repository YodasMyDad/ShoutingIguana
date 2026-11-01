//! The sidecar's ready line, its one line on stdout: `{"event":"ready","port":N,"version":"…"}`.

use serde::Deserialize;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ReadyLine {
    pub port: u16,
    pub version: String,
}

#[derive(Deserialize)]
struct Line {
    event: Option<String>,
    port: Option<u64>,
    version: Option<String>,
}

/// The ready line's port and version, or None for any other line. A "token" it carries (a sidecar started without
/// one makes one up, for development) is ignored: the shell always passes its own.
pub fn parse(line: &str) -> Option<ReadyLine> {
    let line = line.trim().trim_start_matches('\u{feff}');
    let parsed: Line = serde_json::from_str(line).ok()?;
    if parsed.event.as_deref() != Some("ready") {
        return None;
    }
    let port = u16::try_from(parsed.port?).ok().filter(|port| *port != 0)?;
    Some(ReadyLine { port, version: parsed.version.unwrap_or_default() })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_port_and_version() {
        assert_eq!(
            parse(r#"{"event":"ready","port":53123,"version":"2.0.0-alpha.1"}"#),
            Some(ReadyLine { port: 53123, version: "2.0.0-alpha.1".into() })
        );
    }

    #[test]
    fn tolerates_whitespace_a_bom_a_token_and_a_missing_version() {
        assert_eq!(
            parse("\u{feff}{\"event\":\"ready\",\"port\":80,\"token\":\"abc\"}\r\n"),
            Some(ReadyLine { port: 80, version: String::new() })
        );
    }

    #[test]
    fn rejects_anything_else() {
        for line in [
            "",
            "ready",
            "not json",
            r#"{"event":"progress","port":53123}"#,
            r#"{"port":53123}"#,
            r#"{"event":"ready"}"#,
            r#"{"event":"ready","port":0}"#,
            r#"{"event":"ready","port":70000}"#,
            r#"{"event":"ready","port":-1}"#,
            r#"{"event":"ready","port":"53123"}"#,
            r#"["ready",53123]"#,
        ] {
            assert_eq!(parse(line), None, "{line}");
        }
    }
}
