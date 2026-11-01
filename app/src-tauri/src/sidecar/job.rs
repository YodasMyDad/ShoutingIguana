//! The kernel-level backstop on Windows: the sidecar runs in a job that kills every process in it when the shell's
//! last handle to the job closes, which happens however the shell ends, even when it is killed.

use std::io;
use std::os::windows::io::AsRawHandle;
use std::process::Child;
use std::sync::OnceLock;

use windows_sys::Win32::Foundation::CloseHandle;
use windows_sys::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation, SetInformationJobObject,
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION,
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
};

/// The job's handle, as an address so it can sit in a static. It is never closed: the kernel closes it when the shell
/// ends, and that is what kills the sidecar.
static JOB: OnceLock<Result<usize, i32>> = OnceLock::new();

pub fn assign(child: &Child) -> io::Result<()> {
    let job = *JOB.get_or_init(create);
    let job = job.map_err(io::Error::from_raw_os_error)?;
    if unsafe { AssignProcessToJobObject(job as _, child.as_raw_handle()) } == 0 {
        return Err(io::Error::last_os_error());
    }
    Ok(())
}

fn create() -> Result<usize, i32> {
    unsafe {
        let job = CreateJobObjectW(std::ptr::null(), std::ptr::null());
        if job.is_null() {
            return Err(io::Error::last_os_error().raw_os_error().unwrap_or_default());
        }
        let mut limits: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
        // A sidecar that crashes exits at once rather than waiting behind an error report dialog nobody sees, so it
        // can be restarted.
        limits.BasicLimitInformation.LimitFlags =
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION;
        let set = SetInformationJobObject(
            job,
            JobObjectExtendedLimitInformation,
            std::ptr::from_ref(&limits).cast(),
            std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
        );
        if set == 0 {
            let error = io::Error::last_os_error().raw_os_error().unwrap_or_default();
            CloseHandle(job);
            return Err(error);
        }
        Ok(job as usize)
    }
}
