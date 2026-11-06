package com.kwangssam.kwangcam

import android.Manifest
import android.annotation.SuppressLint
import android.content.pm.PackageManager
import android.graphics.ImageFormat
import android.graphics.Rect
import android.graphics.YuvImage
import android.os.Bundle
import android.text.InputType
import android.util.Size
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.OrientationEventListener
import android.view.Surface
import android.view.WindowManager
import android.widget.EditText
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.camera.core.Camera
import androidx.camera.core.CameraSelector
import androidx.camera.core.FocusMeteringAction
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.core.content.ContextCompat
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import com.kwangssam.kwangcam.databinding.ActivityMainBinding
import java.io.ByteArrayOutputStream
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

class MainActivity : AppCompatActivity() {

    companion object {
        const val DEFAULT_PORT = 51234
        private const val PREFS = "kwangcam"
        private const val KEY_HOSTS = "hosts"
        private const val KEY_PORT = "port"
        private const val KEY_QUALITY = "quality"
        private const val KEY_FRONT = "front"
    }

    /** 화질 단계: 이름, 해상도, JPEG 품질 */
    private data class Quality(val label: String, val size: Size, val jpeg: Int)
    private val qualities = listOf(
        Quality("보통", Size(960, 540), 70),
        Quality("고화질", Size(1280, 720), 85),
    )

    private lateinit var b: ActivityMainBinding
    private val sender = FrameSender()
    private lateinit var analysisExecutor: ExecutorService
    private val netExecutor = Executors.newSingleThreadScheduledExecutor()

    private var camera: Camera? = null
    private var imageAnalysis: ImageAnalysis? = null
    private var useFront = false
    private var torchOn = false
    private var qualityIndex = 1
    private var dimmed = false

    // 연결 대상 (QR이나 직접 입력으로 정해짐)
    @Volatile private var targetHosts: List<String> = emptyList()
    @Volatile private var targetPort = DEFAULT_PORT
    @Volatile private var wantConnected = false
    @Volatile private var connecting = false

    // fps 계산
    private var framesThisSecond = 0
    private var lastFpsTime = 0L
    @Volatile private var currentFps = 0

    // 프레임 변환용 버퍼 재사용
    private var nv21: ByteArray = ByteArray(0)
    private val jpegStream = object : ByteArrayOutputStream(256 * 1024) {
        fun buffer(): ByteArray = buf
    }

    private val requestCamera = registerForActivityResult(ActivityResultContracts.RequestPermission()) { ok ->
        if (ok) startCamera() else {
            Toast.makeText(this, R.string.need_camera, Toast.LENGTH_LONG).show()
            finish()
        }
    }

