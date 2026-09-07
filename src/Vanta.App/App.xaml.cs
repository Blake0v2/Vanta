using System.Diagnostics;
using System.Windows;

namespace Vanta;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Vanta.AutoClicker.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsCaptureRun() && IsAnotherVantaProcessRunning())
        {
            MessageBox.Show(
                "Vanta is already running. Close the open copy before starting another one.",
                "Vanta",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        if (!IsCaptureRun())
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
            if (!_ownsSingleInstanceMutex)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static bool IsCaptureRun() => Environment.GetCommandLineArgs()
        .Any(argument => argument.StartsWith("--capture-ui=", StringComparison.OrdinalIgnoreCase));

    private static bool IsAnotherVantaProcessRunning()
    {
        var processes = Process.GetProcessesByName("Vanta");
        try
        {
            return processes.Any(process => process.Id != Environment.ProcessId);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
