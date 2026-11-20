package com.cwi.personal.feedback

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.cwi.personal.BuildConfig
import com.squareup.moshi.Moshi
import com.squareup.moshi.Types
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory

class FeedbackStore(context: Context) {
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val prefs = EncryptedSharedPreferences.create(
        context,
        "cwi_feedback_store",
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    private val moshi = Moshi.Builder()
        .add(KotlinJsonAdapterFactory())
        .build()
    private val listType = Types.newParameterizedType(List::class.java, OutcomeFeedbackEvent::class.java)
    private val adapter = moshi.adapter<List<OutcomeFeedbackEvent>>(listType)
    private val lock = Any()

    fun enqueue(event: OutcomeFeedbackEvent) {
        synchronized(lock) {
            val events = loadLocked().toMutableList()
            events.add(event)
            val capped = events.takeLast(BuildConfig.FEEDBACK_QUEUE_LIMIT)
            saveLocked(capped)
        }
    }

    fun peek(maxItems: Int): List<OutcomeFeedbackEvent> {
        synchronized(lock) {
            return loadLocked().take(maxItems)
        }
    }

    fun remove(eventIds: Set<String>) {
        synchronized(lock) {
            val remaining = loadLocked().filterNot { eventIds.contains(it.eventId) }
            saveLocked(remaining)
        }
    }

    fun size(): Int {
        synchronized(lock) {
            return loadLocked().size
        }
    }

    private fun loadLocked(): List<OutcomeFeedbackEvent> {
        val raw = prefs.getString(KEY_QUEUE, null) ?: return emptyList()
        return adapter.fromJson(raw) ?: emptyList()
    }

    private fun saveLocked(events: List<OutcomeFeedbackEvent>) {
        prefs.edit().putString(KEY_QUEUE, adapter.toJson(events)).apply()
    }

    companion object {
        private const val KEY_QUEUE = "feedback_queue"
    }
}
