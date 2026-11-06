using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace KwangCam;

/// <summary>사용자가 고른 화면 설정</summary>
internal sealed class ViewSettings
{
    public int ExtraRotation { get; set; }   // 0, 90, 180, 270
    public bool MirrorH { get; set; }
    public bool MirrorV { get; set; }
    public bool Fill { get; set; }           // true = 화면 꽉 채우기(잘라냄), false = 전체 보이기(검은 여백)
}

/// <summary>JPEG → 회전/반전 → 1280x720 BGR 프레임으로 변환</summary>
internal sealed class FrameProcessor : IDisposable
{
    private readonly Bitmap _canvas = new(VirtualCamera.Width, VirtualCamera.Height, PixelFormat.Format24bppRgb);
    private readonly Graphics _g;
    private readonly byte[] _bgr = new byte[VirtualCamera.FrameBytes];

    public FrameProcessor()
    {
        _g = Graphics.FromImage(_canvas);
        _g.InterpolationMode = InterpolationMode.Bilinear;
        _g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        _g.CompositingQuality = CompositingQuality.HighSpeed;
        _g.SmoothingMode = SmoothingMode.None;
    }

    /// <summary>현재 캔버스(미리보기용). 반드시 lock(processor) 안에서 사용.</summary>
    public Bitmap Canvas => _canvas;

    /// <summary>프레임을 변환해서 BGR 배열을 돌려줌. 실패하면 null.</summary>
    public byte[]? Process(byte[] jpeg, int length, int phoneRotation, ViewSettings vs)
    {
        using var ms = new MemoryStream(jpeg, 0, length, writable: false);
        using var src = new Bitmap(ms);

        int rot = ((phoneRotation + vs.ExtraRotation) % 360 + 360) % 360;
        bool flipX = vs.MirrorH;
        if (vs.MirrorV)
        {
            // 상하 반전 = 180도 회전 + 좌우 반전
            rot = (rot + 180) % 360;
            flipX = !flipX;
        }
        src.RotateFlip(ToRotateFlip(rot, flipX));

        DrawFitted(src, vs.Fill);
        return CopyOut();
    }

    /// <summary>폰이 연결되지 않았을 때 보여줄 안내 화면</summary>
    public byte[] Placeholder(string line1, string line2)
    {
        _g.Clear(Color.FromArgb(24, 28, 40));
        using var big = new Font("Malgun Gothic", 44, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font("Malgun Gothic", 26, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        _g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        _g.DrawString(line1, big, Brushes.White, new RectangleF(0, 260, VirtualCamera.Width, 80), sf);
        using var gray = new SolidBrush(Color.FromArgb(170, 180, 200));
        _g.DrawString(line2, small, gray, new RectangleF(0, 350, VirtualCamera.Width, 60), sf);
        return CopyOut();
    }

    private void DrawFitted(Bitmap src, bool fill)
    {
        float cw = VirtualCamera.Width, ch = VirtualCamera.Height;
        float scale = fill
            ? Math.Max(cw / src.Width, ch / src.Height)
            : Math.Min(cw / src.Width, ch / src.Height);
        float w = src.Width * scale, h = src.Height * scale;
        _g.Clear(Color.Black);
        _g.DrawImage(src, (cw - w) / 2f, (ch - h) / 2f, w, h);
    }

    private byte[] CopyOut()
    {
        var rect = new Rectangle(0, 0, VirtualCamera.Width, VirtualCamera.Height);
        var data = _canvas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int rowBytes = VirtualCamera.Width * 3;
            if (data.Stride == rowBytes)
            {
                Marshal.Copy(data.Scan0, _bgr, 0, _bgr.Length);
            }
            else
            {
                for (int y = 0; y < VirtualCamera.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, _bgr, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            _canvas.UnlockBits(data);
        }
        return _bgr;
    }

    private static RotateFlipType ToRotateFlip(int rot, bool flipX) => (rot, flipX) switch
    {
        (90, false) => RotateFlipType.Rotate90FlipNone,
        (180, false) => RotateFlipType.Rotate180FlipNone,
        (270, false) => RotateFlipType.Rotate270FlipNone,
        (0, true) => RotateFlipType.RotateNoneFlipX,
        (90, true) => RotateFlipType.Rotate90FlipX,
        (180, true) => RotateFlipType.Rotate180FlipX,
        (270, true) => RotateFlipType.Rotate270FlipX,
        _ => RotateFlipType.RotateNoneFlipNone,
    };

    public void Dispose()
    {
        _g.Dispose();
        _canvas.Dispose();
    }
}
