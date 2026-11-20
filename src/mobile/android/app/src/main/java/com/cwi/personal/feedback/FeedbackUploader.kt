package com.cwi.personal.feedback

import com.cwi.personal.network.ApiService
import kotlinx.coroutines.delay
import kotlin.math.min

class FeedbackUploader(
    private val api: ApiService,
    private val store: FeedbackStore,
    private val maxBatchSize: Int = 10,
    private val maxAttempts: Int = 3,
    private val baseDelayMillis: Long = 1000
) {
    suspend fun flush() {
        val batch = store.peek(maxBatchSize)
        if (batch.isEmpty()) return

        for (event in batch) {
            var attempt = 0
            var sent = false
            while (attempt < maxAttempts && !sent) {
                try {
                    api.recordFeedback(event.request)
                    sent = true
                } catch (_: Exception) {
                    attempt += 1
                    if (attempt >= maxAttempts) break
                    val delayMillis = min(baseDelayMillis * (1L shl (attempt - 1)), 10000)
                    delay(delayMillis)
                }
            }
        }
        store.remove(batch.map { it.eventId }.toSet())
    }
}
