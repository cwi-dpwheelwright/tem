package com.cwi.personal.reports.cache

import android.content.Context
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary
import com.squareup.moshi.Moshi
import com.squareup.moshi.Types
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory

class ReportCacheRepository(context: Context) {
    private val dao = ReportCacheDatabase.getInstance(context).reportCacheDao()
    private val moshi = Moshi.Builder()
        .add(KotlinJsonAdapterFactory())
        .build()

    private val reportListAdapter = moshi.adapter<List<ReportSummary>>(
        Types.newParameterizedType(List::class.java, ReportSummary::class.java)
    )
    private val reportDetailAdapter = moshi.adapter(ReportDetail::class.java)
    private val documentListAdapter = moshi.adapter<List<DocumentSummary>>(
        Types.newParameterizedType(List::class.java, DocumentSummary::class.java)
    )
    private val documentDetailAdapter = moshi.adapter(DocumentDetail::class.java)

    suspend fun saveReportList(tenantId: String, reports: List<ReportSummary>) {
        val json = reportListAdapter.toJson(reports)
        dao.saveReportList(
            ReportListCacheEntity(
                tenantId = tenantId,
                json = json,
                cachedAtMillis = System.currentTimeMillis()
            )
        )
    }

    suspend fun loadReportList(tenantId: String): List<ReportSummary>? {
        val entity = dao.loadReportList(tenantId) ?: return null
        return reportListAdapter.fromJson(entity.json)
    }

    suspend fun saveReportDetail(tenantId: String, report: ReportDetail) {
        val json = reportDetailAdapter.toJson(report)
        dao.saveReportDetail(
            ReportDetailCacheEntity(
                tenantId = tenantId,
                reportId = report.reportId,
                json = json,
                cachedAtMillis = System.currentTimeMillis()
            )
        )
    }

    suspend fun loadReportDetail(tenantId: String, reportId: String): ReportDetail? {
        val entity = dao.loadReportDetail(tenantId, reportId) ?: return null
        return reportDetailAdapter.fromJson(entity.json)
    }

    suspend fun saveDocumentList(tenantId: String, documents: List<DocumentSummary>) {
        val json = documentListAdapter.toJson(documents)
        dao.saveDocumentList(
            DocumentListCacheEntity(
                tenantId = tenantId,
                json = json,
                cachedAtMillis = System.currentTimeMillis()
            )
        )
    }

    suspend fun loadDocumentList(tenantId: String): List<DocumentSummary>? {
        val entity = dao.loadDocumentList(tenantId) ?: return null
        return documentListAdapter.fromJson(entity.json)
    }

    suspend fun saveDocumentDetail(tenantId: String, document: DocumentDetail) {
        val json = documentDetailAdapter.toJson(document)
        dao.saveDocumentDetail(
            DocumentDetailCacheEntity(
                tenantId = tenantId,
                docId = document.docId,
                json = json,
                cachedAtMillis = System.currentTimeMillis()
            )
        )
    }

    suspend fun loadDocumentDetail(tenantId: String, docId: String): DocumentDetail? {
        val entity = dao.loadDocumentDetail(tenantId, docId) ?: return null
        return documentDetailAdapter.fromJson(entity.json)
    }
}
