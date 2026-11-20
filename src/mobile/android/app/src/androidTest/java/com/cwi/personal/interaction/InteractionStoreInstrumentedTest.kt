package com.cwi.personal.interaction

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class InteractionStoreInstrumentedTest {
    @Test
    fun enqueuePeekRemoveFlow() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val store = InteractionStore(context)
        clearStore(store)

        val first = InteractionEvent(
            eventId = "event-1",
            tenantId = "tenant-1",
            userId = "user-1",
            traceId = "trace-1",
            actionId = null,
            eventType = "ask",
            createdUtc = "2026-02-02T00:00:00Z",
            metadata = mapOf("source" to "ui")
        )
        val second = InteractionEvent(
            eventId = "event-2",
            tenantId = "tenant-1",
            userId = "user-1",
            traceId = "trace-2",
            actionId = "action-1",
            eventType = "action",
            createdUtc = "2026-02-02T00:01:00Z"
        )

        store.enqueue(first)
        store.enqueue(second)

        assertEquals(2, store.size())
        assertEquals(1, store.peek(1).size)

        store.remove(setOf("event-1", "event-2"))
        assertEquals(0, store.size())
    }

    private fun clearStore(store: InteractionStore) {
        val pending = store.peek(1000)
        if (pending.isNotEmpty()) {
            store.remove(pending.map { it.eventId }.toSet())
        }
    }
}
