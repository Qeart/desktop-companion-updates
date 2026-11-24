using System.Collections.Concurrent;
using System.IO;
using System.Media;
using AionMeter.Core.Bosses;

namespace Aurum;

internal sealed class BossSound : IDisposable
{
    private readonly BlockingCollection<(BossAlert Alert, int Volume, long Queued)> _queue = new(4);
    private readonly Thread _worker;
    private bool _disposed;
    internal string? LastError { get; private set; }
    internal BossSound()
    {
        _worker = new Thread(Work) { IsBackground = true, Name = "Aurum boss audio" };
        _worker.SetApartmentState(ApartmentState.STA); _worker.Start();
    }
    internal void Enqueue(BossAlert alert, double volume)
    {
        if (_disposed) return;
        _queue.TryAdd((alert, (int)Math.Clamp(double.IsFinite(volume) ? volume : 45, 0, 100), Environment.TickCount64));
    }
    internal void Clear() { while (_queue.TryTake(out _)) { } }
    private void Work()
    {
        foreach (var item in _queue.GetConsumingEnumerable()) {
                if (Environment.TickCount64 - item.Queued > 1200) continue;
                try {
                    using var wav = Tone(item.Alert.Category == "danger" ? 930 : 660, item.Volume);
                    using var player = new SoundPlayer(wav); player.PlaySync();
                } catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { LastError = e.Message; }
            }
    }
    private static MemoryStream Tone(int frequency, int volume)
    {
        const int rate = 22050, samples = 3307;
        var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true)) {
            writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
            for (var i = 0; i < samples; i++) { var envelope = Math.Min(1, i / 180d) * Math.Min(1, (samples - i) / 500d); writer.Write((short)(Math.Sin(i * 2 * Math.PI * frequency / rate) * 9500 * volume / 100d * envelope)); }
        }
        stream.Position = 0; return stream;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Clear(); _queue.CompleteAdding(); _worker.Join(500); }
}
