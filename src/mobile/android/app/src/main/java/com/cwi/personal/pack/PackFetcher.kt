package com.cwi.personal.pack

import com.cwi.personal.network.ApiClient
import okhttp3.OkHttpClient
import okhttp3.Request

data class PackDocument(
    val name: String,
    val rawJson: String,
    val anchor: String,
    val hash: String
)

class PackFetcher(
    private val client: OkHttpClient = OkHttpClient()
) {
    fun fetch(packName: String): PackDocument {
        val base = ApiClient.baseUrl().trimEnd('/')
        val packUrl = "$base/packs/$packName/pack.json"
        val anchorUrl = "$base/packs/$packName/anchor.txt"

        val packJson = fetchText(packUrl)
        val anchor = fetchText(anchorUrl)
        val hash = AnchorVerifier.verify(packJson, anchor)
        return PackDocument(packName, packJson, anchor, hash)
    }

    private fun fetchText(url: String): String {
        val request = Request.Builder().url(url).build()
        client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) {
                throw IllegalStateException("Failed to fetch $url")
            }
            return response.body?.string().orEmpty()
        }
    }
}
