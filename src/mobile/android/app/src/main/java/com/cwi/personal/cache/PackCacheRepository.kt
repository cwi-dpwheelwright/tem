package com.cwi.personal.cache

import android.content.Context
import com.cwi.personal.pack.AnchorVerifier
import com.cwi.personal.pack.PackDocument
import com.cwi.personal.pack.TrustState
import java.io.File

data class CachedPackResult(
    val pack: PackDocument?,
    val trustState: TrustState,
    val error: String? = null
)

class PackCacheRepository(private val context: Context) {
    private val dao = PackCacheDatabase.getInstance(context).packCacheDao()

    suspend fun save(pack: PackDocument): PackCacheEntity {
        val dir = File(context.filesDir, "packs")
        if (!dir.exists()) {
            dir.mkdirs()
        }
        val file = File(dir, "${pack.name}.json")
        file.writeText(pack.rawJson)

        val entity = PackCacheEntity(
            name = pack.name,
            anchor = pack.anchor,
            hash = pack.hash,
            filePath = file.absolutePath,
            savedAt = System.currentTimeMillis()
        )
        dao.upsert(entity)
        return entity
    }

    suspend fun load(name: String): PackDocument? {
        val entity = dao.getByName(name) ?: return null
        val file = File(entity.filePath)
        if (!file.exists()) return null
        val rawJson = file.readText()
        return PackDocument(
            name = entity.name,
            rawJson = rawJson,
            anchor = entity.anchor,
            hash = entity.hash
        )
    }

    suspend fun loadVerified(name: String): CachedPackResult {
        val entity = dao.getByName(name) ?: return CachedPackResult(null, TrustState.UNKNOWN, "No cached pack.")
        val file = File(entity.filePath)
        if (!file.exists()) {
            return CachedPackResult(null, TrustState.UNVERIFIED, "Cached pack missing on disk.")
        }
        val rawJson = file.readText()
        return try {
            val computed = AnchorVerifier.verify(rawJson, entity.anchor)
            val pack = PackDocument(
                name = entity.name,
                rawJson = rawJson,
                anchor = entity.anchor,
                hash = computed
            )
            CachedPackResult(pack, TrustState.VERIFIED, null)
        } catch (ex: Exception) {
            CachedPackResult(null, TrustState.UNVERIFIED, ex.message)
        }
    }

    suspend fun lastSavedAt(name: String): Long? {
        return dao.getByName(name)?.savedAt
    }
}
