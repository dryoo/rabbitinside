using System.Configuration;
using System.Data;
using System.Windows;
using Rabbit.Services;

namespace Rabbit;

/// <summary>
/// 앱 진입점입니다. 두 번 실행하면 새 창을 띄우는 대신 이미 떠 있는 창을 앞으로 가져옵니다.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "rabbit.inside.SingleInstance";
    private const string ShowEventName = "rabbit.inside.ShowWindow";

    private System.Threading.Mutex? _instanceMutex;
    private System.Threading.EventWaitHandle? _showEvent;
    private System.Threading.CancellationTokenSource? _listenerCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new System.Threading.Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            // 이미 실행 중이면 창을 띄우라고 알리고 조용히 물러납니다.
            if (System.Threading.EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
            {
                existing.Set();
                existing.Dispose();
            }

            Shutdown();
            return;
        }

        _showEvent = new System.Threading.EventWaitHandle(
            false,
            System.Threading.EventResetMode.AutoReset,
            ShowEventName);

        _listenerCancellation = new System.Threading.CancellationTokenSource();
        StartShowListener(_listenerCancellation.Token);

        // 창이 만들어지기 전에 테마를 정해 두면 색이 깜빡이지 않습니다.
        ThemeService.ApplySystemTheme();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();
        _showEvent?.Dispose();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }

    /// <summary>다른 인스턴스가 보내는 신호를 기다렸다가 창을 앞으로 가져옵니다.</summary>
    private void StartShowListener(System.Threading.CancellationToken token)
    {
        var thread = new System.Threading.Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_showEvent is null || !_showEvent.WaitOne(500))
                    {
                        continue;
                    }

                    Dispatcher.Invoke(() => (MainWindow as MainWindow)?.BringToFront());
                }
                catch (Exception)
                {
                    // 창이 아직 준비되지 않은 순간의 신호는 무시합니다.
                }
            }
        })
        {
            IsBackground = true,
            Name = "Rabbit.ShowListener",
        };

        thread.Start();
    }
}
