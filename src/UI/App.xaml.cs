using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using SteamworkUploader.Services;

namespace SteamworkUploader;

public partial class App : System.Windows.Application
{
    private const int NetFramework48Release = 528040;
    private const string NetFrameworkDownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48";

    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamworkUploader",
        "startup-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        if (!HasRequiredFramework())
        {
            MessageBoxResult result = MessageBox.Show(
                "运行本程序需要 Microsoft .NET Framework 4.8 Runtime。\n\n是否打开微软官方下载页面？",
                "缺少必备运行环境",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(NetFrameworkDownloadUrl) { UseShellExecute = true });
            Shutdown(2);
            return;
        }

        base.OnStartup(e);
        MainWindow window = new();
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupError(e.Exception);
        MessageBox.Show(
            $"{LocalizationService.Get("UnexpectedError")}\n\n{e.Exception.Message}\n\n{StartupLogPath}",
            LocalizationService.Get("ErrorTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteStartupError(exception);
    }

    private static bool HasRequiredFramework()
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using RegistryKey? frameworkKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            return frameworkKey?.GetValue("Release") is int release && release >= NetFramework48Release;
        }
        catch (Exception exception)
        {
            WriteStartupError(exception);
            return true;
        }
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath)!);
            File.AppendAllText(StartupLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{exception}\r\n\r\n");
        }
        catch
        {
            // Error reporting must never replace the original startup failure.
        }
    }
}
