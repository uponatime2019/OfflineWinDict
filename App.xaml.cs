using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using OfflineWinDict.Helpers;

namespace OfflineWinDict
{
    public partial class App : Application
    {
        private Window? _window;
        public static Window? CurrentWindow => (Current as App)?._window;

        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try { AppLogger.LogException(e.Exception, "App.Unhandled"); } catch { }
            try { Debug.WriteLine($"[Unhandled] {e.Exception}"); } catch { }
            e.Handled = true;
        }

        private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            try { AppLogger.LogException(e.Exception, "App.UnobservedTask"); } catch { }
            e.SetObserved();
        }

        private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                try { AppLogger.LogException(ex, "App.Domain"); } catch { }
            }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try { AppLogger.LogAction("App_Launched"); } catch { }

            try
            {
                _window = new MainWindow();
                try { _window.Activate(); } catch (Exception ex) { AppLogger.LogException(ex, "Window.Activate"); }

                // Command-line launch-to-search: OfflineWinDict.exe <word>
                try
                {
                    var cmdArgs = Environment.GetCommandLineArgs();
                    if (cmdArgs is { Length: > 1 })
                    {
                        var word = string.Join(" ", cmdArgs.Skip(1)).Trim();
                        if (word.Length > 0)
                        {
                            MainWindow.OpenWordInHome(word, AppSession.DictionaryCode);
                        }
                    }
                }
                catch (Exception ex) { AppLogger.LogException(ex, "App.LaunchArgs"); }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "App.OnLaunched");
                try { Debug.WriteLine($"[App.OnLaunched] {ex}"); } catch { }
            }
        }
    }
}
