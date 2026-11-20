package com.cwi.personal.cache

import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "pack_cache")
data class PackCacheEntity(
    @PrimaryKey val name: String,
    val anchor: String,
    val hash: String,
    val filePath: String,
    val savedAt: Long
)
