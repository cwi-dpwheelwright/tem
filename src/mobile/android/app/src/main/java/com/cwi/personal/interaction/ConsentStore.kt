package com.cwi.personal.interaction

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

class ConsentStore(context: Context) {
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val prefs = EncryptedSharedPreferences.create(
        context,
        "cwi_consent_store",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    fun isGranted(): Boolean = prefs.getBoolean(KEY_CONSENT, false)

    fun setGranted(granted: Boolean) {
        prefs.edit().putBoolean(KEY_CONSENT, granted).apply()
    }

    companion object {
        private const val KEY_CONSENT = "consent_granted"
    }
}
