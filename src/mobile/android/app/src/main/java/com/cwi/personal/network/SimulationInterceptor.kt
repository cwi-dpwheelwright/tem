package com.cwi.personal.network

import com.cwi.personal.BuildConfig
import okhttp3.Interceptor
import okhttp3.Response
import java.io.IOException

class SimulationInterceptor(
    private val simulateOffline: Boolean = BuildConfig.SIMULATE_OFFLINE,
    private val backpressureMillis: Long = BuildConfig.SIMULATE_BACKPRESSURE_MS
) : Interceptor {
    override fun intercept(chain: Interceptor.Chain): Response {
        if (simulateOffline) {
            throw IOException("Simulated offline mode")
        }
        if (backpressureMillis > 0) {
            Thread.sleep(backpressureMillis)
        }
        return chain.proceed(chain.request())
    }
}
