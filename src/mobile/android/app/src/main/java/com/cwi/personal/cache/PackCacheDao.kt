package com.cwi.personal.cache

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface PackCacheDao {
    @Query("SELECT * FROM pack_cache WHERE name = :name LIMIT 1")
    suspend fun getByName(name: String): PackCacheEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(entity: PackCacheEntity)
}
