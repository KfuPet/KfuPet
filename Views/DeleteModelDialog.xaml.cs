using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KfuPet.Views
{
    /// <summary>
    /// 删除模型确认对话框：确认按钮默认可用，点击后回传删除请求。
    /// </summary>
    public partial class DeleteModelDialog : Window
    {
        /// <summary>确认删除后触发。</summary>
        public event Action? DeleteConfirmed;

        public DeleteModelDialog(string modelName)
        {
            InitializeComponent();

            SummaryText.Text = $"{modelName} 将被永久删除，无法恢复。";

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close();
                }
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

        /// <summary>最终确认：回传调用方执行删除，并关闭窗口。</summary>
        private void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteConfirmed?.Invoke();
            Close();
        }
    }
}
