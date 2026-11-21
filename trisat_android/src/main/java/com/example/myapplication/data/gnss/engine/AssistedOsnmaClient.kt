package com.example.myapplication.data.gnss.engine

import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL
import java.util.Locale

/**
 * Galileo System Time (GST: Week Number since 1999-08-22 00:00:00 UTC + Time of Week in seconds).
 */
data class GstTime(
    val weekNumber: Int,
    val towSeconds: Int
) {
    val totalSeconds: Long
        get() = weekNumber.toLong() * SECONDS_PER_WEEK + towSeconds.toLong()

    val formatted: String
        get() = String.format(Locale.US, "WN %d · TOW %06ds", weekNumber, towSeconds)

    companion object {
        const val SECONDS_PER_WEEK = 604_800
        const val DEFAULT_LEAP_SECONDS = 18
        const val GALILEO_EPOCH_UNIX_SECONDS = 935_280_000L // 1999-08-22T00:00:00Z

        fun fromUtcMillis(utcMillis: Long, leapSeconds: Int = DEFAULT_LEAP_SECONDS): GstTime {
            val elapsedSec = (utcMillis / 1000L) - GALILEO_EPOCH_UNIX_SECONDS + leapSeconds
            if (elapsedSec < 0L) return GstTime(0, 0)
            val wn = (elapsedSec / SECONDS_PER_WEEK).toInt()
            val tow = (elapsedSec % SECONDS_PER_WEEK).toInt()
            return GstTime(wn, tow)
        }

        fun fromTotalSeconds(totalSeconds: Long): GstTime {
            if (totalSeconds < 0L) return GstTime(0, 0)
            return GstTime(
                weekNumber = (totalSeconds / SECONDS_PER_WEEK).toInt(),
                towSeconds = (totalSeconds % SECONDS_PER_WEEK).toInt()
            )
        }
    }
}

data class ParsedGalileoINavPage(
    val svid: Int,
    val wordType: Int,
    val hkrootHex: String,
    val mackHex: String,
    val osnma40Hex: String,
    val crcValid: Boolean,
    val decodedGst: GstTime?,
    val decodedTowOnly: Int?
) {
    val hasNonZeroOsnma: Boolean
        get() = osnma40Hex != "0000000000"
}

/**
 * Clean-room bit-exact Galileo E1-B I/NAV nominal page parser per Galileo OS SIS ICD v2.1.
 * Supports Android GnssNavigationMessage TYPE_GAL_I (240-bit nominal page) and 228-bit UBX-RXM-SFRBX.
 */
object GalileoINavBitParser {
    private const val CRC24Q_POLY = 0x1864CFB

    fun computeCrc24q(bytes: ByteArray): Int {
        var crc = 0
        for (b in bytes) {
            crc = crc xor ((b.toInt() and 0xFF) shl 16)
            for (i in 0 until 8) {
                crc = crc shl 1
                if ((crc and 0x1000000) != 0) {
                    crc = crc xor CRC24Q_POLY
                }
            }
        }
        return crc and 0xFFFFFF
    }

    fun parseFull240Bit(svid: Int, data: ByteArray): ParsedGalileoINavPage? {
        if (svid !in 1..36 || data.size < 30) return null
        return parseLayout(svid, data, oddOffsetBits = 120)
    }

    fun parseUbxSfrbx228(svid: Int, data: ByteArray): ParsedGalileoINavPage? {
        if (svid !in 1..36 || data.size < 32) return null
        val msb = ByteArray(32)
        for (w in 0 until 8) {
            val base = w * 4
            msb[base + 0] = data[base + 3]
            msb[base + 1] = data[base + 2]
            msb[base + 2] = data[base + 1]
            msb[base + 3] = data[base + 0]
        }
        return parseLayout(svid, msb, oddOffsetBits = 114)
    }

    fun buildFull240BitNominalPage(
        data128: ByteArray,
        hkrootByte: Byte,
        mack4: ByteArray,
        sar22: Int = 0,
        sspByte: Byte = 0x04
    ): ByteArray {
        require(data128.size == 16) { "data128 must be 16 bytes" }
        require(mack4.size == 4) { "mack4 must be 4 bytes" }

        val page = ByteArray(30)
        copyBits(data128, 0, page, 2, 112)
        setBit(page, 120, 1)
        copyBits(data128, 112, page, 122, 16)

        val osnma = ByteArray(5)
        osnma[0] = hkrootByte
        System.arraycopy(mack4, 0, osnma, 1, 4)
        copyBits(osnma, 0, page, 138, 40)
        writeBits(page, 178, 22, sar22.toLong())

        val crcInput = ByteArray(25)
        copyBits(page, 0, crcInput, 4, 114)
        copyBits(page, 120, crcInput, 118, 82)
        val crc = computeCrc24q(crcInput)

        writeBits(page, 202, 24, crc.toLong())
        writeBits(page, 226, 8, (sspByte.toInt() and 0xFF).toLong())
        return page
    }