    private val scanQr = registerForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@registerForActivityResult
        val parsed = parseAddress(text, requirePrefix = true)
        if (parsed == null) {
            Toast.makeText(this, R.string.qr_invalid, Toast.LENGTH_LONG).show()
        } else {
            connectTo(parsed.first, parsed.second)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        b = ActivityMainBinding.inflate(layoutInflater)
        setContentView(b.root)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        analysisExecutor = Executors.newSingleThreadExecutor()

        val prefs = getSharedPreferences(PREFS, MODE_PRIVATE)
        qualityIndex = prefs.getInt(KEY_QUALITY, 1).coerceIn(0, qualities.lastIndex)
        useFront = prefs.getBoolean(KEY_FRONT, false)
        updateQualityButton()

        b.btnConnect.setOnClickListener {
            if (wantConnected) disconnect() else launchScanner()
        }
        b.btnManual.setOnClickListener { showManualDialog() }
        b.btnSwitch.setOnClickListener {
            useFront = !useFront
            torchOn = false
            prefs.edit().putBoolean(KEY_FRONT, useFront).apply()
            bindCamera()
        }
        b.btnTorch.setOnClickListener {
            val cam = camera ?: return@setOnClickListener
            if (cam.cameraInfo.hasFlashUnit()) {
                torchOn = !torchOn
                cam.cameraControl.enableTorch(torchOn)
            }
        }
        b.btnQuality.setOnClickListener {
            qualityIndex = (qualityIndex + 1) % qualities.size
            prefs.edit().putInt(KEY_QUALITY, qualityIndex).apply()
            updateQualityButton()
            bindCamera()
        }
        b.btnDim.setOnClickListener { setDimmed(true) }

        val dimGestures = GestureDetector(this, object : GestureDetector.SimpleOnGestureListener() {
            override fun onDown(e: MotionEvent) = true
            override fun onDoubleTap(e: MotionEvent): Boolean { setDimmed(false); return true }
        })
        b.dimOverlay.setOnTouchListener { _, ev -> dimGestures.onTouchEvent(ev) }

        setupTapToFocus()
        setupOrientation()

        // 지난번에 연결했던 PC가 있으면 자동으로 다시 연결 시도
        val savedHosts = prefs.getString(KEY_HOSTS, null)
        if (!savedHosts.isNullOrBlank()) {
            connectTo(savedHosts.split(',').filter { it.isNotBlank() }, prefs.getInt(KEY_PORT, DEFAULT_PORT), save = false)
        }

        if (ContextCompat.checkSelfPermission(this, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            startCamera()
        } else {
            requestCamera.launch(Manifest.permission.CAMERA)
        }

        // 1초마다 상태 표시 갱신
        netExecutor.scheduleWithFixedDelay({ runOnUiThread { refreshStatus() } }, 1, 1, TimeUnit.SECONDS)
    }

    override fun onDestroy() {
        super.onDestroy()
        wantConnected = false
        netExecutor.shutdownNow()
        analysisExecutor.shutdown()
        Thread { sender.close() }.start()
    }

    // ---------------- 카메라 ----------------

    private fun startCamera() = bindCamera()

    private fun bindCamera() {
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            val provider = future.get()
            val q = qualities[qualityIndex]

            val preview = Preview.Builder().build().also {
                it.setSurfaceProvider(b.preview.surfaceProvider)
            }

            val selector = ResolutionSelector.Builder()
                .setResolutionStrategy(
                    ResolutionStrategy(q.size, ResolutionStrategy.FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER)
                )
                .build()

            val analysis = ImageAnalysis.Builder()
                .setResolutionSelector(selector)
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_YUV_420_888)
                .setTargetRotation(currentRotation)
                .build()
            analysis.setAnalyzer(analysisExecutor) { image -> handleFrame(image) }
            imageAnalysis = analysis

            var cameraSelector = if (useFront) CameraSelector.DEFAULT_FRONT_CAMERA else CameraSelector.DEFAULT_BACK_CAMERA
            if (!provider.hasCamera(cameraSelector)) {
                useFront = !useFront
                cameraSelector = if (useFront) CameraSelector.DEFAULT_FRONT_CAMERA else CameraSelector.DEFAULT_BACK_CAMERA
            }

            provider.unbindAll()
            camera = provider.bindToLifecycle(this, cameraSelector, preview, analysis)
        }, ContextCompat.getMainExecutor(this))
    }

    @SuppressLint("ClickableViewAccessibility")
    private fun setupTapToFocus() {
        b.preview.setOnTouchListener { v, ev ->
            if (ev.action == MotionEvent.ACTION_UP) {
                val cam = camera
                if (cam != null) {
                    val point = b.preview.meteringPointFactory.createPoint(ev.x, ev.y)
                    val action = FocusMeteringAction.Builder(point)
                        .setAutoCancelDuration(5, TimeUnit.SECONDS)
                        .build()
                    cam.cameraControl.startFocusAndMetering(action)
                }
                v.performClick()
            }
            true
        }
    }

    // 화면은 세로로 고정하지만, 폰을 실제로 눕히면 영상이 바로 서도록 회전값을 맞춰 줌
    @Volatile private var currentRotation = Surface.ROTATION_0

    private fun setupOrientation() {
        val listener = object : OrientationEventListener(this) {
            override fun onOrientationChanged(orientation: Int) {
                if (orientation == ORIENTATION_UNKNOWN) return
                val rot = when (orientation) {
                    in 45 until 135 -> Surface.ROTATION_270
                    in 135 until 225 -> Surface.ROTATION_180
                    in 225 until 315 -> Surface.ROTATION_90
                    else -> Surface.ROTATION_0
                }
                if (rot != currentRotation) {
                    currentRotation = rot
                    imageAnalysis?.targetRotation = rot
                }
            }
        }
        if (listener.canDetectOrientation()) listener.enable()
    }

    // ---------------- 프레임 처리 (분석 스레드) ----------------

    private fun handleFrame(image: ImageProxy) {
        try {
            if (!sender.isConnected) return
            val q = qualities[qualityIndex]
            val w = image.width
            val h = image.height
            val data = toNv21(image)
            jpegStream.reset()
            YuvImage(data, ImageFormat.NV21, w, h, null).compressToJpeg(Rect(0, 0, w, h), q.jpeg, jpegStream)
            val ok = sender.send(jpegStream.buffer(), jpegStream.size(), image.imageInfo.rotationDegrees, useFront)
            if (ok) countFrame() else onConnectionLost()
        } catch (_: Exception) {
            // 프레임 하나 실패는 무시
        } finally {
            image.close()
        }
    }

    /** YUV_420_888 → NV21 (줄 간격·픽셀 간격을 고려해서 복사) */
    private fun toNv21(image: ImageProxy): ByteArray {
        val w = image.width
        val h = image.height
        val ySize = w * h
        val total = ySize + ySize / 2
        if (nv21.size != total) nv21 = ByteArray(total)
        val out = nv21

        val yPlane = image.planes[0]
        val yBuf = yPlane.buffer
        val yRow = yPlane.rowStride
        if (yRow == w) {
            yBuf.position(0)
            yBuf.get(out, 0, ySize)
        } else {
            for (row in 0 until h) {
                yBuf.position(row * yRow)
                yBuf.get(out, row * w, w)
            }
        }

        val uPlane = image.planes[1]
        val vPlane = image.planes[2]
        val uBuf = uPlane.buffer
        val vBuf = vPlane.buffer
        val uvRow = uPlane.rowStride
        val uvPix = uPlane.pixelStride
        val vRow = vPlane.rowStride
        val vPix = vPlane.pixelStride
        var pos = ySize
        val cw = w / 2
        val ch = h / 2
        for (row in 0 until ch) {
            val uBase = row * uvRow
            val vBase = row * vRow
            for (col in 0 until cw) {
                out[pos++] = vBuf.get(vBase + col * vPix)
                out[pos++] = uBuf.get(uBase + col * uvPix)
            }
        }
        return out
    }

    private fun countFrame() {
        val now = System.currentTimeMillis()
        framesThisSecond++
        if (now - lastFpsTime >= 1000) {
            currentFps = framesThisSecond
            framesThisSecond = 0
            lastFpsTime = now
        }
    }

    // ---------------- 연결 ----------------

    private fun launchScanner() {
        val opts = ScanOptions()
            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
            .setPrompt(getString(R.string.qr_prompt))
            .setBeepEnabled(false)
            .setOrientationLocked(true)
        scanQr.launch(opts)
    }

    private fun showManualDialog() {
        val input = EditText(this).apply {
            hint = getString(R.string.manual_hint)
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
            setSingleLine()
            val prefs = getSharedPreferences(PREFS, MODE_PRIVATE)
            val h = prefs.getString(KEY_HOSTS, null)?.split(',')?.firstOrNull()
            if (!h.isNullOrBlank()) setText("$h:${prefs.getInt(KEY_PORT, DEFAULT_PORT)}")
        }
        AlertDialog.Builder(this)
            .setTitle(R.string.manual_title)
            .setView(input)
            .setPositiveButton(R.string.ok) { _, _ ->
                val parsed = parseAddress(input.text.toString(), requirePrefix = false)
                if (parsed == null) {
                    Toast.makeText(this, R.string.status_failed, Toast.LENGTH_SHORT).show()
                } else {
                    connectTo(parsed.first, parsed.second)
                }
            }
            .setNegativeButton(R.string.cancel, null)
            .show()
    }

    /** "kwangcam://192.168.0.5,10.0.0.2:51234" 또는 "192.168.0.5:51234" 또는 "192.168.0.5" */
    private fun parseAddress(raw: String, requirePrefix: Boolean): Pair<List<String>, Int>? {
        var s = raw.trim()
        val prefix = "kwangcam://"
        if (s.startsWith(prefix, ignoreCase = true)) {
            s = s.substring(prefix.length)
        } else if (requirePrefix) {
            return null
        }
        s = s.trimEnd('/')
        var port = DEFAULT_PORT
        val colon = s.lastIndexOf(':')
        if (colon > 0) {
            port = s.substring(colon + 1).toIntOrNull() ?: return null
            s = s.substring(0, colon)
        }
        val hosts = s.split(',').map { it.trim() }.filter { it.isNotEmpty() }
        if (hosts.isEmpty() || port !in 1..65535) return null
        return hosts to port
    }

    private fun connectTo(hosts: List<String>, port: Int, save: Boolean = true) {
        targetHosts = hosts
        targetPort = port
        wantConnected = true
        if (save) {
            getSharedPreferences(PREFS, MODE_PRIVATE).edit()
                .putString(KEY_HOSTS, hosts.joinToString(","))
                .putInt(KEY_PORT, port)
                .apply()
        }
        b.btnConnect.setText(R.string.btn_disconnect)
        b.status.setText(R.string.status_connecting)
        tryConnect()
    }

    private fun tryConnect() {
        if (connecting) return
        connecting = true
        netExecutor.execute {
            val ok = wantConnected && sender.connect(targetHosts, targetPort) != null
            connecting = false
            if (ok) {
                runOnUiThread { refreshStatus() }
            } else if (wantConnected) {
                runOnUiThread { b.status.setText(R.string.status_failed) }
                // 3초 뒤 다시 시도
                netExecutor.schedule({ if (wantConnected && !sender.isConnected) tryConnect() }, 3, TimeUnit.SECONDS)
            }
        }
    }

    private fun onConnectionLost() {
        currentFps = 0
        if (wantConnected) {
            runOnUiThread { b.status.setText(R.string.status_lost) }
            netExecutor.schedule({ if (wantConnected && !sender.isConnected) tryConnect() }, 1, TimeUnit.SECONDS)
        }
    }

    private fun disconnect() {
        wantConnected = false
        netExecutor.execute { sender.close() }
        // 자동 재연결도 그만하도록 저장된 주소 삭제
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().remove(KEY_HOSTS).apply()
        b.btnConnect.setText(R.string.btn_qr)
        b.status.setText(R.string.status_idle)
    }

    private fun refreshStatus() {
        if (!wantConnected) return
        if (sender.isConnected) {
            b.status.text = getString(R.string.status_connected, currentFps)
        }
    }

    // ---------------- 기타 ----------------

    private fun updateQualityButton() {
        b.btnQuality.text = getString(R.string.btn_quality, qualities[qualityIndex].label)
    }

    private fun setDimmed(on: Boolean) {
        dimmed = on
        b.dimOverlay.visibility = if (on) android.view.View.VISIBLE else android.view.View.GONE
        val lp = window.attributes
        lp.screenBrightness = if (on) 0.01f else WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE
        window.attributes = lp
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (dimmed) setDimmed(false) else {
            @Suppress("DEPRECATION")
            super.onBackPressed()
        }
    }
}
