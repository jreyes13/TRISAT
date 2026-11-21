package com.example.myapplication

import com.example.myapplication.data.gnss.engine.AssistedOsnmaUiState
import com.example.myapplication.data.gnss.engine.GalileoINavBitParser
import com.example.myapplication.data.gnss.engine.GstTime
import com.example.myapplication.data.gnss.engine.OsnmaVerificationEngine
import com.example.myapplication.data.gnss.model.GalileoInavMessage
import com.example.myapplication.data.gnss.model.OsnmaStatus
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AssistedOsnmaTest {

    @Test
    fun testGalileoINav240BitParser_DecodesCrc24qOsnmaAndGst() {
        val wt5 = GalileoINavBitParser.buildWordType5(wn = 1248, tow = 345660)
        val page = GalileoINavBitParser.buildFull240BitNominalPage(
            data128 = wt5,
            hkrootByte = 0x82.toByte(),
            mack4 = byteArrayOf(0x7B, 0xB2.toByte(), 0x38, 0xC8.toByte())
        )

        val parsed = GalileoINavBitParser.parseFull240Bit(svid = 2, data = page)
        assertNotNull(parsed)
        assertTrue(parsed!!.crcValid)
        assertEquals(5, parsed.wordType)
        assertEquals("82", parsed.hkrootHex)
        assertEquals("7BB238C8", parsed.mackHex)
        assertEquals("827BB238C8", parsed.osnma40Hex)
        assertEquals(GstTime(1248, 345660), parsed.decodedGst)

        // Flip 1 bit -> CRC-24Q must fail
        val tampered = page.copyOf()
        tampered[12] = (tampered[12].toInt() xor 0x01).toByte()
        val parsedBad = GalileoINavBitParser.parseFull240Bit(svid = 2, data = tampered)
        assertNotNull(parsedBad)
        assertFalse(parsedBad!!.crcValid)
    }

    @Test
    fun testOsnmaVerificationEngine_AssistedBitwiseAndSpoofingTransitions() {
        val engine = OsnmaVerificationEngine()
        val sampleMsg = GalileoInavMessage(
            svid = 2,
            type = 6,
            typeName = "GAL_I",
            submessageId = 5,
            messageId = 0,
            status = 1,
            rawBytes = byteArrayOf(0x01, 0x02, 0x03),
            hexData = "010203"
        )

        // Without Assisted OSNMA results -> remains UNAUTHENTICATED
        assertEquals(OsnmaStatus.UNAUTHENTICATED, engine.verifyOsnma(listOf(sampleMsg)))

        // With Bitwise match -> BITWISE_VERIFIED
        val matchedState = AssistedOsnmaUiState(isConnected = true, matchedCount = 3, mismatchCount = 0)
        assertEquals(OsnmaStatus.BITWISE_VERIFIED, engine.verifyOsnma(listOf(sampleMsg), matchedState))

        // With Bitwise mismatch -> FAILED
        val spoofedState = AssistedOsnmaUiState(isConnected = true, matchedCount = 3, mismatchCount = 1)
        assertEquals(OsnmaStatus.FAILED, engine.verifyOsnma(listOf(sampleMsg), spoofedState))

        // With GST time-spoofing -> FAILED
        val timeSpoofState = AssistedOsnmaUiState(isConnected = true, matchedCount = 3, isTimeSpoofingSuspected = true)
        assertEquals(OsnmaStatus.FAILED, engine.verifyOsnma(listOf(sampleMsg), timeSpoofState))
    }
}
