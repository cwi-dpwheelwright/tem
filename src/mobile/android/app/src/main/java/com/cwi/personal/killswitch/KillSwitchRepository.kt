package com.cwi.personal.killswitch

import android.content.Context
import com.cwi.personal.network.ApiClient

class KillSwitchRepository(context: Context) {
    private val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    fun loadCached(): KillSwitchStatus? {
        if (!prefs.contains(KEY_ENABLED) || !prefs.contains(KEY_CACHED_AT)) {
            return null
        }
        val enabled = prefs.getBoolean(KEY_ENABLED, false)
        val cachedAt = prefs.getLong(KEY_CACHED_AT, 0L)
        if (cachedAt <= 0L) {
            return null
        }
        return KillSwitchStatus(enabled = enabled, cachedAtMillis = cachedAt)
    }

    suspend fun refresh(): KillSwitchStatus {
        val response = ApiClient.api().killSwitchStatus()
        val now = System.currentTimeMillis()
        val status = KillSwitchStatus(enabled = response.enabled, cachedAtMillis = now)
        prefs.edit()
            .putBoolean(KEY_ENABLED, status.enabled)
            .putLong(KEY_CACHED_AT, status.cachedAtMillis)
            .apply()
        return status
    }

    fun isStale(maxAgeMinutes: Long): Boolean {
        val cached = loadCached() ?: return true
        val ageMinutes = (System.currentTimeMillis() - cached.cachedAtMillis) / 60000
        return ageMinutes >= maxAgeMinutes
    }

    companion object {
        private const val PREFS_NAME = "kill_switch_cache"
        private const val KEY_ENABLED = "enabled"
        private const val KEY_CACHED_AT = "cached_at"
    }
}
