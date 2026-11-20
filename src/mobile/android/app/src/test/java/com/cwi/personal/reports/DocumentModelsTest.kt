package com.cwi.personal.reports

import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class DocumentModelsTest {
    private val moshi = Moshi.Builder().add(KotlinJsonAdapterFactory()).build()

    @Test
    fun parsesDocumentSummary() {
        val json = """
            {
              "doc_id": "doc-1",
              "title": "Policy",
              "type": "policy",
              "owner": "Compliance",
              "status": "active",
              "versions_count": 2,
              "latest_version": "2.0.0"
            }
        """.trimIndent()
        val adapter = moshi.adapter(DocumentSummary::class.java)
        val summary = adapter.fromJson(json)
        assertEquals("doc-1", summary?.docId)
        assertEquals("2.0.0", summary?.latestVersion)
    }

    @Test
    fun parsesDocumentDetail() {
        val json = """
            {
              "doc_id": "doc-1",
              "title": "Policy",
              "type": "policy",
              "owner": "Compliance",
              "status": "active",
              "versions": [
                {
                  "version": "2.0.0",
                  "created_utc": "2026-02-01T10:20:00Z",
                  "author": "ops",
                  "checksum": "abc",
                  "notes": "Updated",
                  "content": "Content"
                }
              ]
            }
        """.trimIndent()
        val adapter = moshi.adapter(DocumentDetail::class.java)
        val detail = adapter.fromJson(json)
        assertEquals("doc-1", detail?.docId)
        assertNotNull(detail?.versions?.first()?.content)
    }
}
