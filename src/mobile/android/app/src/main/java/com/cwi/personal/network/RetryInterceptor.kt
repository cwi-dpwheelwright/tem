package com.cwi.personal.network

import java.io.IOException
import okhttp3.Interceptor
import okhttp3.Response

class RetryInterceptor(
    private val maxRetries: Int = 2
) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        if (!isIdempotent(request.method) || maxRetries <= 0) {
            return chain.proceed(request)
        }

        var attempt = 0
        var lastError: IOException? = null
        val maxAttempts = maxRetries + 1
        while (attempt < maxAttempts) {
            try {
                return chain.proceed(request)
            } catch (ex: IOException) {
                lastError = ex
                attempt += 1
            }
        }

        throw lastError ?: IOException("Retry failed without exception")
    }

    private fun isIdempotent(method: String): Boolean {
        return method.equals("GET", ignoreCase = true)
            || method.equals("HEAD", ignoreCase = true)
            || method.equals("OPTIONS", ignoreCase = true)
    }
}
