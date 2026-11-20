package com.cwi.personal.interaction

import com.cwi.personal.BuildConfig
import com.cwi.personal.network.ApiService
import com.cwi.personal.telemetry.TelemetryRequest
import com.cwi.personal.telemetry.TelemetrySignal
import com.cwi.personal.telemetry.TelemetryWindow
import com.squareup.moshi.Moshi
import com.squareup.moshi.Types
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.delay
import java.time.Instant
import kotlin.math.min
import kotlin.random.Random

class InteractionUploader(
    private val api: ApiService,
    private val store: InteractionStore,
    private val sampleRate: Double = BuildConfig.INTERACTION_SAMPLE_RATE,
    private val maxBatchSize: Int = 25,
    private val maxAttempts: Int = 3,
    private val baseDelayMillis: Long = 1000
) {
    private val moshi = Moshi.Builder()
        .add(KotlinJsonAdapterFactory())
        .build()
    private val listType = Types.newParameterizedType(List::class.java, InteractionEvent::class.java)
    private val adapter = moshi.adapter<List<InteractionEvent>>(listType)

    fun shouldSample(): Boolean {
        if (sampleRate >= 1.0) return true
        if (sampleRate <= 0.0) return false
        return Random.nextDouble() <= sampleRate
    }

    suspend fun flush(tenantId: String, userId: String) {
        val batch = store.peek(maxBatchSize)
        if (batch.isEmpty()) return

        val eventsJson = adapter.toJson(batch)
        val now = Instant.now()
        val payload = TelemetryRequest(
            tenantId = tenantId,
            window = TelemetryWindow(start = now.toString(), end = now.toString()),
            signals = listOf(
                TelemetrySignal(tag = "interaction_batch", unit = "count", series = listOf(batch.size.toDouble()))
            ),
            metadata = mapOf(
                "event" to "interaction_batch",
                "user_id" to userId,
                "sample_rate" to sampleRate.toString(),
                "batch_size" to batch.size.toString(),
                "events_json" to eventsJson
            )
        )

        var attempt = 0
        while (attempt < maxAttempts) {
            try {
                api.ingestTelemetry(payload)
                store.remove(batch.map { it.eventId }.toSet())
                return
            } catch (_: Exception) {
                attempt += 1
                if (attempt >= maxAttempts) return
                val delayMillis = min(baseDelayMillis * (1L shl (attempt - 1)), 10000)
                delay(delayMillis)
            }
        }
    }
}
