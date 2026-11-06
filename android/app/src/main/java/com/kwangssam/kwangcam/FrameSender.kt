package com.kwangssam.kwangcam

import java.io.BufferedOutputStream
import java.io.DataOutputStream
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket

/**
 * PC 프로그램으로 JPEG 프레임을 보내는 TCP 연결.
 *
 * 프레임 하나의 형식 (빅엔디언):
 *   "KWC1"(4바이트) | 회전각도 uint16 | 플래그 uint8 | 예약 uint8 | JPEG 길이 uint32 | JPEG 데이터
 * 플래그 bit0 = 전면 카메라
 */
class FrameSender {

    @Volatile private var socket: Socket? = null
    private var out: DataOutputStream? = null
    private val lock = Any()

    val isConnected: Boolean get() = socket?.isConnected == true && socket?.isClosed == false

    /** 여러 주소를 차례로 시도해서 처음 되는 곳에 연결. 성공한 주소를 돌려줌. 백그라운드 스레드에서 호출. */
    fun connect(hosts: List<String>, port: Int, timeoutMs: Int = 2500): String? {
        close()
        for (host in hosts) {
            try {
                val s = Socket()
                s.tcpNoDelay = true
                s.sendBufferSize = 512 * 1024
                s.connect(InetSocketAddress(host, port), timeoutMs)
                synchronized(lock) {
                    socket = s
                    out = DataOutputStream(BufferedOutputStream(s.getOutputStream(), 256 * 1024))
                }
                return host
            } catch (_: IOException) {
                // 다음 주소 시도
            }
        }
        return null
    }

    /** 프레임 전송. 실패하면 연결을 닫고 false. */
    fun send(jpeg: ByteArray, length: Int, rotation: Int, front: Boolean): Boolean {
        synchronized(lock) {
            val o = out ?: return false
            return try {
                o.writeBytes("KWC1")
                o.writeShort(rotation)
                o.writeByte(if (front) 1 else 0)
                o.writeByte(0)
                o.writeInt(length)
                o.write(jpeg, 0, length)
                o.flush()
                true
            } catch (_: IOException) {
                closeLocked()
                false
            }
        }
    }

    fun close() {
        synchronized(lock) { closeLocked() }
    }

    private fun closeLocked() {
        try { socket?.close() } catch (_: IOException) {}
        socket = null
        out = null
    }
}
