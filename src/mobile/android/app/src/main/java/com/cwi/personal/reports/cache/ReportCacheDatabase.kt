package com.cwi.personal.reports.cache

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(
    entities = [
        ReportListCacheEntity::class,
        ReportDetailCacheEntity::class,
        DocumentListCacheEntity::class,
        DocumentDetailCacheEntity::class
    ],
    version = 1,
    exportSchema = false
)
abstract class ReportCacheDatabase : RoomDatabase() {
    abstract fun reportCacheDao(): ReportCacheDao

    companion object {
        @Volatile private var instance: ReportCacheDatabase? = null

        fun getInstance(context: Context): ReportCacheDatabase {
            return instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(
                    context.applicationContext,
                    ReportCacheDatabase::class.java,
                    "cwi-report-cache.db"
                ).build().also { instance = it }
            }
        }
    }
}
