using System.Runtime.InteropServices;

namespace KwangCam;

/// <summary>
/// softcam.dll(가상 카메라)을 감싼 클래스.
/// 설치 프로그램이 softcam.dll을 등록해 두면, Zoom 등 카메라 목록에 "광쌤캠 (KwangCam)"이 나타납니다.
/// 프레임은 위에서 아래 순서, BGR 24비트로 보냅니다.
/// </summary>
internal sealed class VirtualCamera : IDisposable
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int FrameBytes = Width * Height * 3;

    [DllImport("softcam.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr scCreateCamera(int width, int height, float framerate);

    [DllImport("softcam.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void scDeleteCamera(IntPtr camera);

    [DllImport("softcam.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void scSendFrame(IntPtr camera, byte[] imageBits);

    [DllImport("softcam.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool scIsConnected(IntPtr camera);

    private IntPtr _cam;
    private readonly object _lock = new();

    private VirtualCamera(IntPtr cam) => _cam = cam;

    /// <summary>가상 카메라 생성. 실패하면 오류 메시지와 함께 null.</summary>
    public static VirtualCamera? TryCreate(out string? error)
    {
        error = null;
        try
        {
            // framerate 0 = 받은 프레임을 바로 내보냄
            var cam = scCreateCamera(Width, Height, 0f);
            if (cam == IntPtr.Zero)
            {
                error = "가상 카메라를 만들 수 없어요. 다른 가상 카메라 프로그램이 같은 이름으로 실행 중인지 확인해 주세요.";
                return null;
            }
            return new VirtualCamera(cam);
        }
        catch (DllNotFoundException)
        {
            error = "softcam.dll을 찾을 수 없어요. 광쌤캠을 다시 설치해 주세요.";
            return null;
        }
        catch (Exception ex)
        {
            error = "가상 카메라 오류: " + ex.Message;
            return null;
        }
    }

    /// <summary>Zoom 같은 앱이 지금 이 카메라를 쓰고 있는지</summary>
    public bool IsInUse
    {
        get
        {
            lock (_lock) return _cam != IntPtr.Zero && scIsConnected(_cam);
        }
    }

    public void Send(byte[] bgr)
    {
        if (bgr.Length < FrameBytes) return;
        lock (_lock)
        {
            if (_cam != IntPtr.Zero) scSendFrame(_cam, bgr);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_cam != IntPtr.Zero)
            {
                scDeleteCamera(_cam);
                _cam = IntPtr.Zero;
            }
        }
    }
}
