package com.example.myapplication.data.gnss.model

data class GalileoInavMessage(
    val svid: Int,
    val type: Int,
    val typeName: String,
    val submessageId: Int,
    val messageId: Int,
    val status: Int,
    val rawBytes: ByteArray,
    val hexData: String,
    val timestampMillis: Long = System.currentTimeMillis()
) {
    val formattedSvid: String
        get() = "E${svid.toString().padStart(2, '0')}"

    val isGalileoInav: Boolean
        get() = typeName == "GAL_I" || type == 6

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (javaClass != other?.javaClass) return false
        other as GalileoInavMessage
        return svid == other.svid && timestampMillis == other.timestampMillis && hexData == other.hexData
    }

    override fun hashCode(): Int {
        var result = svid
        result = 31 * result + hexData.hashCode()
        result = 31 * result + timestampMillis.hashCode()
        return result
    }
}
