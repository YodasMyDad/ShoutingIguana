//! What the webview may call, as tauri-build resolved it into `gen/schemas`: never a command that takes a path
//! without a scope to narrow it. The store plugin's commands did, so any script in the page could read, write and
//! truncate any file the user can (the shell review's H1). Each permission the capability grants is expanded, through
//! the plugins' ACL manifests, to the commands it allows.

use std::collections::BTreeSet;
use std::path::PathBuf;

use serde_json::Value;

/// The app's own commands, in the manifests: permissions without a plugin prefix.
const APP: &str = "__app-acl__";

/// Plugins whose every command works on paths.
const PATH_PLUGINS: &[&str] = &["fs", "store", "shell", "sql", "persisted-scope", "upload", "stronghold"];

/// Commands that take a path or read a file, and whether a scope can narrow them (to http(s) URLs, say).
const TAKES_A_PATH: &[(&str, &str, bool)] = &[
    ("core:image", "from_path", false),
    ("core:menu", "new", false),
    ("core:menu", "set_icon", false),
    ("core:tray", "new", false),
    ("core:tray", "set_icon", false),
    ("core:tray", "set_temp_dir_path", false),
    ("core:window", "set_icon", false),
    ("core:app", "set_dock_icon", false),
    ("opener", "open_path", true),
    ("opener", "open_url", true),
    ("opener", "reveal_item_in_dir", true),
];

/// Plugins whose commands have been read and found to take no path, apart from those listed above. A plugin new to
/// the capability fails until someone has checked it and added it here.
const REVIEWED_PLUGINS: &[&str] = &["core:event", "core:window", "core:webview", "dialog", "opener", APP];

#[derive(Debug, Clone, PartialEq, Eq, PartialOrd, Ord)]
struct Grant {
    plugin: String,
    command: String,
    scoped: bool,
}

fn schemas() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("gen").join("schemas")
}

fn read_json(name: &str) -> Value {
    let path = schemas().join(name);
    let text = std::fs::read_to_string(&path).unwrap_or_else(|error| panic!("{}: {error}", path.display()));
    serde_json::from_str(&text).unwrap_or_else(|error| panic!("{}: {error}", path.display()))
}

/// "core:window:allow-set-theme" is plugin "core:window", permission "allow-set-theme"; "allow-x" is the app's.
fn split(identifier: &str) -> (&str, &str) {
    identifier.rsplit_once(':').unwrap_or((APP, identifier))
}

fn has_scope(permission: &Value) -> bool {
    permission
        .get("scope")
        .and_then(|scope| scope.get("allow"))
        .and_then(Value::as_array)
        .is_some_and(|allow| !allow.is_empty())
}

/// Every command a permission (or set, or default) allows.
fn expand(manifests: &Value, identifier: &str, scoped: bool, grants: &mut BTreeSet<Grant>) {
    let (plugin, name) = split(identifier);
    let manifest =
        manifests.get(plugin).unwrap_or_else(|| panic!("no ACL manifest for plugin {plugin} ({identifier})"));
    // A set's members are named in full ("core:path:default") or within the plugin ("allow-new").
    let qualify =
        |entry: &str| if entry.contains(':') || plugin == APP { entry.to_owned() } else { format!("{plugin}:{entry}") };
    let members = if name == "default" {
        manifest.get("default_permission").and_then(|default| default.get("permissions"))
    } else {
        manifest.get("permission_sets").and_then(|sets| sets.get(name)).and_then(|set| set.get("permissions"))
    };
    if let Some(members) = members.and_then(Value::as_array) {
        for member in members {
            expand(manifests, &qualify(member.as_str().expect("a permission name")), scoped, grants);
        }
        return;
    }
    let permission = manifest
        .get("permissions")
        .and_then(|permissions| permissions.get(name))
        .unwrap_or_else(|| panic!("no permission {identifier} in the ACL manifests"));
    let allowed = permission.pointer("/commands/allow").and_then(Value::as_array).cloned().unwrap_or_default();
    for command in allowed {
        grants.insert(Grant {
            plugin: plugin.to_owned(),
            command: command.as_str().expect("a command name").to_owned(),
            scoped: scoped || has_scope(permission),
        });
    }
}

