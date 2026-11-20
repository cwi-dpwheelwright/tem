package com.cwi.personal.network

import okhttp3.Interceptor
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class AuthInterceptorTest {
    @Test
    fun addsAuthorizationHeaderWhenTokenPresent() {
        val interceptor = AuthInterceptor { "test-token" }
        val chain = FakeChain(Request.Builder().url("http://localhost/").build())
        interceptor.intercept(chain)
        assertEquals("Bearer test-token", chain.proceededRequest?.header("Authorization"))
    }

    @Test
    fun doesNotAddHeaderWhenTokenMissing() {
        val interceptor = AuthInterceptor { null }
        val chain = FakeChain(Request.Builder().url("http://localhost/").build())
        interceptor.intercept(chain)
        assertNull(chain.proceededRequest?.header("Authorization"))
    }

    private class FakeChain(private val request: Request) : Interceptor.Chain {
        var proceededRequest: Request? = null

        override fun request(): Request = request

        override fun proceed(request: Request): Response {
            proceededRequest = request
            return Response.Builder()
                .request(request)
                .protocol(Protocol.HTTP_1_1)
                .code(200)
                .message("OK")
                .body("".toResponseBody())
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
