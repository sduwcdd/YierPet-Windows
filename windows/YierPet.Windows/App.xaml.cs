using System.Windows;
using YierPet.Windows.Core;

namespace YierPet.Windows;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private DuoCoordinator? _duo;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--verify-release")
        {
            base.OnStartup(e);
            Shutdown(ReleaseVerifier.Run(e.Args[1]));
            return;
        }
        _instanceMutex = new Mutex(true, @"Local\YierPet.Windows", out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _duo = new DuoCoordinator(SettingsStore.Load());
        _duo.Start();
        if (e.Args.Contains("--library")) _duo.OpenLibrary();
        if (e.Args.Contains("--voice")) _duo.OpenVoice();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _duo?.Dispose();
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
