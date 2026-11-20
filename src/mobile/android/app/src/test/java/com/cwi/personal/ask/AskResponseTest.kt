package com.cwi.personal.ask

import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import org.junit.Assert.assertEquals
import org.junit.Test

class AskResponseTest {
    @Test
    fun parsesTraceIdFromJson() {
        val json = """
            {
              "answer": "ok",
              "citations": [],
              "trace": {
                "trace_id": "trace-999",
                "policy_mode": "approve",
                "model": "local",
                "pack_hash": "abc"
              }
            }
        """.trimIndent()
        val adapter = Moshi.Builder()
            .add(KotlinJsonAdapterFactory())
            .build()
            .adapter(AskResponse::class.java)
        val response = adapter.fromJson(json)
        assertEquals("trace-999", response?.trace?.traceId)
        assertEquals("approve", response?.trace?.policyMode)
    }
}
