package com.cwi.personal.pack

data class PackModule(
    val id: String,
    val type: String,
    val title: String?,
    val surface: String?,
    val actions: List<PackAction> = emptyList()
)

data class PackAction(
    val id: String,
    val label: String,
    val actionType: String,
    val args: Map<String, String> = emptyMap()
)
