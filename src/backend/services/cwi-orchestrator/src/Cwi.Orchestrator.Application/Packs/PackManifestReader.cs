using System.Text.Json;
using Cwi.Orchestrator.Application.Policy;

namespace Cwi.Orchestrator.Application.Packs;

public static class PackManifestReader
{
    public static PackManifestMetadata Read(PackDocument document)
    {
        using var json = JsonDocument.Parse(document.Json);
        return Read(json.RootElement);
    }

    public static PackManifestMetadata Read(JsonElement root)
    {
        var declaredName = ResolveDeclaredName(root);
        var packFamily = TryGetString(root, "pack_family");
        var dependencies = ReadDependencies(root);
        var capabilities = ReadCapabilities(root);
        var entries = ReadEntries(root);
        var modules = ReadModules(root);
        var moduleRoles = ReadModuleRoles(root);
        var scopeAppliesTo = ReadScopeAppliesTo(root);
        var forceMatchQueries = ReadForceMatchQueries(root);
        var activationLoadOrder = ReadActivationLoadOrder(root);
        var alwaysLoad = ReadAlwaysLoad(root);
        var runtimeRequired = ReadRuntimeRequired(root);
        var confidenceFloor = ReadConfidenceFloor(root);
        var (applyStage, hookName) = ReadRuntimeHook(root);

        return new PackManifestMetadata(
            declaredName,
            packFamily,
            dependencies,
            capabilities,
            entries,
            modules,
            moduleRoles,
            scopeAppliesTo,
            forceMatchQueries,
            activationLoadOrder,
            alwaysLoad,
            runtimeRequired,
            confidenceFloor,
            applyStage,
            hookName);
    }

    private static string? ResolveDeclaredName(JsonElement root)
    {
        var declaredName = TryGetString(root, "pack_id")
            ?? TryGetString(root, "name")
            ?? TryGetString(root, "pack_target")
            ?? TryGetString(root, "target");

        if (!string.IsNullOrWhiteSpace(declaredName))
        {
            return declaredName;
        }

        if (root.TryGetProperty("publication", out var publication) && publication.ValueKind == JsonValueKind.Object)
        {
            declaredName = TryGetString(publication, "slug")
                ?? TryGetString(publication, "name")
                ?? TryGetString(publication, "pack")
                ?? TryGetString(publication, "pack_id");
        }

        return declaredName;
    }

