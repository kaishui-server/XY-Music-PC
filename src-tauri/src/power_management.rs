#[cfg(target_os = "windows")]
mod platform {
    use std::sync::{mpsc, OnceLock};

    use windows_sys::Win32::System::Power::{
        SetThreadExecutionState, ES_CONTINUOUS, ES_SYSTEM_REQUIRED,
    };

    struct Request {
        enabled: bool,
        response: mpsc::SyncSender<Result<(), String>>,
    }

    static CONTROLLER: OnceLock<Result<mpsc::Sender<Request>, String>> = OnceLock::new();

    fn controller() -> Result<&'static mpsc::Sender<Request>, String> {
        CONTROLLER
            .get_or_init(|| {
                let (sender, receiver) = mpsc::channel::<Request>();
                std::thread::Builder::new()
                    .name("playback-sleep-prevention".to_string())
                    .spawn(move || {
                        while let Ok(request) = receiver.recv() {
                            let flags = if request.enabled {
                                ES_CONTINUOUS | ES_SYSTEM_REQUIRED
                            } else {
                                ES_CONTINUOUS
                            };
                            let previous = unsafe { SetThreadExecutionState(flags) };
                            let result = if previous == 0 {
                                Err(format!(
                                    "设置系统防休眠状态失败: {}",
                                    std::io::Error::last_os_error()
                                ))
                            } else {
                                Ok(())
                            };
                            let _ = request.response.send(result);
                        }

                        // 控制通道关闭时确保恢复系统默认休眠策略。
                        unsafe {
                            SetThreadExecutionState(ES_CONTINUOUS);
                        }
                    })
                    .map(|_| sender)
                    .map_err(|error| format!("创建防休眠控制线程失败: {error}"))
            })
            .as_ref()
            .map_err(Clone::clone)
    }

    pub(super) fn set_enabled(enabled: bool) -> Result<(), String> {
        let (response_sender, response_receiver) = mpsc::sync_channel(1);
        controller()?
            .send(Request {
                enabled,
                response: response_sender,
            })
            .map_err(|error| format!("发送防休眠状态失败: {error}"))?;
        response_receiver
            .recv()
            .map_err(|error| format!("接收防休眠状态失败: {error}"))?
    }
}

#[tauri::command]
pub(crate) fn set_playback_sleep_prevention(enabled: bool) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        platform::set_enabled(enabled)
    }

    #[cfg(not(target_os = "windows"))]
    {
        let _ = enabled;
        Ok(())
    }
}
