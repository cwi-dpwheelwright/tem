package com.cwi.personal.feedback

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class FeedbackStoreInstrumentedTest {
    @Test
    fun enqueuePeekRemoveFlow() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val store = FeedbackStore(context)
        clearStore(store)

        val request = OutcomeFeedbackRequest(
            tenantId = "tenant-1",
            traceId = "trace-1",
            actorId = "user-1",
            feedback = "up",
            consentGranted = true
        )
        val event = OutcomeFeedbackEvent(eventId = "fb-1", request = request)

        store.enqueue(event)
        assertEquals(1, store.size())
        assertEquals(1, store.peek(10).size)

        store.remove(setOf("fb-1"))
        assertEquals(0, store.size())
    }

    private fun clearStore(store: FeedbackStore) {
        val pending = store.peek(1000)
        if (pending.isNotEmpty()) {
            store.remove(pending.map { it.eventId }.toSet())
        }
    }
}
