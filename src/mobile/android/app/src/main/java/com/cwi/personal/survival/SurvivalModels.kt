package com.cwi.personal.survival

import com.squareup.moshi.Json

data class SurvivalStatusResponse(
    val mode: String,
    val profile: String,
    @Json(name = "last_signal_utc") val lastSignalUtc: String? = null,
    @Json(name = "offline_message") val offlineMessage: String
)

data class OfflineResponse(
    val mode: String,
    val message: String
)
