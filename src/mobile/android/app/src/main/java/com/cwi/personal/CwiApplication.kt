package com.cwi.personal

import android.app.Application
import com.cwi.personal.telemetry.TelemetryClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.io.File
import java.lang.Thread.UncaughtExceptionHandler

class CwiApplication : Application() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        reportLastCrashIfPresent()
        installCrashHandler()
    }

    private fun reportLastCrashIfPresent() {
        val crashFile = File(filesDir, "last_crash.json")
        if (!crashFile.exists()) return
        val payload = runCatching { JSONObject(crashFile.readText()) }.getOrNull()
        crashFile.delete()
        if (payload == null) return
        val type = payload.optString("type", "unknown")
        val message = payload.optString("message", "")
        val timestamp = payload.optString("timestamp", "")
        scope.launch {
            TelemetryClient().sendEvent(
                tenantId = "tenant-1",
                event = "app_crash",
                metadata = mapOf(
                    "type" to type,
                    "message" to message.take(120),
                    "timestamp" to timestamp
                )
            )
        }
    }

    private fun installCrashHandler() {
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            val crashFile = File(filesDir, "last_crash.json")
            val payload = JSONObject()
                .put("type", throwable.javaClass.simpleName)
                .put("message", throwable.message ?: "")
                .put("timestamp", System.currentTimeMillis().toString())
                .put("thread", thread.name)
            crashFile.writeText(payload.toString())
            (previous ?: UncaughtExceptionHandler { _, _ -> }).uncaughtException(thread, throwable)
        }
    }
}
