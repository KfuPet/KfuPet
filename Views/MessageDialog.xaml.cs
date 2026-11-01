using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KfuPet.Views
{
    /// <summary>通用提示弹窗的语义类型：决定图标与颜色。</summary>
    public enum MessageDialogKind
    {
        /// <summary>普通信息。</summary>
        Info,

        /// <summary>操作成功。</summary>
        Success,

        /// <summary>需要确认的警告。</summary>
        Warning,

        /// <summary>操作失败。</summary>
        Error,

        /// <summary>询问是否继续。</summary>
        Question
    }

    /// <summary>
    /// 通用提示 / 确认弹窗，统一使用应用的无边框圆角样式。
    /// 用非模态 Show() 打开（ShowDialog 会禁用同一线程上的所有窗口，导致桌宠无法拖动）。
    /// </summary>
    public partial class MessageDialog : Window
    {
        /// <summary>点击确认按钮后触发；取消或按 Esc 关闭不触发。</summary>
        public event Action? Confirmed;

        /// <summary>未点确认就关闭（取消按钮 / Esc / 其他关闭方式）时触发。</summary>
        public event Action? Canceled;

        private bool _confirmed;

        /// <summary>
        /// 创建提示弹窗。
        /// </summary>
        /// <param name="title">标题栏文字。</param>
        /// <param name="message">提示正文，支持换行。</param>
        /// <param name="kind">提示类型，决定图标与颜色。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="showCancel">是否显示取消按钮。</param>
        /// <param name="cancelText">取消按钮文字，默认“取消”。</param>
        public MessageDialog(
            string title,
            string message,
            MessageDialogKind kind,
            string confirmText,
            bool showCancel = false,
            string? cancelText = null)
        {
            InitializeComponent();

            Title = title;
            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmText.Text = confirmText;

            CancelButton.Content = cancelText ?? "取消";
            CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

            var (icon, color) = GetIcon(kind);
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            TitleIcon.Text = icon;
            TitleIcon.Foreground = brush;
            BodyIcon.Text = icon;
            BodyIcon.Foreground = brush;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        /// <summary>按提示类型返回图标字形与颜色。</summary>
        private static (string Icon, string Color) GetIcon(MessageDialogKind kind)
        {
            return kind switch
            {
                MessageDialogKind.Success => ("\uE73E", "#059669"),
                MessageDialogKind.Warning => ("\uE7BA", "#D97706"),
                MessageDialogKind.Error => ("\uE711", "#E81123"),
                MessageDialogKind.Question => ("\uE9CE", "#2F6BFF"),
                _ => ("\uE946", "#2F6BFF")
            };
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            _confirmed = true;
            // 先关闭当前弹窗，再触发确认回调，避免回调里再打开弹窗时出现叠层
            Close();
            Confirmed?.Invoke();
        }

        /// <summary>关闭时若没有点确认，按取消处理。</summary>
        protected override void OnClosed(EventArgs e)
        {
            if (!_confirmed)
            {
                Canceled?.Invoke();
            }

            base.OnClosed(e);
        }
    }
}
