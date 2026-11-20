package com.cwi.personal.cache

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(entities = [PackCacheEntity::class], version = 1, exportSchema = false)
abstract class PackCacheDatabase : RoomDatabase() {
    abstract fun packCacheDao(): PackCacheDao

    companion object {
        @Volatile private var instance: PackCacheDatabase? = null

        fun getInstance(context: Context): PackCacheDatabase {
            return instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(
                    context.applicationContext,
                    PackCacheDatabase::class.java,
                    "cwi-pack-cache.db"
                ).build().also { instance = it }
            }
        }
    }
}
