package com.cwi.personal.reports.cache

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface ReportCacheDao {
    @Query("SELECT * FROM report_list_cache WHERE tenantId = :tenantId LIMIT 1")
    suspend fun loadReportList(tenantId: String): ReportListCacheEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun saveReportList(entity: ReportListCacheEntity)

    @Query("SELECT * FROM report_detail_cache WHERE tenantId = :tenantId AND reportId = :reportId LIMIT 1")
    suspend fun loadReportDetail(tenantId: String, reportId: String): ReportDetailCacheEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun saveReportDetail(entity: ReportDetailCacheEntity)

    @Query("SELECT * FROM document_list_cache WHERE tenantId = :tenantId LIMIT 1")
    suspend fun loadDocumentList(tenantId: String): DocumentListCacheEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun saveDocumentList(entity: DocumentListCacheEntity)

    @Query("SELECT * FROM document_detail_cache WHERE tenantId = :tenantId AND docId = :docId LIMIT 1")
    suspend fun loadDocumentDetail(tenantId: String, docId: String): DocumentDetailCacheEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun saveDocumentDetail(entity: DocumentDetailCacheEntity)
}
