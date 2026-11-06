using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using QRCoder;

namespace KwangCam;

internal sealed class MainForm : Form
{
    // 화면 구성 요소
    private readonly PictureBox _preview = new();
    private readonly PictureBox _qr = new();
    private readonly Label _address = new();
    private readonly Label _status = new();
    private readonly Label _zoomStatus = new();
    private readonly Button _rotate = new();
    private readonly CheckBox _mirrorH = new();
    private readonly CheckBox _mirrorV = new();
    private readonly CheckBox _fill = new();
    private readonly Button _disconnect = new();
    private readonly Button _refreshQr = new();

    private readonly FrameReceiver _receiver = new();
    private readonly FrameProcessor _processor = new();
    private VirtualCamera? _vcam;
    private ViewSettings _settings = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 500 };

    private volatile string? _phone;
    private long _lastFrameTicks;
    private int _frameCount;
    private int _fps;
    private readonly Stopwatch _fpsWatch = Stopwatch.StartNew();
    private volatile bool _closing;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KwangCam", "settings.json");

    public MainForm()
    {
        Text = "광쌤캠 (KwangCam)";
        Font = new Font("Malgun Gothic", 10f);
        ClientSize = new Size(980, 560);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        LoadSettings();
        BuildLayout();

        _receiver.FrameReceived += OnFrame;
        _receiver.ClientChanged += ip =>
        {
            _phone = ip;
            if (ip == null) Interlocked.Exchange(ref _lastFrameTicks, 0);
        };

        _uiTimer.Tick += (_, _) => RefreshStatus();
        Load += OnLoadForm;
        FormClosing += (_, _) => Shutdown();
    }

    private void OnLoadForm(object? sender, EventArgs e)
    {
        _vcam = VirtualCamera.TryCreate(out var err);
        if (_vcam == null)
        {
            MessageBox.Show(this, err, "광쌤캠", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        try
        {
            _receiver.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "폰 연결을 기다릴 수 없어요: " + ex.Message, "광쌤캠",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateQr();
        new Thread(PlaceholderLoop) { IsBackground = true, Name = "placeholder" }.Start();
        _uiTimer.Start();
        RefreshStatus();
    }

    // ---------------- 화면 구성 ----------------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        Controls.Add(root);

        // 왼쪽: 미리보기 + 화면 설정
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(left, 0, 0);

        var previewTitle = new Label
        {
            Text = "Zoom으로 나가는 화면",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
        };
        left.Controls.Add(previewTitle, 0, 0);

        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.Black;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        left.Controls.Add(_preview, 0, 1);

        var controls = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0),
        };
        _rotate.Text = "↻ 90° 회전";
        _rotate.AutoSize = true;
        _rotate.Click += (_, _) =>
        {
            _settings.ExtraRotation = (_settings.ExtraRotation + 90) % 360;
            SaveSettings();
        };
        _mirrorH.Text = "좌우 반전";
        _mirrorV.Text = "상하 반전";
        _fill.Text = "화면 꽉 채우기";
        foreach (var cb in new[] { _mirrorH, _mirrorV, _fill })
        {
            cb.AutoSize = true;
            cb.Margin = new Padding(14, 8, 0, 0);
        }
        _mirrorH.Checked = _settings.MirrorH;
        _mirrorV.Checked = _settings.MirrorV;
        _fill.Checked = _settings.Fill;
        _mirrorH.CheckedChanged += (_, _) => { _settings.MirrorH = _mirrorH.Checked; SaveSettings(); };
        _mirrorV.CheckedChanged += (_, _) => { _settings.MirrorV = _mirrorV.Checked; SaveSettings(); };
        _fill.CheckedChanged += (_, _) => { _settings.Fill = _fill.Checked; SaveSettings(); };
        controls.Controls.AddRange(new Control[] { _rotate, _mirrorH, _mirrorV, _fill });
        left.Controls.Add(controls, 0, 2);

        // 오른쪽: QR코드 + 상태 + 안내
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12, 0, 0, 0),
        };
        root.Controls.Add(right, 1, 0);

        right.Controls.Add(new Label
        {
            Text = "① 폰에서 광쌤캠 앱을 열고\n    [QR 연결]로 아래 코드를 찍으세요",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        });

        _qr.Size = new Size(240, 240);
        _qr.SizeMode = PictureBoxSizeMode.Zoom;
        _qr.BorderStyle = BorderStyle.FixedSingle;
        right.Controls.Add(_qr);

        _address.AutoSize = true;
        _address.ForeColor = Color.DimGray;
        _address.MaximumSize = new Size(270, 0);
        right.Controls.Add(_address);

        _refreshQr.Text = "QR 새로 만들기";
        _refreshQr.AutoSize = true;
        _refreshQr.Click += (_, _) => UpdateQr();
        right.Controls.Add(_refreshQr);

        _status.AutoSize = true;
        _status.MaximumSize = new Size(270, 0);
        _status.Font = new Font(Font, FontStyle.Bold);
        _status.Margin = new Padding(0, 14, 0, 4);
        right.Controls.Add(_status);

        _disconnect.Text = "폰 연결 끊기";
        _disconnect.AutoSize = true;
        _disconnect.Click += (_, _) => _receiver.DisconnectClient();
        right.Controls.Add(_disconnect);

        right.Controls.Add(new Label
        {
            Text = "② Zoom → 설정 → 비디오 → 카메라에서\n    \"광쌤캠 (KwangCam)\"을 고르세요.\n" +
                   "    교재만 따로 보여주려면 화면 공유 →\n    고급 → 보조 카메라의 콘텐츠",
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 4),
        });

        _zoomStatus.AutoSize = true;
        _zoomStatus.ForeColor = Color.DimGray;
        right.Controls.Add(_zoomStatus);
    }

    private void UpdateQr()
    {
        var ips = NetInfo.GetLanAddresses();
        if (ips.Count == 0)
        {
            _qr.Image = null;
            _address.Text = "와이파이(인터넷)에 연결되어 있지 않아요. 연결 후 [QR 새로 만들기]를 누르세요.";
            return;
        }
        // 주소가 여러 개면 폰이 차례로 시도 (최대 3개)
        string payload = $"kwangcam://{string.Join(",", ips.Take(3))}:{_receiver.Port}";
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var old = _qr.Image;
        _qr.Image = Image.FromStream(new MemoryStream(png));
        old?.Dispose();
        _address.Text = "직접 입력할 주소: " + string.Join("  또는  ", ips.Take(3).Select(ip => $"{ip}:{_receiver.Port}"));
    }

    // ---------------- 프레임 ----------------

    private void OnFrame(byte[] jpeg, int length, int rotation, bool front)
    {
        if (_closing) return;
        Bitmap? thumb = null;
        lock (_processor)
        {
            byte[]? bgr;
            try { bgr = _processor.Process(jpeg, length, rotation, _settings); }
            catch { return; } // 깨진 프레임은 건너뜀
            if (bgr == null) return;
            _vcam?.Send(bgr);

            Interlocked.Exchange(ref _lastFrameTicks, Environment.TickCount64);
            int n = Interlocked.Increment(ref _frameCount);
            // 미리보기는 3장에 1장만 갱신 (CPU 절약)
            if (n % 3 == 0) thumb = new Bitmap(_processor.Canvas, 640, 360);
        }
        if (thumb != null) ShowPreview(thumb);
    }

    private void ShowPreview(Bitmap bmp)
    {
        if (_closing || !IsHandleCreated) { bmp.Dispose(); return; }
        try
        {
            BeginInvoke(new Action(() =>
            {
                var old = _preview.Image;
                _preview.Image = bmp;
                old?.Dispose();
            }));
        }
        catch (InvalidOperationException) { bmp.Dispose(); }
    }

    /// <summary>폰이 연결되지 않은 동안 Zoom에 안내 화면을 보냄</summary>
    private void PlaceholderLoop()
    {
        bool shown = false;
        while (!_closing)
        {
            long last = Interlocked.Read(ref _lastFrameTicks);
            bool stale = Environment.TickCount64 - last > 1500;
            if (stale)
            {
                Bitmap? thumb = null;
                lock (_processor)
                {
                    var bgr = _processor.Placeholder("광쌤캠", "핸드폰 앱에서 연결해 주세요");
                    _vcam?.Send(bgr);
                    if (!shown) thumb = new Bitmap(_processor.Canvas, 640, 360);
                }
                if (thumb != null) ShowPreview(thumb);
                shown = true;
            }
            else
            {
                shown = false;
            }
            Thread.Sleep(200);
        }
    }

    private void RefreshStatus()
    {
        if (_fpsWatch.ElapsedMilliseconds >= 1000)
        {
            _fps = (int)Math.Round(Interlocked.Exchange(ref _frameCount, 0) * 1000.0 / _fpsWatch.ElapsedMilliseconds);
            _fpsWatch.Restart();
        }

        var phone = _phone;
        bool receiving = Environment.TickCount64 - Interlocked.Read(ref _lastFrameTicks) < 1500;
        if (phone == null)
        {
            _status.Text = "● 폰 연결 기다리는 중";
            _status.ForeColor = Color.DarkOrange;
        }
        else if (receiving)
        {
            _status.Text = $"● 폰 연결됨 ({phone}) · {_fps} fps";
            _status.ForeColor = Color.SeaGreen;
        }
        else
        {
            _status.Text = $"● 폰 연결됨 ({phone}) · 영상 없음\n   (폰 앱 화면이 켜져 있는지 확인)";
            _status.ForeColor = Color.DarkOrange;
        }
        _disconnect.Enabled = phone != null;

        if (_vcam == null)
            _zoomStatus.Text = "가상 카메라를 쓸 수 없는 상태예요.";
        else
            _zoomStatus.Text = _vcam.IsInUse ? "Zoom 등에서 광쌤캠을 사용 중이에요." : "아직 광쌤캠을 쓰는 프로그램이 없어요.";
    }

    // ---------------- 설정 저장 ----------------

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonSerializer.Deserialize<ViewSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { _settings = new(); }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings));
        }
        catch { /* 저장 실패는 무시 */ }
    }

    private void Shutdown()
    {
        _closing = true;
        _uiTimer.Stop();
        _receiver.Dispose();
        lock (_processor)
        {
            _vcam?.Dispose();
            _vcam = null;
        }
    }
}
