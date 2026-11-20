package com.cwi.personal.pack

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class PackParserTest {
    @Test
    fun parseModules_skipsAdminPageAndParsesActions() {
        val json = """
            {
              "ui": {
                "modules": [
                  {
                    "type": "admin_page",
                    "id": "admin-1"
                  },
                  {
                    "id": "module-1",
                    "type": "module",
                    "title": "Ops",
                    "surface": "android",
                    "actions": [
                      {
                        "id": "action-1",
                        "label": "Run",
                        "action_type": "orchestrator.action",
                        "args": {
                          "severity": "high",
                          "mode": "safe"
                        }
                      }
                    ]
                  }
                ]
              }
            }
        """.trimIndent()

        val modules = PackParser.parseModules(json)
        assertEquals(1, modules.size)
        val module = modules.first()
        assertEquals("module-1", module.id)
        assertEquals("Ops", module.title)
        assertEquals("android", module.surface)
        assertEquals(1, module.actions.size)
        val action = module.actions.first()
        assertEquals("action-1", action.id)
        assertEquals("orchestrator.action", action.actionType)
        assertEquals("high", action.args["severity"])
        assertEquals("safe", action.args["mode"])
    }

    @Test
    fun parseModules_returnsEmptyForInvalidJson() {
        val modules = PackParser.parseModules("{not-json}")
        assertTrue(modules.isEmpty())
    }
}
