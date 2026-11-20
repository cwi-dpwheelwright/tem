package com.cwi.personal.reports

import com.squareup.moshi.Json

data class DocumentSummary(
    @Json(name = "doc_id") val docId: String,
    val title: String,
    val type: String,
    val owner: String,
    val status: String,
    @Json(name = "versions_count") val versionsCount: Int? = null,
    @Json(name = "latest_version") val latestVersion: String? = null
)

data class DocumentVersion(
    val version: String,
    @Json(name = "created_utc") val createdUtc: String,
    val author: String,
    val checksum: String,
    val notes: String? = null,
    val content: String? = null
)

data class DocumentDetail(
    @Json(name = "doc_id") val docId: String,
    val title: String,
    val type: String,
    val owner: String,
    val status: String,
    val versions: List<DocumentVersion>
)
