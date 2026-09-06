using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using WinUIMusicPlayer.Utils;

namespace WinUIMusicPlayer.View.SubView
{
    /// <summary>登录后云同步询问弹窗: 等待 3 秒后才可选择(与手机版一致)。</summary>
    public sealed partial class CloudSyncPromptDialog : ContentDialog
    {
        private readonly DispatcherQueueTimer _timer;
        private int _remaining = 3;

        /// <summary>true=开启云同步; null/关闭=暂不上传。</summary>
        public bool? EnableResult { get; private set; }

        public CloudSyncPromptDialog()
        {
            InitializeComponent();
            Title = ToolUtils.GetString("AccountCloudSyncPromptTitle");
            Line1Text.Text = ToolUtils.GetString("AccountCloudSyncPromptLine1");
            Line2Text.Text = ToolUtils.GetString("AccountCloudSyncPromptLine2");
            PrimaryButtonText = ToolUtils.GetString("AccountCloudSyncPromptEnable");
            CloseButtonText = ToolUtils.GetString("AccountCloudSyncPromptLater");
            DefaultButton = ContentDialogButton.Primary;
            UpdateCountdown();
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) =>
            {
                _remaining--;
                UpdateCountdown();
                if (_remaining <= 0) _timer.Stop();
            };
            Loaded += (_, _) => _timer.Start();
            Unloaded += (_, _) => _timer.Stop();
        }

        private void UpdateCountdown()
        {
            var enabled = _remaining <= 0;
            IsPrimaryButtonEnabled = enabled;
            CountdownText.Text = enabled
                ? string.Empty
                : string.Format(ToolUtils.GetString("AccountCloudSyncPromptWait"), _remaining);
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            => EnableResult = true;
    }
}
