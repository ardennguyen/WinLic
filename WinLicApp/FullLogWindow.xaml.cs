using System;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using WinLicApp;

namespace WinLicApp
{
    public partial class FullLogWindow : Window
    {
        public FullLogWindow(string logContent, string title, string modeLabel)
        {
            InitializeComponent();
            
            TxtLog.Text = logContent;
            TxtTitle.Text = title;
            this.Title = title;
            TxtMode.Text = modeLabel;
            
            TxtBtnSave.Text = L.Get("FL_BTN_SAVE");
            TxtBtnCopy.Text = L.Get("FL_BTN_COPY");
            TxtBtnClose.Text = L.Get("FL_BTN_CLOSE");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Text Files (*.txt)|*.txt";
            sfd.FileName = $"WinLic_FullLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, TxtLog.Text, new UTF8Encoding(true));
                    ShowStatus(L.Get("FL_SAVED"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtLog.Text);
                ShowStatus(L.Get("FL_COPIED"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void ShowStatus(string msg)
        {
            TxtStatus.Text = msg;
            TxtStatus.Visibility = Visibility.Visible;
            await System.Threading.Tasks.Task.Delay(2000);
            TxtStatus.Visibility = Visibility.Hidden;
        }
    }
}
