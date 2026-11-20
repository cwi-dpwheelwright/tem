package com.cwi.personal.network

import android.content.Context
import com.cwi.personal.BuildConfig
import okhttp3.OkHttpClient
import okhttp3.CertificatePinner
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory
import java.util.concurrent.TimeUnit

object ApiClient {
    private var tokenStore: TokenStore? = null

    private fun resolveBaseUrl(): String {
        val raw = BuildConfig.API_BASE_URL.trim()
        if (raw.isEmpty()) {
            throw IllegalStateException("API_BASE_URL is required. Set CWI_API_BASE_URL at build time.")
        }
        return if (raw.endsWith("/")) raw else "$raw/"
    }

    fun baseUrl(): String = resolveBaseUrl()

    private val loggingInterceptor = HttpLoggingInterceptor().apply {
        level = if (BuildConfig.LOG_HTTP) {
            HttpLoggingInterceptor.Level.BASIC
        } else {
            HttpLoggingInterceptor.Level.NONE
        }
        redactHeader("Authorization")
    }

    private val authInterceptor = AuthInterceptor { tokenStore?.token }

    private val okHttpClient = buildClient()

    private fun buildClient(): OkHttpClient {
        val builder = OkHttpClient.Builder()
            .connectTimeout(10, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .writeTimeout(30, TimeUnit.SECONDS)
            .addInterceptor(RetryInterceptor())
            .addInterceptor(SimulationInterceptor())
            .addInterceptor(authInterceptor)
            .addInterceptor(loggingInterceptor)

        if (BuildConfig.CERT_PINNING_ENABLED) {
            val host = BuildConfig.CERT_PIN_HOST.trim()
            val pinList = BuildConfig.CERT_PIN_SHA256
                .split(",")
                .map { it.trim() }
                .filter { it.isNotEmpty() }
            if (host.isNotEmpty() && pinList.isNotEmpty()) {
                val pinner = CertificatePinner.Builder().apply {
                    pinList.forEach { pin -> add(host, pin) }
                }.build()
                builder.certificatePinner(pinner)
            }
        }

        return builder.build()
    }

    private val retrofit by lazy {
        Retrofit.Builder()
            .baseUrl(resolveBaseUrl())
            .client(okHttpClient)
            .addConverterFactory(MoshiConverterFactory.create())
            .build()
    }

    fun <T> create(service: Class<T>): T = retrofit.create(service)

    fun api(): ApiService = retrofit.create(ApiService::class.java)

    fun initialize(context: Context) {
        if (tokenStore == null) {
            tokenStore = TokenStore(context.applicationContext)
        }
    }

    fun updateToken(token: String?) {
        tokenStore?.token = token
    }
}
