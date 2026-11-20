package com.cwi.personal.pack

import java.security.MessageDigest
import org.json.JSONArray
import org.json.JSONObject

object AnchorVerifier {
    fun verify(rawJson: String, anchor: String): String {
        val canonical = canonicalize(rawJson)
        val computed = sha256Hex(canonical)
        val expected = anchor.trim().lowercase()
        if (computed != expected) {
            throw IllegalStateException("Pack anchor mismatch.")
        }
        return computed
    }

    fun canonicalize(rawJson: String): String {
        val parsed = JSONObject(rawJson)
        return canonicalizeValue(parsed)
    }

    private fun canonicalizeValue(value: Any?): String {
        return when (value) {
            null -> "null"
            JSONObject.NULL -> "null"
            is JSONObject -> canonicalizeObject(value)
            is JSONArray -> canonicalizeArray(value)
            is String -> JSONObject.quote(value)
            is Boolean -> if (value) "true" else "false"
            is Number -> value.toString()
            else -> JSONObject.quote(value.toString())
        }
    }

    private fun canonicalizeObject(obj: JSONObject): String {
        val keys = obj.keys().asSequence().toList().sorted()
        val builder = StringBuilder()
        builder.append("{")
        keys.forEachIndexed { index, key ->
            builder.append(JSONObject.quote(key))
            builder.append(":")
            builder.append(canonicalizeValue(obj.get(key)))
            if (index < keys.size - 1) builder.append(",")
        }
        builder.append("}")
        return builder.toString()
    }

    private fun canonicalizeArray(array: JSONArray): String {
        val builder = StringBuilder()
        builder.append("[")
        for (i in 0 until array.length()) {
            builder.append(canonicalizeValue(array.get(i)))
            if (i < array.length() - 1) builder.append(",")
        }
        builder.append("]")
        return builder.toString()
    }

    private fun sha256Hex(input: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(input.toByteArray())
        return digest.joinToString("") { "%02x".format(it) }
    }
}
