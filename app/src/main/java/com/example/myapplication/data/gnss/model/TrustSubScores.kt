package com.example.myapplication.data.gnss.model

data class TrustSubScores(
    val signalAuthenticityScore: Float = 100f,
    val agcInterferenceScore: Float = 100f,
    val constellationDiversityScore: Float = 100f,
    val geometryDopScore: Float = 100f,
    val clockStabilityScore: Float = 100f,
    val osnmaAuthScore: Float = 100f
) {
    val weightedOverallScore: Float
        get() = (signalAuthenticityScore * 0.25f) +
                (agcInterferenceScore * 0.20f) +
                (constellationDiversityScore * 0.15f) +
                (geometryDopScore * 0.15f) +
                (clockStabilityScore * 0.10f) +
                (osnmaAuthScore * 0.15f)
}
