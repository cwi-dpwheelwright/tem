package com.cwi.personal.killswitch

data class KillSwitchStatus(
    val enabled: Boolean,
    val cachedAtMillis: Long
)

data class KillSwitchStatusResponse(
    val enabled: Boolean
)
