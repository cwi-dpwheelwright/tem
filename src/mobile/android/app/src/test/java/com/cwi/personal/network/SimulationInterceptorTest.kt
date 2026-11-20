package com.cwi.personal.network

import java.io.IOException
import okhttp3.Interceptor
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class SimulationInterceptorTest {
    @Test
    fun offlineModeThrowsIOException() {
        val interceptor = SimulationInterceptor(simulateOffline = true, backpressureMillis = 0)
        val chain = RecordingChain(Request.Builder().url("http://localhost/").build())

        assertThrows(IOException::class.java) {
            interceptor.intercept(chain)
        }
        assertEquals(0, chain.proceedCount)
    }

    @Test
    fun backpressureDelaysAndProceeds() {
        val delayMillis = 50L
        val interceptor = SimulationInterceptor(simulateOffline = false, backpressureMillis = delayMillis)
        val chain = RecordingChain(Request.Builder().url("http://localhost/").build())

        val start = System.currentTimeMillis()
        val response = interceptor.intercept(chain)
        val elapsed = System.currentTimeMillis() - start

        assertEquals(1, chain.proceedCount)
        assertEquals(200, response.code)
        assertEquals(true, elapsed >= delayMillis)
    }

    private class RecordingChain(private val request: Request) : Interceptor.Chain {
        var proceedCount = 0

        override fun request(): Request = request

        override fun proceed(request: Request): Response {
            proceedCount += 1
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
