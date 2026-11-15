//! Secrets the user gave (a proxy's password, a site login sent as a header), kept in the operating system's credential
//! store: Windows' Credential Manager, macOS's Keychain, the Secret Service on Linux. Never in a settings file or a
//! crawl file: the engine stores neither (docs/v2-sidecar-api.md, "Crawl options"), and the page keeps them only
//! while it runs (docs/v2-ui-architecture.md, "Secrets").
//!
//! The page names what the secrets are for, never where they go: `defaults` (the crawl defaults' proxy) or
//! `crawl:<id>` (what a crawl was started with, so Continue needn't ask again). Each is one entry, a small JSON object
//! the page reads back as it wrote it: `{"proxyUrl": "...", "headers": {"Authorization": "..."}}`.

use std::fmt;

use crate::profile;

const SERVICE: &str = "Shouting Iguana";

/// Windows keeps at most 2,560 bytes of a credential (CRED_MAX_CREDENTIAL_BLOB_SIZE): 1,200 bytes of UTF-8 stay under
/// it however they are stored, and are far more than a proxy address and a few headers need.
pub const MAX_BYTES: usize = 1_200;

#[derive(Debug, PartialEq, Eq)]
pub enum SecretsError {
    BadKey,
    TooLarge(usize),
    NotAnObject,
    Store(String),
}

impl fmt::Display for SecretsError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::BadKey => formatter.write_str("The shell keeps secrets for the crawl defaults and for crawls only."),
            Self::TooLarge(bytes) => write!(
                formatter,
                "The secrets are {bytes} bytes, more than the {MAX_BYTES} the credential store is given."
            ),
            Self::NotAnObject => formatter.write_str("The secrets aren't a JSON object."),
            Self::Store(error) => write!(formatter, "The system's credential store refused: {error}"),
        }
    }
}

/// `defaults`, or `crawl:` and a crawl's id (letters, digits, `-` and `_`, at most 64).
pub fn valid_key(key: &str) -> bool {
    if key == "defaults" {
        return true;
    }
    key.strip_prefix("crawl:").is_some_and(|id| {
        !id.is_empty()
            && id.len() <= 64
            && id.bytes().all(|byte| byte.is_ascii_alphanumeric() || byte == b'-' || byte == b'_')
    })
}

/// A test run of the built app keeps its secrets apart from the user's own.
fn service() -> String {
    match profile::test_profile() {
        Some(folder) => format!("{SERVICE} (test {})", folder.display()),
        None => SERVICE.to_owned(),
    }
}

fn entry(key: &str) -> Result<keyring::Entry, SecretsError> {
    if !valid_key(key) {
        return Err(SecretsError::BadKey);
    }
    keyring::Entry::new(&service(), key).map_err(|error| SecretsError::Store(error.to_string()))
}

/// The secrets kept for `key`, or None.
pub fn read(key: &str) -> Result<Option<String>, SecretsError> {
    match entry(key)?.get_password() {
        Ok(secrets) => Ok(Some(secrets)),
        Err(keyring::Error::NoEntry) => Ok(None),
        Err(error) => Err(SecretsError::Store(error.to_string())),
    }
}

/// Keeps `secrets` for `key`, replacing what was kept; None, or an empty object, forgets them.
pub fn write(key: &str, secrets: Option<&str>) -> Result<(), SecretsError> {
    let entry = entry(key)?;
    let kept = match secrets {
        Some(secrets) => checked(secrets)?,
        None => None,
    };
    match kept {
        Some(secrets) => entry.set_password(secrets).map_err(|error| SecretsError::Store(error.to_string())),
        None => match entry.delete_credential() {
            Ok(()) | Err(keyring::Error::NoEntry) => Ok(()),
            Err(error) => Err(SecretsError::Store(error.to_string())),
        },
    }
}

/// The secrets as they will be kept: a JSON object, within the size; None when there's nothing in it to keep.
fn checked(secrets: &str) -> Result<Option<&str>, SecretsError> {
    let bytes = secrets.len();
    if bytes > MAX_BYTES {
        return Err(SecretsError::TooLarge(bytes));
    }
    match serde_json::from_str::<serde_json::Value>(secrets) {
        Ok(serde_json::Value::Object(object)) if object.is_empty() => Ok(None),
        Ok(serde_json::Value::Object(_)) => Ok(Some(secrets)),
        _ => Err(SecretsError::NotAnObject),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_the_defaults_and_crawls_have_secrets() {
        assert!(valid_key("defaults"));
        assert!(valid_key("crawl:0123456789abcdef"));
        assert!(valid_key("crawl:a-b_c"));
        for key in
            ["", "crawl:", "crawl:../x", "crawl:a b", "settings", "Defaults", &format!("crawl:{}", "a".repeat(65))]
        {
            assert!(!valid_key(key), "{key}");
        }
    }

    #[test]
    fn only_an_object_within_the_size_is_kept() {
        assert_eq!(
            checked(r#"{"proxyUrl":"http://u:p@proxy:8080"}"#),
            Ok(Some(r#"{"proxyUrl":"http://u:p@proxy:8080"}"#))
        );
        assert_eq!(checked("{}"), Ok(None));
        assert_eq!(checked("[]"), Err(SecretsError::NotAnObject));
        assert_eq!(checked("password"), Err(SecretsError::NotAnObject));
        let large = format!(r#"{{"a":"{}"}}"#, "x".repeat(MAX_BYTES));
        assert!(matches!(checked(&large), Err(SecretsError::TooLarge(_))));
    }

    /// The real store, round trip: written, read back, replaced, forgotten. Its own key, so it never meets the user's.
    #[test]
    #[cfg(windows)]
    fn the_credential_store_keeps_and_forgets() {
        let key = format!("crawl:test-{}", std::process::id());
        write(&key, Some(r#"{"proxyUrl":"http://me:secret@proxy:3128"}"#)).unwrap();
        assert_eq!(read(&key).unwrap().as_deref(), Some(r#"{"proxyUrl":"http://me:secret@proxy:3128"}"#));
        write(&key, Some(r#"{"headers":{"Authorization":"Basic eDp5"}}"#)).unwrap();
        assert_eq!(read(&key).unwrap().as_deref(), Some(r#"{"headers":{"Authorization":"Basic eDp5"}}"#));
        write(&key, Some("{}")).unwrap();
        assert_eq!(read(&key).unwrap(), None);
        write(&key, None).unwrap();
        assert_eq!(read(&key).unwrap(), None);
    }
}
