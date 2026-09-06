using CommunityToolkit.WinUI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinUIMusicPlayer.Helper;
using WinUIMusicPlayer.Services.Account;
using WinUIMusicPlayer.Utils;
using WinUIMusicPlayer.ViewModel.Pages;

namespace WinUIMusicPlayer.View
{
    /// <summary>账号页: 未登录展示登录/注册, 已登录展示个人资料与云同步设置。</summary>
    public sealed partial class AccountPage : Page
    {
        private bool _frequencyUpdating;

        public AccountViewModel ViewModel { get; }

        public AccountPage()
        {
            ViewModel = App.Services.GetRequiredService<AccountViewModel>();
            InitializeComponent();
            DataContext = this;
            NavigationCacheMode = NavigationCacheMode.Disabled;
            ViewModel.LoginChanged += OnLoginChanged;
            ViewModel.LoginSucceeded += OnLoginSucceeded;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _ = Task.Run(() => ViewModel.SyncProfileAsync());
        }

        private void AccountPage_Loaded(object sender, RoutedEventArgs e) => InitializeTexts();

        private void InitializeTexts()
        {
            TitleText.Text = ViewModel.IsLoggedIn ? ToolUtils.GetString("AccountMyTitle") : ToolUtils.GetString("AccountTitle");
            BrandSubtitleText.Text = ToolUtils.GetString("AccountBrandSubtitle");
            LoginTabButton.Content = ToolUtils.GetString("AccountLoginTab");
            RegisterTabButton.Content = ToolUtils.GetString("AccountRegisterTab");

            LoginIdLabel.Text = ToolUtils.GetString("AccountIdLabel");
            LoginPasswordLabel.Text = ToolUtils.GetString("AccountPasswordLabel");
            LoginButtonText.Text = ToolUtils.GetString("AccountLoginButton");
            ForgotPasswordLink.Content = ToolUtils.GetString("AccountForgotPassword");

            RegIdLabel.Text = ToolUtils.GetString("AccountIdLabel");
            RegNicknameLabel.Text = ToolUtils.GetString("AccountNicknameLabel");
            RegPasswordLabel.Text = ToolUtils.GetString("AccountPasswordLabel");
            RegConfirmLabel.Text = ToolUtils.GetString("AccountConfirmPasswordLabel");
            RegEmailLabel.Text = ToolUtils.GetString("AccountEmailLabel");
            RegCodeLabel.Text = ToolUtils.GetString("AccountCodeLabel");
            SendCodeButtonText.Text = ToolUtils.GetString("AccountSendCode");
            RegisterButtonText.Text = ToolUtils.GetString("AccountRegisterButton");

            AvatarHintText.Text = ToolUtils.GetString("AccountAvatarHint");
            UserIdText.Text = ViewModel.IsLoggedIn ? string.Format(ToolUtils.GetString("AccountIdFormat"), ViewModel.AccountId) : string.Empty;
            RefreshProfileText.Text = ToolUtils.GetString("AccountRefreshProfile");
            EditNicknameText.Text = ToolUtils.GetString("AccountEditNickname");
            ChangePasswordText.Text = ToolUtils.GetString("AccountChangePassword");
            LogoutText.Text = ToolUtils.GetString("AccountLogoutButton");

            CloudSyncTitleText.Text = ToolUtils.GetString("AccountCloudSyncTitle");
            CloudSyncEnableText.Text = ToolUtils.GetString("AccountCloudSyncEnable");
            CloudSyncFrequencyText.Text = ToolUtils.GetString("AccountCloudSyncFrequency");
            SyncNowText.Text = ToolUtils.GetString("AccountSyncNow");

            UpdateAvatarStatusText();
            UpdateSendCodeButton();
            RefreshFrequencyCombo();
            ViewModel.RefreshSyncInfo();
        }

        // ─── 状态联动 ─────────────────────────────────