    private static IReadOnlyList<string> ReadDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var dependencies))
        {
            return Array.Empty<string>();
        }

        if (dependencies.ValueKind == JsonValueKind.Array)
        {
            return ReadDependencyArray(dependencies);
        }

        if (dependencies.ValueKind == JsonValueKind.Object)
        {
            if (dependencies.TryGetProperty("packs", out var packs) && packs.ValueKind == JsonValueKind.Array)
            {
                return ReadDependencyArray(packs);
            }

            if (dependencies.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return ReadDependencyArray(items);
            }
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ReadDependencyArray(JsonElement array)
    {
        var dependencies = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    dependencies.Add(value.Trim());
                }

                continue;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var name = TryGetString(item, "pack_id") ?? TryGetString(item, "name") ?? TryGetString(item, "pack");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    dependencies.Add(name.Trim());
                }
            }
        }

        return dependencies;
    }

    private static IReadOnlyList<PackCapability> ReadCapabilities(JsonElement root)
    {
        if (!root.TryGetProperty("capabilities", out var capabilities))
        {
            return Array.Empty<PackCapability>();
        }

        if (capabilities.ValueKind == JsonValueKind.Array)
        {
            return ReadCapabilityArray(capabilities);
        }

        if (capabilities.ValueKind == JsonValueKind.Object)
        {
            if (capabilities.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return ReadCapabilityArray(items);
            }
        }

        return Array.Empty<PackCapability>();
    }

    private static IReadOnlyList<PackCapability> ReadCapabilityArray(JsonElement array)
    {
        var capabilities = new List<PackCapability>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    capabilities.Add(new PackCapability(name.Trim(), scope: null, risk: null, requiresApproval: false, description: null));
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var nameValue = TryGetString(item, "name") ?? TryGetString(item, "id") ?? TryGetString(item, "capability");
            if (string.IsNullOrWhiteSpace(nameValue))
            {
                continue;
            }

            var scope = TryGetString(item, "scope") ?? TryGetString(item, "type") ?? TryGetString(item, "kind");
            var description = TryGetString(item, "description");
            var requiresApproval = TryGetBool(item, "requires_approval") || TryGetBool(item, "approval_required");
            var risk = TryGetRisk(item);

            capabilities.Add(new PackCapability(nameValue.Trim(), scope, risk, requiresApproval, description));
        }

        return capabilities;
    }

    private static IReadOnlyList<string> ReadEntries(JsonElement root)
    {
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return ReadNameArray(entries, new[] { "id", "name", "entry_id", "slug", "path" });
    }

    private static IReadOnlyList<string> ReadModules(JsonElement root)
    {
        if (!root.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return ReadNameArray(modules, new[] { "id", "name", "module_id", "slug", "path" });
    }

    private static IReadOnlyList<string> ReadModuleRoles(JsonElement root)
    {
        var roles = new List<string>();
        var topLevelRole = TryGetString(root, "module_role");
        if (!string.IsNullOrWhiteSpace(topLevelRole))
        {
            roles.Add(topLevelRole.Trim());
        }

        if (root.TryGetProperty("modules", out var modules) && modules.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in modules.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var role = TryGetString(item, "role")
                    ?? TryGetString(item, "module_role")
                    ?? TryGetString(item, "kind");
                if (!string.IsNullOrWhiteSpace(role))
                {
                    roles.Add(role.Trim());
                }
            }
        }

        return roles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadScopeAppliesTo(JsonElement root)
    {
        if (!root.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        if (!scope.TryGetProperty("applies_to", out var appliesTo))
        {
            return Array.Empty<string>();
        }

        return ReadStringValues(appliesTo);
    }

    private static IReadOnlyList<string> ReadForceMatchQueries(JsonElement root)
    {
        if (root.TryGetProperty("retrieval", out var retrieval) && retrieval.ValueKind == JsonValueKind.Object)
        {
            if (retrieval.TryGetProperty("force_match_queries", out var queries))
            {
                return ReadStringValues(queries);
            }
        }

        if (root.TryGetProperty("force_match_queries", out var topLevelQueries))
        {
            return ReadStringValues(topLevelQueries);
        }

        return Array.Empty<string>();
    }

    private static int? ReadActivationLoadOrder(JsonElement root)
    {
        if (root.TryGetProperty("activation", out var activation) && activation.ValueKind == JsonValueKind.Object)
        {
            return TryGetInt(activation, "load_order");
        }

        return TryGetInt(root, "load_order");
    }

    private static bool ReadAlwaysLoad(JsonElement root)
    {
        if (root.TryGetProperty("activation", out var activation) && activation.ValueKind == JsonValueKind.Object)
        {
            return TryGetBool(activation, "always_load") || TryGetBool(activation, "required");
        }

        return TryGetBool(root, "always_load");
    }

    private static bool ReadRuntimeRequired(JsonElement root)
    {
        if (root.TryGetProperty("activation", out var activation) && activation.ValueKind == JsonValueKind.Object)
        {
            if (TryGetBool(activation, "runtime_required"))
            {
                return true;
            }
        }

        return TryGetBool(root, "runtime_required");
    }

    private static float? ReadConfidenceFloor(JsonElement root)
    {
        if (root.TryGetProperty("confidence_policy", out var policy) && policy.ValueKind == JsonValueKind.Object)
        {
            var floor = TryGetFloat(policy, "floor")
                ?? TryGetFloat(policy, "min_confidence")
                ?? TryGetFloat(policy, "confidence_floor");
            if (floor.HasValue)
            {
                return floor.Value;
            }
        }

        return TryGetFloat(root, "confidence_floor");
    }

    private static (string? ApplyStage, string? HookName) ReadRuntimeHook(JsonElement root)
    {
        if (root.TryGetProperty("runtime_hooks", out var hooks))
        {
            var result = ReadRuntimeHookContainer(hooks);
            if (!string.IsNullOrWhiteSpace(result.ApplyStage) || !string.IsNullOrWhiteSpace(result.HookName))
            {
                return result;
            }
        }

        if (root.TryGetProperty("runtime_hook", out var hook))
        {
            var result = ReadRuntimeHookContainer(hook);
            if (!string.IsNullOrWhiteSpace(result.ApplyStage) || !string.IsNullOrWhiteSpace(result.HookName))
            {
                return result;
            }
        }

        return (TryGetString(root, "apply_stage"), TryGetString(root, "hook_name"));
    }

    private static (string? ApplyStage, string? HookName) ReadRuntimeHookContainer(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var result = ReadRuntimeHookContainer(item);
                    if (!string.IsNullOrWhiteSpace(result.ApplyStage) || !string.IsNullOrWhiteSpace(result.HookName))
                    {
                        return result;
                    }
                }
            }

            var applyStage = TryGetString(element, "apply_stage");
            var hookName = TryGetString(element, "hook_name") ?? TryGetString(element, "name");
            if (!string.IsNullOrWhiteSpace(applyStage) || !string.IsNullOrWhiteSpace(hookName))
            {
                return (applyStage, hookName);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var result = ReadRuntimeHookContainer(item);
                if (!string.IsNullOrWhiteSpace(result.ApplyStage) || !string.IsNullOrWhiteSpace(result.HookName))
                {
                    return result;
                }
            }
        }

        return (null, null);
    }

    private static IReadOnlyList<string> ReadNameArray(JsonElement array, IReadOnlyList<string> nameCandidates)
    {
        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var stringValue = item.GetString();
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    values.Add(stringValue.Trim());
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? value = null;
            foreach (var candidate in nameCandidates)
            {
                value = TryGetString(item, candidate);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return values;
    }

    private static IReadOnlyList<string> ReadStringValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : new[] { value.Trim() };
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RiskLevel? TryGetRisk(JsonElement element)
    {
        var value = TryGetString(element, "risk") ?? TryGetString(element, "risk_level");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "low" => RiskLevel.Low,
            "medium" => RiskLevel.Medium,
            "high" => RiskLevel.High,
            _ => null
        };
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static float? TryGetFloat(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetSingle(out var floatValue) => floatValue,
            JsonValueKind.String when float.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool TryGetBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }
}
