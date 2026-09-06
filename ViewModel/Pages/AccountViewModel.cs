using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using WinUIMusicPlayer.Services.Account;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.ViewModel.Pages
{
    /// <summary>账号页 ViewModel: 登录/注册/资料/云同步状态与命令。</summary>
    public partial class AccountViewModel : ObservableObject
    {
        private readonly AuthService _auth;
        private readonly AccountCloudSyncService _cloudSync;

        public AuthService Auth => _auth;
        public AccountCloudSyncService CloudSync => _cloudSync;

        public bool IsLoggedIn => _auth.IsLoggedIn;
        public AuthUser? User => _auth.CurrentUser;
        public string AccountId => _auth.AccountId;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        // ─── 登录表单 ─────────────────────────────────
        [ObservableProperty]
        private string _loginId = string.Empty;
        [ObservableProperty]
        private string _loginPassword = string.Empty;
        [ObservableProperty]
        private string _loginError = string.Empty;
        [ObservableProperty]
        private bool _isLoginBusy;

        // ─── 注册表单 ─────────────────────────────────
        [ObservableProperty]
        private string _regId = string.Empty;
        [ObservableProperty]
        private string _regNickname = string.Empty;
        [ObservableProperty]
        private string _regPassword = string.Empty;
        [ObservableProperty]
        private string _regConfirm = string.Empty;
        [ObservableProperty]
        private string _regEmail = string.Empty;
        [ObservableProperty]
        private string _regCode = string.Empty;
        [ObservableProperty]
        private string _regError = string.Empty;
        [ObservableProperty]
        private bool _isRegisterBusy;
        [ObservableProperty]
        private int _codeCountdown;
        [ObservableProperty]
        private bool _isSendingCode;

        private System.Threading.Timer? _codeTimer;

        // ─── 资料视图 ─────────────────────────────────
        [ObservableProperty]
        private string _avatarStatus = "none"; // none / pending / rejected
        [ObservableProperty]
        private bool _isAvatarUploading;
        [ObservableProperty]
        private bool _isProfileBusy;

        // ─── 云同步 ─────────────────────────────────
        [ObservableProperty]
        private bool _isCloudSyncing;
        [ObservableProperty]
        private bool _cloudSyncEnabled;
        [ObservableProperty]
        private CloudSyncFrequency _cloudSyncFrequency = CloudSyncFrequency.ThirtyMinutes;
        [ObservableProperty]
        private string _lastSyncText = string.Empty;

        /// <summary>登录成功后触发(供页面弹出云同步询问/刷新 UI)。</summary>
        public event Action? LoginSucceeded;
        /// <summary>登录态变化(登录/退出)。</summary>
        public event Action? LoginChanged;

        public AccountViewModel()
        {
            _auth = App.Services.GetRequiredService<AuthService>();
            _cloudSync = App.Services.GetRequiredService<AccountCloudSyncService>();
            _auth.LoginStateChanged += OnLoginStateChanged;
        }

        private void OnLoginStateChanged()
        {
            OnPropertyChanged(nameof(IsLoggedIn));
            OnPropertyChanged(nameof(User));
            OnPropertyChanged(nameof(AccountId));
            LoginChanged?.Invoke();
        }

        public void ShowStatus(string message)
        {
            StatusMessage = message;
        }

        public void ClearStatus() => StatusMessage = string.Empty;

        // ─── 登录/注册 ─────────────────────────────────

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (IsLoginBusy) return;
            LoginError = string.Empty;
            if (string.IsNullOrWhiteSpace(LoginId) || string.IsNullOrWhiteSpace(LoginPassword))
            {
                LoginError = ToolUtils.GetString("AccountFormIncomplete");
                return;
            }
            IsLoginBusy = true;
            try
            {
                await _auth.LoginAsync(LoginId, LoginPassword);
                LoginPassword = string.Empty;
                _ = LoadAvatarStatusAsync();
                OnLoginStateChanged();
                LoginSucceeded?.Invoke();
            }
            catch (AuthException ex)
            {
                LoginError = ex.Message;
            }
            catch (Exception ex)
            {
                LoginError = ToolUtils.GetString("AccountLoginFailed") + ": " + ex.Message;
            }
            finally
            {
                IsLoginBusy = false;
            }
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            if (IsRegisterBusy) return;
            RegError = string.Empty;
            if (string.IsNullOrWhiteSpace(RegId) || string.IsNullOrWhiteSpace(RegPassword) ||
                string.IsNullOrWhiteSpace(RegEmail) || string.IsNullOrWhiteSpace(RegCode))
            {
                RegError = ToolUtils.GetString("AccountFormIncomplete");
                return;
            }
            if (RegPassword != RegConfirm)
            {
                RegError = ToolUtils.GetString("AccountPasswordMismatch");
                return;
            }
            IsRegisterBusy = true;
            try
            {
                var notice = await _auth.RegisterAsync(RegId, RegNickname, RegPassword, RegEmail, RegCode, CaptchaPayload);
                if (!string.IsNullOrEmpty(notice)) ShowStatus(notice);
                RegPassword = RegConfirm = string.Empty;
                _ = LoadAvatarStatusAsync();
                OnLoginStateChanged();
                LoginSucceeded?.Invoke();
            }
            catch (AuthException ex)
            {
                RegError = ex.Message;
            }
            catch (Exception ex)
            {
                RegError = ToolUtils.GetString("AccountRegisterFailed") + ": " + ex.Message;
            }
            finally
            {
                IsRegisterBusy = false;
            }
        }

        /// <summary>注册流程的人机验证结果(由页面弹窗写入)。</summary>
        public HumanCaptchaPayload? CaptchaPayload { get; set; }

        [RelayCommand]
        private async Task SendCodeAsync()
        {
            if (IsSendingCode || CodeCountdown > 0) return;
            if (string.IsNullOrWhiteSpace(RegEmail) || !RegEmail.Contains('@'))
            {
                RegError = ToolUtils.GetString("AccountEmailInvalid");
                return;
            }
            RegError = string.Empty;
            IsSendingCode = true;
            try
            {
                await _auth.SendVerifyCodeAsync(RegEmail, "register", CaptchaPayload, RegId);
                ShowStatus(ToolUtils.GetString("AccountCodeSent"));
                StartCountdown();
            }
            catch (AuthException ex)
            {
                RegError = ex.Message;
            }
            catch (Exception ex)
            {
                RegError = ToolUtils.GetString("AccountCodeSendFailed") + ": " + ex.Message;
            }
            finally
            {
                IsSendingCode = false;
            }
        }

        private void StartCountdown()
        {
            CodeCountdown = 60;
            _codeTimer?.Dispose();
            _codeTimer = new System.Threading.Timer(_ =>
            {
                if (CodeCountdown <= 0)
                {
                    _codeTimer?.Dispose();
                    _codeTimer = null;
                    return;
                }
                // PropertyChanged 必须在 UI 线程触发
                App.MainWindow?.DispatcherQueue.TryEnqueue(() => CodeCountdown--);
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        // ─── 资料相关 ─────────────────────────────────

        /// <summary>进入账号页/手动刷新时拉取最新资料并启动自动上传。</summary>
        public async Task SyncProfileAsync()
        {
            if (!_auth.IsLoggedIn) return;
            if (IsProfileBusy) return;
            IsProfileBusy = true;
            try
            {
                await _auth.RefreshProfileAsync();
            }
            catch
            {
                // 网络暂不可用时保留本地资料
            }
            finally
            {
                IsProfileBusy = false;
            }
            await LoadAvatarStatusAsync();
            await Task.Run(() => _cloudSync.StartAutoUpload());
        }

        public async Task LoadAvatarStatusAsync()
        {
            if (!_auth.IsLoggedIn) return;
            AvatarStatus = await _auth.GetAvatarStatusAsync();
        }

        /// <summary>登录后云同步询问流程(已启用则直接同步)。</summary>
        public async Task HandleCloudSyncAfterLoginAsync()
        {
            var accountId = _auth.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            if (_cloudSync.IsEnabled(accountId))
            {
                await RunCloudSyncAsync();
                _cloudSync.StartAutoUpload();
                return;
            }
            if (_cloudSync.HasPrompted(accountId)) return;
            // 由页面弹出 CloudSyncPromptDialog, 通过 OnCloudSyncPrompted 回调结果
        }

        /// <summary>云同步询问弹窗返回结果后的处理。</summary>
        public async Task OnCloudSyncPromptResultAsync(bool enabled)
        {
            var accountId = _auth.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            _cloudSync.MarkPrompted(accountId);
            _cloudSync.SetEnabled(accountId, enabled);
            CloudSyncEnabled = enabled;
            if (enabled)
            {
                await RunCloudSyncAsync();
                _cloudSync.StartAutoUpload();
            }
        }

        [RelayCommand]
        private async Task RunCloudSyncAsync()
        {
            if (IsCloudSyncing) return;
            var accountId = _auth.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            IsCloudSyncing = true;
            try
            {
                var (result, error) = await _cloudSync.SyncAllAsync(manual: true);
                if (error is not null)
                {
                    ShowStatus(error);
                }
                else if (result is not null)
                {
                    var suffix = result.PluginErrors.Count == 0
                        ? string.Empty
                        : string.Format(ToolUtils.GetString("AccountSyncPluginErrorSuffix"), result.PluginErrors.Count);
                    ShowStatus(result.NoChange
                        ? ToolUtils.GetString("AccountSyncNoChange") + suffix
                        : string.Format(ToolUtils.GetString("AccountSyncDone"),
                            result.DownloadedPlugins, result.UploadedPlugins,
                            result.UploadedPlaylists, result.DownloadedPlaylists) + suffix);
                }
                RefreshSyncInfo(accountId);
            }
            finally
            {
                IsCloudSyncing = false;
            }
        }

        /// <summary>从持久化状态刷新云同步开关/频率/最近同步时间显示。</summary>
        public void RefreshSyncInfo(string? accountId = null)
        {
            accountId ??= _auth.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            CloudSyncEnabled = _cloudSync.IsEnabled(accountId);
            CloudSyncFrequency = _cloudSync.GetFrequency(accountId);
            var last = _cloudSync.GetLastManualSync(accountId);
            LastSyncText = last is null
                ? ToolUtils.GetString("AccountNeverSynced")
                : string.Format(ToolUtils.GetString("AccountLastSyncFormat"), last.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }

        /// <summary>云同步开关切换。</summary>
        public async Task SetCloudSyncEnabledAsync(bool enabled)
        {
            var accountId = _auth.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            _cloudSync.SetEnabled(accountId, enabled);
            CloudSyncEnabled = enabled;
            if (enabled)
            {
                _cloudSync.StartAutoUpload();
            }
            else
            {
                _cloudSync.StopAutoUpload();
            }
        }

        /// <summary>云同步频率切换。</summary>
        public void SetCloudSyncFrequency(CloudSyncFrequency frequency)
        {
            var accountId = _auth.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            _cloudSync.SetFrequency(accountId, frequency);
            CloudSyncFrequency = frequency;
            if (CloudSyncEnabled) _cloudSync.StartAutoUpload();
        }

        [RelayCommand]
        private async Task RefreshProfileAsync()
        {
            if (IsProfileBusy) return;
            await SyncProfileAsync();
            ShowStatus(ToolUtils.GetString("AccountProfileRefreshed"));
        }

        public void Logout()
        {
            _cloudSync.StopAutoUpload();
            _auth.Logout();
            AvatarStatus = "none";
            OnLoginStateChanged();
        }
    }
}
