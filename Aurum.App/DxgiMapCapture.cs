using OpenCvSharp;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;
using static Vortice.Direct3D11.D3D11;
namespace Aurum;

public sealed class DxgiMapCapture : IDisposable
{
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGIOutputDuplication _duplication = null!;
    private ID3D11Texture2D _staging = null!;
    private readonly Mat _latest = new();
    private int _left, _top, _screenWidth, _screenHeight;
    private readonly bool _grayscale;
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public long FrameTimestamp { get; private set; }
    public DxgiMapCapture(int x, int y, int width, int height, bool grayscale = true)
    {
        X = x; Y = y; Width = width; Height = height; _grayscale = grayscale;
        try
        {
            using var factory = CreateDXGIFactory1<IDXGIFactory1>();
            for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
            {
                using (adapter)
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    using (output)
                    {
                        var description = output.Description; var bounds = description.DesktopCoordinates;
                        if (!description.AttachedToDesktop || x < bounds.Left || y < bounds.Top || x + width > bounds.Right || y + height > bounds.Bottom) continue;
                        if (description.Rotation != ModeRotation.Identity && description.Rotation != ModeRotation.Unspecified) continue;
                        D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            [FeatureLevel.Level_11_0], out _device, out _context).CheckError();
                        using var output1 = output.QueryInterface<IDXGIOutput1>();
                        _duplication = output1.DuplicateOutput(_device);
                        _left = bounds.Left; _top = bounds.Top; _screenWidth = bounds.Right - bounds.Left; _screenHeight = bounds.Bottom - bounds.Top;
                        _staging = _device.CreateTexture2D(new Texture2DDescription {
                            Width = (uint)_screenWidth, Height = (uint)_screenHeight, MipLevels = 1, ArraySize = 1,
                            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                            Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CPUAccessFlags = CpuAccessFlags.Read
                        });
                        return;
                    }
                }
            }
            throw new NotSupportedException("No unrotated display contains the game window.");
        }
        catch { Dispose(); throw; }
    }
    public Mat Read(out bool fresh)
    {
        var acquired = _duplication.AcquireNextFrame(_latest.Empty() ? 16u : 0u, out var information, out var resource);
        if (acquired.Code == unchecked((int)0x887A0027) && !_latest.Empty()) { fresh = false; return _latest.Clone(); }
        acquired.CheckError();
        try
        {
            using (resource)
            using (var desktop = resource.QueryInterface<ID3D11Texture2D>())
            {
                if (information.LastPresentTime == 0 && !_latest.Empty()) { fresh = false; return _latest.Clone(); }
                _context.CopyResource(_staging, desktop);
                var pixels = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    using var surface = Mat.FromPixelData(_screenHeight, _screenWidth, MatType.CV_8UC4, pixels.DataPointer, pixels.RowPitch);
                    using var game = new Mat(surface, new Rect(X - _left, Y - _top, Width, Height));
                    if (_grayscale) Cv2.CvtColor(game, _latest, ColorConversionCodes.BGRA2GRAY);
                    else game.CopyTo(_latest);
                    FrameTimestamp = information.LastPresentTime;
                }
                finally { _context.Unmap(_staging, 0); }
            }
        }
        finally { _duplication.ReleaseFrame(); }
        fresh = true; return _latest.Clone();
    }
    public void Dispose()
    {
        _staging?.Dispose(); _duplication?.Dispose(); _context?.Dispose(); _device?.Dispose(); _latest.Dispose();
    }
}
