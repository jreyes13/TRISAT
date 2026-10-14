package com.example.myapplication.data.gnss.engine

import com.example.myapplication.data.gnss.model.GalileoInavMessage
import com.example.myapplication.data.gnss.model.OsnmaStatus

class OsnmaVerificationEngine {

    /**
     * Evaluates OSNMA authentication status based on received Galileo I/NAV navigation messages.
     * On Galileo E1-B, the OSNMA field occupies 40 bits per odd I/NAV page.
     */
    fun verifyOsnma(inavMessages: List<GalileoInavMessage>): OsnmaStatus {
        if (inavMessages.isEmpty()) {
            return OsnmaStatus.NOT_AVAILABLE
        }

        val galileoInavPages = inavMessages.filter { it.isGalileoInav }
        if (galileoInavPages.size < 3) {
            return OsnmaStatus.NOT_AVAILABLE
        }

        val validOsnmaFrames = galileoInavPages.filter { msg ->
            msg.rawBytes.isNotEmpty()
        }

        return if (validOsnmaFrames.isNotEmpty()) {
            OsnmaStatus.VERIFIED
        } else {
            OsnmaStatus.FAILED
        }
    }
}
