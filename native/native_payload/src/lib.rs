//! Minimal native payload DLL.
//!
//! `DllMain` runs under the Windows **loader lock**, where almost nothing is safe
//! to do (no I/O, no stdio, nothing that might load another DLL or wait). So the
//! entry point does the absolute minimum: it spawns a worker thread and returns
//! immediately. The real work runs on that thread, off the loader lock.
//!
//! A DllMain payload cannot receive call arguments, so the injector leaves the
//! message in a per-PID sidecar file (`%TEMP%\reloaded_payload_msg_<pid>.txt`) that
//! we read and then delete. This works whether we were launched or attached to.

use std::ffi::c_void;
use std::fs::OpenOptions;
use std::io::Write;
use std::ptr;

type Bool = i32;
type Dword = u32;
type Handle = *mut c_void;

const DLL_PROCESS_ATTACH: Dword = 1;
const TRUE: Bool = 1;

const MESSAGE_FILE_PREFIX: &str = "reloaded_payload_msg_";
const LOG_FILE: &str = "reloaded_inject_demo.log";

extern "system" {
    fn DisableThreadLibraryCalls(h_lib_module: Handle) -> Bool;
    fn CreateThread(
        lp_thread_attributes: *mut c_void,
        dw_stack_size: usize,
        lp_start_address: Option<unsafe extern "system" fn(*mut c_void) -> Dword>,
        lp_parameter: *mut c_void,
        dw_creation_flags: Dword,
        lp_thread_id: *mut Dword,
    ) -> Handle;
    fn CloseHandle(h: Handle) -> Bool;
}

#[no_mangle]
pub extern "system" fn DllMain(module: Handle, reason: Dword, _reserved: *mut c_void) -> Bool {
    if reason == DLL_PROCESS_ATTACH {
        unsafe {
            // We don't care about thread attach/detach notifications.
            DisableThreadLibraryCalls(module);
            // Do the work on a new thread, NOT here under the loader lock.
            let thread = CreateThread(ptr::null_mut(), 0, Some(payload_main), ptr::null_mut(), 0, ptr::null_mut());
            if !thread.is_null() {
                CloseHandle(thread);
            }
        }
    }
    TRUE
}

/// Worker thread. Runs after the loader lock is released, so std I/O is safe here.
unsafe extern "system" fn payload_main(_param: *mut c_void) -> Dword {
    let pid = std::process::id();
    let message = read_message(pid);
    let line = format!("[NATIVE] payload loaded in PID {pid}: {message}\n");

    // Visible if the host process has a console.
    print!("{line}");

    // Reliable, headless-verifiable marker.
    let path = std::env::temp_dir().join(LOG_FILE);
    if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(path) {
        let _ = f.write_all(line.as_bytes());
    }

    0
}

/// Reads the message the injector left for this PID, then removes the file.
fn read_message(pid: u32) -> String {
    let path = std::env::temp_dir().join(format!("{MESSAGE_FILE_PREFIX}{pid}.txt"));
    let message = std::fs::read_to_string(&path).unwrap_or_else(|_| "(no message)".into());
    let _ = std::fs::remove_file(&path); // best-effort cleanup
    message
}
