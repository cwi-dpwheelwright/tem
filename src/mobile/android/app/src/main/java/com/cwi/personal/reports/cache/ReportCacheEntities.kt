package com.cwi.personal.reports.cache

import androidx.room.Entity

@Entity(tableName = "report_list_cache", primaryKeys = ["tenantId"])
data class ReportListCacheEntity(
    val tenantId: String,
    val json: String,
    val cachedAtMillis: Long
)

@Entity(tableName = "report_detail_cache", primaryKeys = ["tenantId", "reportId"])
data class ReportDetailCacheEntity(
    val tenantId: String,
    val reportId: String,
    val json: String,
    val cachedAtMillis: Long
)

@Entity(tableName = "document_list_cache", primaryKeys = ["tenantId"])
data class DocumentListCacheEntity(
    val tenantId: String,
    val json: String,
    val cachedAtMillis: Long
)

@Entity(tableName = "document_detail_cache", primaryKeys = ["tenantId", "docId"])
data class DocumentDetailCacheEntity(
    val tenantId: String,
    val docId: String,
    val json: String,
    val cachedAtMillis: Long
)
