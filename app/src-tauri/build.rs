// The shell's own commands get an allow and a deny permission each, so the webview may call only those its
// capability (capabilities/default.json) grants. Keep the list in step with `generate_handler!` in main.rs.
fn main() {
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(tauri_build::AppManifest::new().commands(&[
        "sidecar_state",
        "restart_sidecar",
        "request_quit",
        "shell_info",
        "take_pending_open_files",
        "settings_read",
        "settings_write",
        "reveal_crawl_file",
        "reveal_export_file",
        "open_export_file",
    ])))
    .expect("the Tauri build script failed");
}