/// The commands a capability grants, with whether the capability narrows each with a scope.
fn granted(manifests: &Value, capability: &Value) -> BTreeSet<Grant> {
    let mut grants = BTreeSet::new();
    for permission in capability["permissions"].as_array().expect("a list of permissions") {
        let (identifier, scoped) = match permission {
            Value::String(identifier) => (identifier.as_str(), false),
            object => (
                object["identifier"].as_str().expect("a permission identifier"),
                object.get("allow").and_then(Value::as_array).is_some_and(|allow| !allow.is_empty()),
            ),
        };
        expand(manifests, identifier, scoped, &mut grants);
    }
    grants
}

/// Why each grant is unsafe for a page that renders crawled, attacker-written text.
fn violations(grants: &BTreeSet<Grant>) -> Vec<String> {
    let mut found = Vec::new();
    for grant in grants {
        let label = format!("{}|{}", grant.plugin, grant.command);
        if PATH_PLUGINS.contains(&grant.plugin.as_str()) {
            found.push(format!("{label}: the {} plugin works on any path", grant.plugin));
        } else if let Some((_, _, scopable)) =
            TAKES_A_PATH.iter().find(|(plugin, command, _)| *plugin == grant.plugin && *command == grant.command)
        {
            if !scopable || !grant.scoped {
                found.push(format!("{label}: takes a path, with no scope to narrow it"));
            }
        } else if !REVIEWED_PLUGINS.contains(&grant.plugin.as_str()) {
            found.push(format!(
                "{label}: the {} plugin hasn't been reviewed for paths (see REVIEWED_PLUGINS)",
                grant.plugin
            ));
        }
    }
    found
}

#[test]
fn the_webview_is_granted_no_command_that_takes_an_unscoped_path() {
    let manifests = read_json("acl-manifests.json");
    let capabilities = read_json("capabilities.json");
    let capabilities = capabilities.as_object().expect("capabilities by identifier");
    assert!(!capabilities.is_empty(), "no capabilities were generated");
    for (identifier, capability) in capabilities {
        let grants = granted(&manifests, capability);
        let found = violations(&grants);
        assert!(found.is_empty(), "capability {identifier} grants the webview:\n{}", found.join("\n"));
    }
}

#[test]
fn the_webview_still_gets_what_it_uses() {
    let manifests = read_json("acl-manifests.json");
    let capabilities = read_json("capabilities.json");
    let grants = granted(&manifests, &capabilities["default"]);
    let has =
        |plugin: &str, command: &str| grants.iter().any(|grant| grant.plugin == plugin && grant.command == command);
    for (plugin, command) in [
        ("core:event", "listen"),
        ("core:event", "unlisten"),
        ("core:window", "set_theme"),
        ("core:webview", "set_webview_zoom"),
        ("dialog", "open"),
        ("dialog", "save"),
        ("opener", "open_url"),
        (APP, "settings_read"),
        (APP, "settings_write"),
        (APP, "reveal_crawl_file"),
        (APP, "reveal_export_file"),
        (APP, "open_export_file"),
        (APP, "take_pending_open_files"),
    ] {
        assert!(has(plugin, command), "the webview needs {plugin}|{command}");
    }
}

/// The check itself catches what the review found: the store, and `core:default`'s image and tray commands.
#[test]
fn the_check_finds_the_reviews_grants() {
    let manifests = read_json("acl-manifests.json");
    let reviewed = serde_json::json!({ "permissions": ["core:default", "store:default", "opener:allow-reveal-item-in-dir", "opener:allow-open-url"] });
    let found = violations(&granted(&manifests, &reviewed));
    for expected in [
        "store|load:",
        "store|save:",
        "core:image|from_path:",
        "core:tray|new:",
        "opener|reveal_item_in_dir:",
        "opener|open_url:",
    ] {
        assert!(found.iter().any(|line| line.starts_with(expected)), "expected {expected} in {found:#?}");
    }
    let scoped = serde_json::json!({ "permissions": [{ "identifier": "opener:allow-open-url", "allow": [{ "url": "https://*" }] }] });
    assert!(violations(&granted(&manifests, &scoped)).is_empty());
}
