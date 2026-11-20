import Foundation

struct RuntimeConfig: Codable {
    let baseUrl: String?
    let tenantId: String?
    let userId: String?
    let packName: String?
    let policyMode: String?
    let overrideExisting: Bool?
}

enum RuntimeConfigLoader {
    private static let fileName = "runtime-config.json"

    static func applyIfPresent(bundle: Bundle = .main) {
        guard let config = load(bundle: bundle) else {
            return
        }
        let overrideExisting = config.overrideExisting ?? false
        if shouldApply(config.baseUrl, existing: AppDefaults.baseUrl, overrideExisting: overrideExisting) {
            AppDefaults.baseUrl = normalizeUrl(config.baseUrl ?? "")
        }
        if shouldApply(config.tenantId, existing: AppDefaults.tenantId, overrideExisting: overrideExisting) {
            AppDefaults.tenantId = config.tenantId?.trimmed() ?? ""
        }
        if shouldApply(config.userId, existing: AppDefaults.userId, overrideExisting: overrideExisting) {
            AppDefaults.userId = config.userId?.trimmed() ?? ""
        }
        if shouldApply(config.packName, existing: AppDefaults.packName, overrideExisting: overrideExisting) {
            AppDefaults.packName = config.packName?.trimmed() ?? ""
        }
        if shouldApply(config.policyMode, existing: AppDefaults.policyMode, overrideExisting: overrideExisting) {
            AppDefaults.policyMode = config.policyMode?.trimmed() ?? ""
        }
    }

    static func load(bundle: Bundle = .main) -> RuntimeConfig? {
        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .convertFromSnakeCase
        for url in candidateUrls(bundle: bundle) {
            guard let data = try? Data(contentsOf: url) else {
                continue
            }
            if let config = try? decoder.decode(RuntimeConfig.self, from: data) {
                return config
            }
        }
        return nil
    }

    private static func candidateUrls(bundle: Bundle) -> [URL] {
        var urls: [URL] = []
        if let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first {
            urls.append(documents.appendingPathComponent(fileName))
        }
        if let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first {
            urls.append(support.appendingPathComponent(fileName))
        }
        if let bundled = bundle.url(forResource: "runtime-config", withExtension: "json") {
            urls.append(bundled)
        }
        return urls
    }

    private static func shouldApply(_ newValue: String?, existing: String, overrideExisting: Bool) -> Bool {
        guard let newValue = newValue?.trimmed(), !newValue.isEmpty else {
            return false
        }
        if overrideExisting {
            return true
        }
        return existing.trimmed().isEmpty
    }

    private static func normalizeUrl(_ value: String) -> String {
        let trimmed = value.trimmed()
        return trimmed.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
    }
}

private extension String {
    func trimmed() -> String {
        trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