    fun buildWordType5(wn: Int, tow: Int): ByteArray {
        val word = ByteArray(16)
        writeBits(word, 0, 6, 5L)
        writeBits(word, 73, 12, (wn and 0xFFF).toLong())
        writeBits(word, 85, 20, (tow and 0xFFFFF).toLong())
        return word
    }

    private fun parseLayout(svid: Int, bits: ByteArray, oddOffsetBits: Int): ParsedGalileoINavPage {
        val evenFlag = getBit(bits, 0)
        val oddFlag = getBit(bits, oddOffsetBits)

        val data128 = ByteArray(16)
        copyBits(bits, 2, data128, 0, 112)
        copyBits(bits, oddOffsetBits + 2, data128, 112, 16)

        val wordType = readBits(data128, 0, 6).toInt()

        val osnma40 = ByteArray(5)
        copyBits(bits, oddOffsetBits + 18, osnma40, 0, 40)

        val crcReceived = readBits(bits, oddOffsetBits + 82, 24).toInt()

        val crcInput = ByteArray(25)
        copyBits(bits, 0, crcInput, 4, 114)
        copyBits(bits, oddOffsetBits, crcInput, 118, 82)
        val crcComputed = computeCrc24q(crcInput)

        val crcValid = evenFlag == 0 && oddFlag == 1 && crcReceived == crcComputed
        val (gst, towOnly) = decodeGstFromWord(wordType, data128)

        val hkHex = String.format(Locale.US, "%02X", osnma40[0].toInt() and 0xFF)
        val mackHex = (1..4).joinToString("") { idx ->
            String.format(Locale.US, "%02X", osnma40[idx].toInt() and 0xFF)
        }
        return ParsedGalileoINavPage(
            svid = svid,
            wordType = wordType,
            hkrootHex = hkHex,
            mackHex = mackHex,
            osnma40Hex = hkHex + mackHex,
            crcValid = crcValid,
            decodedGst = gst,
            decodedTowOnly = towOnly
        )
    }

    private fun decodeGstFromWord(wordType: Int, data128: ByteArray): Pair<GstTime?, Int?> {
        return when (wordType) {
            0 -> {
                val timeFlag = readBits(data128, 6, 2).toInt()
                if (timeFlag == 2) {
                    val wn = readBits(data128, 96, 12).toInt()
                    val tow = readBits(data128, 108, 20).toInt()
                    if (tow in 0 until GstTime.SECONDS_PER_WEEK) Pair(GstTime(wn, tow), tow) else Pair(null, null)
                } else Pair(null, null)
            }
            5 -> {
                val wn = readBits(data128, 73, 12).toInt()
                val tow = readBits(data128, 85, 20).toInt()
                if (tow in 0 until GstTime.SECONDS_PER_WEEK) Pair(GstTime(wn, tow), tow) else Pair(null, null)
            }
            6 -> {
                val tow = readBits(data128, 105, 20).toInt()
                if (tow in 0 until GstTime.SECONDS_PER_WEEK) Pair(null, tow) else Pair(null, null)
            }
            else -> Pair(null, null)
        }
    }

    private fun getBit(data: ByteArray, index: Int): Int =
        ((data[index ushr 3].toInt() ushr (7 - (index and 7))) and 1)

    private fun setBit(data: ByteArray, index: Int, value: Int) {
        val byteIdx = index ushr 3
        val mask = 1 shl (7 - (index and 7))
        data[byteIdx] = if (value != 0) {
            (data[byteIdx].toInt() or mask).toByte()
        } else {
            (data[byteIdx].toInt() and mask.inv()).toByte()
        }
    }

    private fun copyBits(src: ByteArray, srcOff: Int, dst: ByteArray, dstOff: Int, len: Int) {
        for (i in 0 until len) setBit(dst, dstOff + i, getBit(src, srcOff + i))
    }

    private fun readBits(data: ByteArray, offset: Int, len: Int): Long {
        var v = 0L
        for (i in 0 until len) v = (v shl 1) or getBit(data, offset + i).toLong()
        return v
    }

