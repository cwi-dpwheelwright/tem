package com.cwi.personal.pack

import org.junit.Assert.assertEquals
import org.junit.Test

class AnchorVerifierTest {
    @Test
    fun verifyMatchesExpectedAnchor() {
        val rawJson = """{"b":2,"a":"x","arr":[1,true,null,{"c":"d"}]}"""
        val expected = "89c64b6e245755a6983d59750510eb8b1e2910eda8057efb74ff03389d8700be"
        val computed = AnchorVerifier.verify(rawJson, expected)
        assertEquals(expected, computed)
    }
}
