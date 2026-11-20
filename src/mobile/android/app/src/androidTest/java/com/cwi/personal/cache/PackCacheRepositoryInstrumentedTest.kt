package com.cwi.personal.cache

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.cwi.personal.pack.PackDocument
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class PackCacheRepositoryInstrumentedTest {
    @Test
    fun saveAndLoadPack() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        val repository = PackCacheRepository(context)
        val pack = PackDocument(
            name = "enterprise",
            rawJson = "{\"name\":\"enterprise\"}",
            anchor = "anchor",
            hash = "hash"
        )

        repository.save(pack)
        val loaded = repository.load("enterprise")

        assertNotNull(loaded)
        assertEquals(pack.name, loaded!!.name)
        assertEquals(pack.anchor, loaded.anchor)
        assertEquals(pack.hash, loaded.hash)
    }
}
