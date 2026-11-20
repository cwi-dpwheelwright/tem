package com.cwi.personal.pack

import org.json.JSONArray
import org.json.JSONObject

object PackParser {
    fun parseModules(rawJson: String): List<PackModule> {
        return try {
            val root = JSONObject(rawJson)
            val ui = root.optJSONObject("ui") ?: return emptyList()
            val modules = ui.optJSONArray("modules") ?: return emptyList()
            buildModules(modules)
        } catch (_: Exception) {
            emptyList()
        }
    }

    private fun buildModules(modules: JSONArray): List<PackModule> {
        val results = mutableListOf<PackModule>()
        for (i in 0 until modules.length()) {
            val module = modules.optJSONObject(i) ?: continue
            val type = module.optString("type", "module")
            if (type == "admin_page") continue
            val id = module.optString("id", "module-$i")
            val title = module.optString("title").takeIf { it.isNotBlank() }
            val surface = module.optString("surface").takeIf { it.isNotBlank() }
            val actions = parseActions(module.optJSONArray("actions"))
            results.add(PackModule(id = id, type = type, title = title, surface = surface, actions = actions))
        }
        return results
    }

    private fun parseActions(actions: JSONArray?): List<PackAction> {
        if (actions == null) return emptyList()
        val results = mutableListOf<PackAction>()
        for (i in 0 until actions.length()) {
            val action = actions.optJSONObject(i) ?: continue
            val id = action.optString("id", "action-$i")
            val label = action.optString("label", "Action")
            val actionType = action.optString("action_type", action.optString("type", "action"))
            val argsObj = action.optJSONObject("args")
            val args = mutableMapOf<String, String>()
            if (argsObj != null) {
                val keys = argsObj.keys()
                while (keys.hasNext()) {
                    val key = keys.next()
                    args[key] = argsObj.optString(key)
                }
            }
            results.add(PackAction(id = id, label = label, actionType = actionType, args = args))
        }
        return results
    }
}
