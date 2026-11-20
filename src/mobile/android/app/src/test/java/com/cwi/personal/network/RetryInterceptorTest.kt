package com.cwi.personal.network

import java.io.IOException
import okhttp3.Interceptor
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class RetryInterceptorTest {
    @Test
    fun retriesAreBoundedForIdempotentRequests() {
        val interceptor = RetryInterceptor(maxRetries = 2)
        val chain = FlakyChain(Request.Builder().url("http://localhost/").get().build(), failTimes = 2)

        val response = interceptor.intercept(chain)

        assertEquals(3, chain.attempts)
        assertEquals(200, response.code)
    }

    @Test
    fun doesNotRetryForNonIdempotentRequests() {
        val interceptor = RetryInterceptor(maxRetries = 2)
        val chain = FlakyChain(Request.Builder().url("http://localhost/").post("x".toRequestBody()).build(), failTimes = 1)

        assertThrows(IOException::class.java) {
            interceptor.intercept(chain)
        }
        assertEquals(1, chain.attempts)
    }

    private class FlakyChain(
        private val request: Request,
        private val failTimes: Int
    ) : Interceptor.Chain {
        var attempts = 0

        override fun request(): Request = request

        override fun proceed(request: Request): Response {
            attempts += 1
            if (attempts <= failTimes) {
                throw IOException("simulated failure")
            }
            return Response.Builder()
                .request(request)
                .protocol(Protocol.HTTP_1_1)
                .code(200)
                .message("OK")
                .body("ok".toResponseBody())
                .build()
        }

        override fun call() = throw UnsupportedOperationException()
        override fun connection(): okhttp3.Connection? = null
        override fun connectTimeoutMillis() = 1000
        override fun readTimeoutMillis() = 1000
        override fun writeTimeoutMillis() = 1000
        override fun withConnectTimeout(timeout: Int, unit: java.util.concurrent.TimeUnit) = this
        override fun withReadTimeout(timeout: Int, unit: java.util.concurrent.TimeUnit) = this
        override fun withWriteTimeout(timeout: Int, unit: java.util.concurrent.TimeUnit) = this
    }
}
