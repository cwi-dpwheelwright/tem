package com.cwi.personal.reports.cache

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.cwi.personal.reports.DocumentDetail
import com.cwi.personal.reports.DocumentSummary
import com.cwi.personal.reports.DocumentVersion
import com.cwi.personal.reports.ReportDetail
import com.cwi.personal.reports.ReportSummary
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ReportCacheRepositoryInstrumentedTest {
    @Test
    fun saveAndLoadReportsAndDocuments() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val repository = ReportCacheRepository(context)
        val tenantId = "tenant-1"

        val reportSummary = ReportSummary(
            reportId = "report-1",
            title = "Ops Summary",
            createdUtc = "2026-02-01T10:00:00Z",
            status = "open",
            coherenceScore = 0.9,
            conflictCount = 0
        )
        repository.saveReportList(tenantId, listOf(reportSummary))
        val reports = repository.loadReportList(tenantId)
        assertNotNull(reports)
        assertEquals(1, reports!!.size)
        assertEquals("report-1", reports.first().reportId)

        val reportDetail = ReportDetail(
            reportId = "report-1",
            title = "Ops Summary",
            createdUtc = "2026-02-01T10:00:00Z",
            status = "open",
            coherenceScore = 0.9,
            conflictCount = 0,
            narrative = "Details"
        )
        repository.saveReportDetail(tenantId, reportDetail)
        val loadedReport = repository.loadReportDetail(tenantId, "report-1")
        assertNotNull(loadedReport)
        assertEquals("report-1", loadedReport!!.reportId)

        val documentSummary = DocumentSummary(
            docId = "doc-1",
            title = "Runbook",
            type = "ops",
            owner = "team",
            status = "active",
            versionsCount = 2,
            latestVersion = "v2"
        )
        repository.saveDocumentList(tenantId, listOf(documentSummary))
        val documents = repository.loadDocumentList(tenantId)
        assertNotNull(documents)
        assertEquals(1, documents!!.size)
        assertEquals("doc-1", documents.first().docId)

        val documentDetail = DocumentDetail(
            docId = "doc-1",
            title = "Runbook",
            type = "ops",
            owner = "team",
            status = "active",
            versions = listOf(
                DocumentVersion(
                    version = "v2",
                    createdUtc = "2026-02-01T09:00:00Z",
                    author = "ops",
                    checksum = "abc"
                )
            )
        )
        repository.saveDocumentDetail(tenantId, documentDetail)
        val loadedDocument = repository.loadDocumentDetail(tenantId, "doc-1")
        assertNotNull(loadedDocument)
        assertEquals("doc-1", loadedDocument!!.docId)
    }
}
