using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using WinUIMusicPlayer.Services.Account;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.View.SubView
{
    /// <summary>
    /// 人机验证弹窗(服务端算术题)。
    /// 加载题目 → 输入答案 → 预校验通过后以 Primary 结果关闭。
    /// </summary>
    public sealed partial class HumanCaptchaDialog : ContentDialog
    {
        private readonly AuthService _auth;
        private HumanCaptcha? _captcha;
        private int _refreshToken;

        /// <summary>验证通过后的载荷; 取消时为 null。</summary>
        public HumanCaptchaPayload? Result { get; private set; }

        public HumanCaptchaDialog(AuthService auth, string title, string description)
        {
            _auth = auth;
            InitializeComponent();
            Title = title;
            DescriptionText.Text = description;
            PrimaryButtonText = ToolUtils.GetString("AccountCaptchaConfirm");
            CloseButtonText = ToolUtils.GetString("TextCancel");
            RefreshButton.Content = ToolUtils.GetString("AccountCaptchaRefresh");
            ErrorText.Text = string.Empty;
            Loaded += (_, _) => _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            var token = ++_refreshToken;
            LoadingRing.IsActive = true;
            QuestionText.Text = string.Empty;
            ErrorText.Text = string.Empty;
            AnswerBox.Text = string.Empty;
            IsPrimaryButtonEnabled = false;
            try
            {
                var captcha = await _auth.FetchCaptchaAsync();
                if (token != _refreshToken) return;
                _captcha = captcha;
                QuestionText.Text = captcha.Question;
                IsPrimaryButtonEnabled = true;
            }
            catch (Exception ex)
            {
                if (token != _refreshToken) return;
                ErrorText.Text = ex.Message;
            }
            finally
            {
                if (token == _refreshToken) LoadingRing.IsActive = false;
            }
        }

        private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_captcha is null) return;
            var answer = AnswerBox.Text.Trim();
            if (string.IsNullOrEmpty(answer))
            {
                ErrorText.Text = ToolUtils.GetString("AccountCaptchaEmpty");
                args.Cancel = true;
                return;
            }
            // 预校验通过才算完成; 失败保留弹窗并刷新题目
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                var payload = new HumanCaptchaPayload(_captcha.CaptchaId, answer);
                await _auth.VerifyCaptchaAsync(payload);
                Result = payload;
                Hide();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
                _ = RefreshAsync();
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
            => _ = RefreshAsync();

        private void AnswerBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && IsPrimaryButtonEnabled)
                _ = SimulatePrimaryAsync();
        }

        private async Task SimulatePrimaryAsync()
        {
            if (_captcha is null) return;
            var answer = AnswerBox.Text.Trim();
            if (string.IsNullOrEmpty(answer)) return;
            try
            {
                var payload = new HumanCaptchaPayload(_captcha.CaptchaId, answer);
                await _auth.VerifyCaptchaAsync(payload);
                Result = payload;
                Hide();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
                _ = RefreshAsync();
            }
        }
    }
}