    private fun writeBits(data: ByteArray, offset: Int, len: Int, value: Long) {
        for (i in 0 until len) {
            val bit = ((value ushr (len - 1 - i)) and 1L).toInt()
            setBit(data, offset + i, bit)
        }
    }
}

data class AssistedOsnmaGstResponse(
    val weekNumber: Int,
    val towSeconds: Int,
    val totalSeconds: Long,
    val utcIso: String,
    val clockSource: String,
    val isNetworkSynced: Boolean
)

data class AssistedOsnmaBitwiseResult(
    val svid: Int,
    val gstSeconds: Long,
    val hasReference: Boolean,
    val matched: Boolean,
    val matchingBits: Int,
    val totalBits: Int,
    val localOsnmaHex: String,
    val referenceOsnmaHex: String?,
    val reason: String
)

data class AssistedOsnmaUiState(
    val serverUrl: String = "http://192.168.137.1:8095",
    val isConnected: Boolean = false,
    val statusText: String = "Disconnected",
    val gstBanner: String = "Independent GST: Unsynced",
    val bitwiseBanner: String = "Bitwise Cross-Check: Standby",
    val matchedCount: Int = 0,
    val mismatchCount: Int = 0,
    val isTimeSpoofingSuspected: Boolean = false,
    val lastReason: String? = null
)

/**
 * Lightweight open-source HTTP JSON client for TRISAT Assisted OSNMA
 * (independent GST sync, fast KROOT bootstrap, and 2-second 40-bit Bitwise cross-check).
 */
class AssistedOsnmaClient(private val timeoutMs: Int = 4000) {

    fun fetchGst(baseUrl: String): AssistedOsnmaGstResponse {
        val body = httpGet("${normalize(baseUrl)}/api/osnma/gst")
        val obj = JSONObject(body)
        return AssistedOsnmaGstResponse(
            weekNumber = obj.optInt("weekNumber"),
            towSeconds = obj.optInt("towSeconds"),
            totalSeconds = obj.optLong("totalSeconds"),
            utcIso = obj.optString("utcIso"),
            clockSource = obj.optString("clockSource", "Assisted OSNMA"),
            isNetworkSynced = obj.optBoolean("isNetworkSynced", false)
        )
    }

    fun checkBitwise(baseUrl: String, svid: Int, gstSeconds: Long, osnma40Hex: String): AssistedOsnmaBitwiseResult {
        val reqJson = JSONObject().apply {
            put("svid", svid)
            put("gstSeconds", gstSeconds)
            put("osnmaHex", osnma40Hex)
        }.toString()
        val body = httpPost("${normalize(baseUrl)}/api/osnma/bitwise-check", reqJson)
        val obj = JSONObject(body)
        return AssistedOsnmaBitwiseResult(
            svid = obj.optInt("svid", svid),
            gstSeconds = obj.optLong("gstSeconds", gstSeconds),
            hasReference = obj.optBoolean("hasReference", false),
            matched = obj.optBoolean("matched", false),
            matchingBits = obj.optInt("matchingBits", 0),
            totalBits = obj.optInt("totalBits", 40),
            localOsnmaHex = obj.optString("localOsnmaHex", osnma40Hex),
            referenceOsnmaHex = obj.optString("referenceOsnmaHex", null),
            reason = obj.optString("reason", "")
        )
    }

    private fun normalize(baseUrl: String): String = baseUrl.trim().trimEnd('/')

    private fun httpGet(urlStr: String): String {
        val conn = (URL(urlStr).openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = timeoutMs
            readTimeout = timeoutMs
        }
        try {
            val code = conn.responseCode
            if (code !in 200..299) throw IllegalStateException("HTTP $code")
            return BufferedReader(InputStreamReader(conn.inputStream)).use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }

    private fun httpPost(urlStr: String, jsonBody: String): String {
        val conn = (URL(urlStr).openConnection() as HttpURLConnection).apply {
            requestMethod = "POST"
            doOutput = true
            connectTimeout = timeoutMs
            readTimeout = timeoutMs
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
        }
        try {
            OutputStreamWriter(conn.outputStream, Charsets.UTF_8).use { it.write(jsonBody) }
            val code = conn.responseCode
            if (code !in 200..299) throw IllegalStateException("HTTP $code")
            return BufferedReader(InputStreamReader(conn.inputStream)).use { it.readText() }
        } finally {
            conn.disconnect()
        }
    }
}
