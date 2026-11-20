package com.cwi.personal.cache

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import com.cwi.personal.pack.PackDocument
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Before
import org.junit.Test

class PackCacheRepositoryTest {
    private lateinit var context: Context

    @Before
    fun setup() {
        context = ApplicationProvider.getApplicationContext()
        context.deleteDatabase("cwi-pack-cache.db")
    }

    @Test
    fun saveAndLoadPackFromCache() = runBlocking {
        val repository = PackCacheRepository(context)
        val pack = PackDocument(
            name = "test-pack",
            rawJson = """{"pack_id":"test","pack_version":"1.0.0","name":"Test","modules":[]}""",
            anchor = "anchor",
            hash = "hash"
        )
        repository.save(pack)
        val loaded = repository.load("test-pack")
        assertNotNull(loaded)
        assertEquals(pack.rawJson, loaded?.rawJson)
        assertEquals(pack.anchor, loaded?.anchor)
    }
}
