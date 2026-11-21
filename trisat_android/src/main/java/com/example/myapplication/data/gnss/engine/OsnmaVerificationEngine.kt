package com.example.myapplication.data.gnss.engine

import com.example.myapplication.data.gnss.model.GalileoInavMessage
import com.example.myapplication.data.gnss.model.OsnmaStatus

class OsnmaVerificationEngine {

    /**
     * Evaluates Galileo OSNMA status from live received I/NAV pages and optional Assisted OSNMA results.
     * Preserves strict provenance: live pages remain UNAUTHENTICATED unless verified by Assisted OSNMA.
     */
    fun verifyOsnma(
        inavMessages: List<GalileoInavMessage>,
        assistedState: AssistedOsnmaUiState? = null
    ): OsnmaStatus {
        if (assistedState != null) {
            if (assistedState.mismatchCount > 0 || assistedState.isTimeSpoofingSuspected) {
                return OsnmaStatus.FAILED
            }
            if (assistedState.matchedCount > 0) {
                return OsnmaStatus.BITWISE_VERIFIED
            }
        }
        return if (inavMessages.any { it.isGalileoInav && it.rawBytes.isNotEmpty() }) {
            OsnmaStatus.UNAUTHENTICATED
        } else {
            OsnmaStatus.NOT_AVAILABLE
        }
    }
}
