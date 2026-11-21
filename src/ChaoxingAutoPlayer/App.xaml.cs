using System.Windows;
using ChaoxingAutoPlayer.Services;

namespace ChaoxingAutoPlayer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Unhandled UI exception.", args.Exception);
            MessageBox.Show(
                $"发生未处理的错误：{args.Exception.Message}",
                "超星视频连播器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
