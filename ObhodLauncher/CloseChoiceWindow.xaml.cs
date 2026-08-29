using System.Windows;

namespace ZapretWPF
{
    public partial class CloseChoiceWindow : Window
    {
        public CloseChoiceWindow()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(
            object sender,
            RoutedEventArgs e)
        {
            /*
             * DialogResult = true означает:
             * пользователь выбрал закрытие приложения.
             */
            DialogResult = true;
        }

        private void BtnTray_Click(
            object sender,
            RoutedEventArgs e)
        {
            /*
             * DialogResult = false означает:
             * пользователь выбрал работу в трее.
             */
            DialogResult = false;
        }
    }
}