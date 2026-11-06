using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace KwangCam;

/// <summary>
/// 폰 앱이 보내는 JPEG 프레임을 받는 TCP 서버.
/// 프레임 형식: "KWC1" | 회전 uint16 | 플래그 uint8 | 예약 uint8 | 길이 uint32 | JPEG (빅엔디언)
/// </summary>
internal sealed class FrameReceiver : IDisposable
{
    public const int DefaultPort = 51234;
    private const int MaxFrameBytes = 16 * 1024 * 1024;

    public int Port { get; private set; }

    /// <summary>JPEG 데이터, 길이, 회전각도, 전면카메라 여부 (수신 스레드에서 호출)</summary>
    public event Action<byte[], int, int, bool>? FrameReceived;
    /// <summary>폰 연결/끊김. 인자는 폰 주소(끊기면 null)</summary>
    public event Action<string?>? ClientChanged;

    private TcpListener? _listener;
    private TcpClient? _client;
    private readonly object _lock = new();
    private volatile bool _running;

    public void Start()
    {
        for (int p = DefaultPort; p < DefaultPort + 10; p++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, p);
                l.Start();
                _listener = l;
                Port = p;
                break;
            }
            catch (SocketException) { /* 다음 포트 */ }
        }
        if (_listener == null) throw new InvalidOperationException("사용할 수 있는 포트가 없어요.");

        _running = true;
        new Thread(AcceptLoop) { IsBackground = true, Name = "accept" }.Start();
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            TcpClient c;
            try { c = _listener!.AcceptTcpClient(); }
            catch { if (!_running) return; continue; }

            c.NoDelay = true;
            c.ReceiveBufferSize = 1024 * 1024;
            lock (_lock)
            {
                // 새 폰이 연결되면 이전 연결은 끊음
                try { _client?.Close(); } catch { }
                _client = c;
            }
            new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "read" }.Start();
        }
    }

    private void ReadLoop(TcpClient c)
    {
        string? remote = (c.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString();
        ClientChanged?.Invoke(remote);
        var header = new byte[12];
        var buf = new byte[512 * 1024];
        try
        {
            var s = c.GetStream();
            while (_running)
            {
                ReadExactly(s, header, 12);
                if (header[0] != 'K' || header[1] != 'W' || header[2] != 'C' || header[3] != '1')
                    throw new IOException("잘못된 데이터");
                int rotation = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                bool front = (header[6] & 1) != 0;
                int len = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
                if (len <= 0 || len > MaxFrameBytes) throw new IOException("프레임 크기 오류");
                if (buf.Length < len) buf = new byte[len * 2];
                ReadExactly(s, buf, len);
                FrameReceived?.Invoke(buf, len, rotation, front);
            }
        }
        catch { /* 연결 끊김 */ }
        finally
        {
            bool wasCurrent;
            lock (_lock)
            {
                wasCurrent = ReferenceEquals(_client, c);
                if (wasCurrent) _client = null;
            }
            try { c.Close(); } catch { }
            if (wasCurrent) ClientChanged?.Invoke(null);
        }
    }

    private static void ReadExactly(Stream s, byte[] buf, int count)
    {
        int off = 0;
        while (off < count)
        {
            int n = s.Read(buf, off, count - off);
            if (n <= 0) throw new EndOfStreamException();
            off += n;
        }
    }

    public void DisconnectClient()
    {
        lock (_lock)
        {
            try { _client?.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        DisconnectClient();
    }
}
