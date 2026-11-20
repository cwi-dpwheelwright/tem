package com.cwi.personal.cache

import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.cwi.personal.pack.PackDocument
import com.cwi.personal.pack.TrustState
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class PackCacheVerificationTest {
    @Test
    fun loadVerifiedReturnsVerifiedForValidAnchor() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        context.deleteDatabase("cwi-pack-cache.db")
        val repository = PackCacheRepository(context)
        val rawJson = """{"b":2,"a":"x","arr":[1,true,null,{"c":"d"}]}"""
        val anchor = "89c64b6e245755a6983d59750510eb8b1e2910eda8057efb74ff03389d8700be"
        val pack = PackDocument(
            name = "verified-pack",
            rawJson = rawJson,
            anchor = anchor,
            hash = anchor
        )

        repository.save(pack)
        val result = repository.loadVerified("verified-pack")

        assertEquals(TrustState.VERIFIED, result.trustState)
        assertNotNull(result.pack)
    }

    @Test
    fun loadVerifiedReturnsUnverifiedOnAnchorMismatch() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<android.content.Context>()
        context.deleteDatabase("cwi-pack-cache.db")
        val repository = PackCacheRepository(context)
        val pack = PackDocument(
            name = "invalid-pack",
            rawJson = "{\"name\":\"invalid\"}",
            anchor = "deadbeef",
            hash = "deadbeef"
        )

        repository.save(pack)
        val result = repository.loadVerified("invalid-pack")

        assertEquals(TrustState.UNVERIFIED, result.trustState)
    }
}
