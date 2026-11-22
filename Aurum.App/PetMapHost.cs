using System.Collections.Concurrent;
using System.Windows.Threading;
using AionMeter.Core.Pets;
using AionMeter.Core.PetMaps;

namespace Aurum;

internal interface IPetMapView : IDisposable
{
    void Arm();
    void Stop();
}

// Map windows have their own dispatcher. Combat UI refreshes, tooltips, and
// persistence on the main dispatcher cannot delay a map-frame submission.
public sealed class PetMapController : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action<IPetMapView>> _commands = new();
    private Dispatcher? _dispatcher;
    private IPetMapView? _view;
    private object _diagnostic = new { Ready = false, Visible = false };
    private int _hasMapView, _disposed;
    public bool HasMapView => Volatile.Read(ref _hasMapView) != 0;
    public object DiagnosticState => Volatile.Read(ref _diagnostic);
    public PetMapController(PetProgress progress, UserSettings settings, Func<int> currentMap, nint meterWindow = 0, Func<PlayerMapPosition?>? position = null)
    {
        _thread = new Thread(() => {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                void Publish(bool visible, object state) {
                    Volatile.Write(ref _hasMapView, visible ? 1 : 0); Volatile.Write(ref _diagnostic, state);
                }
                _view = settings.UseOwnPetMap
                    ? new OwnPetMapView(progress, settings, currentMap, position ?? (() => null), Publish)
                    : new PetMapView(progress, settings, currentMap, meterWindow, Publish);
                Drain(); Dispatcher.Run(); _view.Dispose();
            }
            catch (Exception error) { Volatile.Write(ref _diagnostic, new { Ready = false, Error = error.ToString() }); }
        }) { IsBackground = true, Name = "Aurum map renderer", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
    }
    private void Drain()
    {
        while (_view != null && _commands.TryDequeue(out var command)) command(_view);
    }
    private void Post(Action<IPetMapView> command)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _commands.Enqueue(command);
        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher != null && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Drain));
    }
    public void Arm() => Post(view => view.Arm());
    public void Stop() => Post(view => view.Stop());
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _commands.Enqueue(view => { view.Dispose(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send); });
        var dispatcher = Volatile.Read(ref _dispatcher);
        if (dispatcher != null && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Drain));
    }
}