        private void OnLoginChanged()
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                TitleText.Text = ViewModel.IsLoggedIn ? ToolUtils.GetString("AccountMyTitle") : ToolUtils.GetString("AccountTitle");
                UserIdText.Text = ViewModel.IsLoggedIn ? string.Format(ToolUtils.GetString("AccountIdFormat"), ViewModel.AccountId) : string.Empty;
                await UpdateAvatarAsync();
            });
        }

        private async void OnLoginSucceeded()
        {
            await DispatcherQueue.EnqueueAsync(async () =>
            {
                await UpdateAvatarAsync();
                ViewModel.RefreshSyncInfo();
                await HandleCloudSyncAfterLoginAsync();
            });
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccountViewModel.CodeCountdown))
                DispatcherQueue.TryEnqueue(UpdateSendCodeButton);
            else if (e.PropertyName == nameof(AccountViewModel.AvatarStatus))
                DispatcherQueue.TryEnqueue(UpdateAvatarStatusText);
        }

        private void UpdateSendCodeButton()
        {
            if (ViewModel.CodeCountdown > 0)
            {
                SendCodeButtonText.Text = string.Format(ToolUtils.GetString("AccountCodeCountdown"), ViewModel.CodeCountdown);
            }
            else
            {
                SendCodeButtonText.Text = ToolUtils.GetString("AccountSendCode");
            }
        }

        private void UpdateAvatarStatusText()
        {
            var status = ViewModel.AvatarStatus;
            if (status == "pending")
                AvatarHintText.Text = ToolUtils.GetString("AccountAvatarPending");
            else if (status == "rejected")
                AvatarHintText.Text = ToolUtils.GetString("AccountAvatarRejected");
            else
                AvatarHintText.Text = ToolUtils.GetString("AccountAvatarHint");
        }

        private async Task UpdateAvatarAsync()
        {
            var avatar = ViewModel.User?.Avatar;
            if (string.IsNullOrWhiteSpace(avatar))
            {
                AvatarEllipse.Visibility = Visibility.Collapsed;
                AvatarPlaceholderIcon.Visibility = Visibility.Visible;
                return;
            }
            var source = await AvatarToImageAsync(avatar);
            if (source is null)
            {
                AvatarEllipse.Visibility = Visibility.Collapsed;
                AvatarPlaceholderIcon.Visibility = Visibility.Visible;
                return;
            }
            AvatarBrush.ImageSource = source;
            AvatarEllipse.Visibility = Visibility.Visible;
            AvatarPlaceholderIcon.Visibility = Visibility.Collapsed;
        }

        /// <summary>头像 data URI(base64)或 URL → ImageSource。</summary>
        private static async Task<ImageSource?> AvatarToImageAsync(string avatar)
        {
            try
            {
                if (avatar.StartsWith("data:image/", StringComparison.Ordinal))
                {
                    var comma = avatar.IndexOf(',');
                    if (comma <= 0 || comma >= avatar.Length - 1) return null;
                    var bytes = Convert.FromBase64String(avatar[(comma + 1)..]);
                    using var ms = new InMemoryRandomAccessStream();
                    await ms.WriteAsync(bytes.AsBuffer());
                    ms.Seek(0);
                    var bmp = new BitmapImage();
                    await bmp.SetSourceAsync(ms);
                    return bmp;
                }
                if (Uri.TryCreate(avatar, UriKind.Absolute, out var uri))
                    return new BitmapImage { UriSource = uri };
            }
            catch { }
            return null;
        }

        // ─── Tab 切换 ─────────────────────────────────

        private void LoginTabButton_Checked(object sender, RoutedEventArgs e)
        {
            if (LoginForm is null) return;
            LoginForm.Visibility = Visibility.Visible;
            RegisterForm.Visibility = Visibility.Collapsed;
        }

        private void RegisterTabButton_Checked(object sender, RoutedEventArgs e)
        {
            if (LoginForm is null) return;
            LoginForm.Visibility = Visibility.Collapsed;
            RegisterForm.Visibility = Visibility.Visible;
        }

        // ─── 登录/注册/验证码 ─────────────────────────────────

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LoginPassword = LoginPasswordBox.Password;
            _ = ViewModel.LoginCommand.ExecuteAsync(null);
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RegisterWithCaptchaAsync();
        }

        private async Task RegisterWithCaptchaAsync()
        {
            ViewModel.RegPassword = RegPasswordBox.Password;
            ViewModel.RegConfirm = RegConfirmBox.Password;
            // 注册前先过人机验证
            var captcha = await ShowCaptchaAsync(
                ToolUtils.GetString("AccountRegisterCaptchaTitle"),
                ToolUtils.GetString("AccountRegisterCaptchaDesc"));
            if (captcha is null) return;
            ViewModel.CaptchaPayload = captcha;
            await ViewModel.RegisterCommand.ExecuteAsync(null);
        }

        private void SendCode_Click(object sender, RoutedEventArgs e)
        {
            _ = SendCodeWithCaptchaAsync();
        }

        private async Task SendCodeWithCaptchaAsync()
        {
            if (ViewModel.CodeCountdown > 0) return;
            if (string.IsNullOrWhiteSpace(ViewModel.RegEmail) || !ViewModel.RegEmail.Contains('@'))
            {
                ViewModel.RegError = ToolUtils.GetString("AccountEmailInvalid");
                return;
            }
            var captcha = await ShowCaptchaAsync(
                ToolUtils.GetString("AccountSendCodeCaptchaTitle"),
                ToolUtils.GetString("AccountSendCodeCaptchaDesc"));
            if (captcha is null) return;
            ViewModel.CaptchaPayload = captcha;
            await ViewModel.SendCodeCommand.ExecuteAsync(null);
        }

        private async Task<HumanCaptchaPayload?> ShowCaptchaAsync(string title, string description)
        {
            var dialog = new SubView.HumanCaptchaDialog(ViewModel.Auth, title, description);
            await dialog.ShowThemedAsync(XamlRoot);
            return dialog.Result;
        }

        // ─── 忘记密码 ─────────────────────────────────

        private async void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            var emailBox = new TextBox { PlaceholderText = ToolUtils.GetString("AccountForgotEmailLabel") };
            var emailDialog = new ContentDialog
            {
                Title = ToolUtils.GetString("AccountForgotTitle"),
                Content = emailBox,
                PrimaryButtonText = ToolUtils.GetString("AccountForgotSendCode"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await emailDialog.ShowThemedAsync(XamlRoot) != ContentDialogResult.Primary) return;
            var email = emailBox.Text.Trim();
            if (!email.Contains('@'))
            {
                ViewModel.ShowStatus(ToolUtils.GetString("AccountEmailInvalid"));
                return;
            }

            var captcha = await ShowCaptchaAsync(
                ToolUtils.GetString("AccountSendCodeCaptchaTitle"),
                ToolUtils.GetString("AccountForgotCaptchaDesc"));
            if (captcha is null) return;
            try
            {
                await ViewModel.Auth.SendVerifyCodeAsync(email, "reset_password", captcha);
                ViewModel.ShowStatus(ToolUtils.GetString("AccountCodeSent"));
            }
            catch (Exception ex)
            {
                ViewModel.ShowStatus(ex.Message);
                return;
            }

            // 设置新密码
            var codeBox = new TextBox { PlaceholderText = ToolUtils.GetString("AccountForgotCodeLabel") };
            var pwdBox = new PasswordBox { PlaceholderText = ToolUtils.GetString("AccountForgotNewPassword") };
            var confirmBox = new PasswordBox { PlaceholderText = ToolUtils.GetString("AccountForgotConfirmPassword") };
            var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
            panel.Children.Add(codeBox);
            panel.Children.Add(pwdBox);
            panel.Children.Add(confirmBox);
            var resetDialog = new ContentDialog
            {
                Title = ToolUtils.GetString("AccountForgotResetTitle"),
                Content = panel,
                PrimaryButtonText = ToolUtils.GetString("AccountForgotResetButton"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await resetDialog.ShowThemedAsync(XamlRoot) != ContentDialogResult.Primary) return;
            if (pwdBox.Password != confirmBox.Password)
            {
                ViewModel.ShowStatus(ToolUtils.GetString("AccountPasswordMismatch"));
                return;
            }

            var resetCaptcha = await ShowCaptchaAsync(
                ToolUtils.GetString("AccountResetCaptchaTitle"),
                ToolUtils.GetString("AccountResetCaptchaDesc"));
            if (resetCaptcha is null) return;
            try
            {
                var message = await ViewModel.Auth.ResetPasswordAsync(email, codeBox.Text, pwdBox.Password, resetCaptcha);
                ViewModel.ShowStatus(message);
            }
            catch (Exception ex)
            {
                ViewModel.ShowStatus(ex.Message);
            }
        }

        // ─── 资料操作 ─────────────────────────────────

        private async void Avatar_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsAvatarUploading) return;
            try
            {
                var picker = new FileOpenPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(
                    picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".bmp");
                picker.FileTypeFilter.Add(".webp");
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                var bytes = (await FileIO.ReadBufferAsync(file)).ToArray();
                if (bytes.Length > 5 * 1024 * 1024)
                {
                    ViewModel.ShowStatus(ToolUtils.GetString("AccountAvatarTooBig"));
                    return;
                }
                ViewModel.IsAvatarUploading = true;
                try
                {
                    var message = await ViewModel.Auth.UploadAvatarAsync(bytes);
                    ViewModel.ShowStatus(message);
                    await UpdateAvatarAsync();
                    await ViewModel.LoadAvatarStatusAsync();
                }
                catch (AuthException ex)
                {
                    await ViewModel.LoadAvatarStatusAsync();
                    ViewModel.ShowStatus(ex.Message);
                }
                finally
                {
                    ViewModel.IsAvatarUploading = false;
                }
            }
            catch (Exception ex)
            {
                ViewModel.ShowStatus(ToolUtils.GetString("AccountAvatarUploadFailed") + ": " + ex.Message);
            }
        }

        private void RefreshProfile_Click(object sender, RoutedEventArgs e)
            => _ = ViewModel.RefreshProfileCommand.ExecuteAsync(null);

        private async void EditNickname_Click(object sender, RoutedEventArgs e)
        {
            var input = new TextBox { Text = ViewModel.User?.Nickname ?? string.Empty };
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("AccountNicknameDialogTitle"),
                Content = input,
                PrimaryButtonText = ToolUtils.GetString("AccountNicknameDialogSubmit"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowThemedAsync(XamlRoot) != ContentDialogResult.Primary) return;
            var nickname = input.Text.Trim();
            if (string.IsNullOrEmpty(nickname)) return;
            try
            {
                var message = await ViewModel.Auth.UpdateNicknameAsync(nickname);
                ViewModel.ShowStatus(message);
            }
            catch (Exception ex)
            {
                ViewModel.ShowStatus(ex.Message);
            }
        }

        private async void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            var oldBox = new PasswordBox { PlaceholderText = ToolUtils.GetString("AccountOldPassword") };
            var newBox = new PasswordBox { PlaceholderText = ToolUtils.GetString("AccountNewPassword") };
            var confirmBox = new PasswordBox { PlaceholderText = ToolUtils.GetString("AccountNewPasswordConfirm") };
            var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
            panel.Children.Add(oldBox);
            panel.Children.Add(newBox);
            panel.Children.Add(confirmBox);
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("AccountChangePasswordTitle"),
                Content = panel,
                PrimaryButtonText = ToolUtils.GetString("AccountChangePasswordButton"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowThemedAsync(XamlRoot) != ContentDialogResult.Primary) return;
            if (newBox.Password != confirmBox.Password)
            {
                ViewModel.ShowStatus(ToolUtils.GetString("AccountPasswordMismatch"));
                return;
            }
            try
            {
                var message = await ViewModel.Auth.ChangePasswordAsync(oldBox.Password, newBox.Password);
                ViewModel.ShowStatus(message);
            }
            catch (Exception ex)
            {
                ViewModel.ShowStatus(ex.Message);
            }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = ToolUtils.GetString("AccountLogoutConfirmTitle"),
                Content = ToolUtils.GetString("AccountLogoutConfirmText"),
                PrimaryButtonText = ToolUtils.GetString("AccountLogoutConfirmButton"),
                CloseButtonText = ToolUtils.GetString("TextCancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowThemedAsync(XamlRoot) != ContentDialogResult.Primary) return;
            ViewModel.Logout();
            await UpdateAvatarAsync();
        }

        // ─── 云同步 ─────────────────────────────────

        private async Task HandleCloudSyncAfterLoginAsync()
        {
            var accountId = ViewModel.AccountId;
            if (string.IsNullOrEmpty(accountId)) return;
            if (ViewModel.CloudSync.IsEnabled(accountId))
            {
                await ViewModel.RunCloudSyncCommand.ExecuteAsync(null);
                ViewModel.CloudSync.StartAutoUpload();
                return;
            }
            if (ViewModel.CloudSync.HasPrompted(accountId)) return;
            var prompt = new SubView.CloudSyncPromptDialog();
            await prompt.ShowThemedAsync(XamlRoot);
            var enabled = prompt.EnableResult == true;
            await ViewModel.OnCloudSyncPromptResultAsync(enabled);
            RefreshFrequencyCombo();
        }

        private async void CloudSyncToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (FrequencyComboBox is null) return;
            await ViewModel.SetCloudSyncEnabledAsync(((ToggleSwitch)sender).IsOn);
        }

        private void RefreshFrequencyCombo()
        {
            if (FrequencyComboBox is null) return;
            _frequencyUpdating = true;
            FrequencyComboBox.Items.Clear();
            foreach (var freq in Enum.GetValues<CloudSyncFrequency>())
                FrequencyComboBox.Items.Add(FrequencyLabel(freq));
            FrequencyComboBox.SelectedIndex = (int)ViewModel.CloudSyncFrequency;
            _frequencyUpdating = false;
        }

        private static string FrequencyLabel(CloudSyncFrequency freq) => freq switch
        {
            CloudSyncFrequency.FiveMinutes => ToolUtils.GetString("AccountFreqFiveMinutes"),
            CloudSyncFrequency.FifteenMinutes => ToolUtils.GetString("AccountFreqFifteenMinutes"),
            CloudSyncFrequency.ThirtyMinutes => ToolUtils.GetString("AccountFreqThirtyMinutes"),
            CloudSyncFrequency.OneHour => ToolUtils.GetString("AccountFreqOneHour"),
            CloudSyncFrequency.SixHours => ToolUtils.GetString("AccountFreqSixHours"),
            CloudSyncFrequency.TwelveHours => ToolUtils.GetString("AccountFreqTwelveHours"),
            CloudSyncFrequency.OneDay => ToolUtils.GetString("AccountFreqOneDay"),
            _ => ToolUtils.GetString("AccountFreqManual"),
        };

        private void FrequencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_frequencyUpdating || FrequencyComboBox.SelectedIndex < 0) return;
            ViewModel.SetCloudSyncFrequency((CloudSyncFrequency)FrequencyComboBox.SelectedIndex);
        }

        private void SyncNow_Click(object sender, RoutedEventArgs e)
            => _ = ViewModel.RunCloudSyncCommand.ExecuteAsync(null);
    }
}
