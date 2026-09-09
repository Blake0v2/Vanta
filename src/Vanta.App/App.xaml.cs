using System.Windows;
using Vanta.Interop;

namespace Vanta;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\Vanta.AutoClicker.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsCaptureRun())
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsSingleInstanceMutex);
            if (!_ownsSingleInstanceMutex)
            {
                ActivateRunningInstance();
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

    private static void ActivateRunningInstance()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var windowHandle = NativeMethods.FindWindow(null, "Vanta Auto Clicker");
            if (windowHandle != IntPtr.Zero)
            {
                NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SwRestore);
                NativeMethods.SetForegroundWindow(windowHandle);
                return;
            }

            Thread.Sleep(100);
        }
    }
}
