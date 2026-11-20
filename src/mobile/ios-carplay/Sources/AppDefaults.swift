import Foundation
import Security

enum AppDefaults {
    private static let defaults = UserDefaults.standard

    private static let baseUrlKey = "cwi.base_url"
    private static let tenantIdKey = "cwi.tenant_id"
    private static let userIdKey = "cwi.user_id"
    private static let policyModeKey = "cwi.policy_mode"
    private static let packNameKey = "cwi.pack_name"
    private static let consentKey = "cwi.consent_granted"
    private static let offlineModeKey = "cwi.offline_mode"
    private static let survivalModeKey = "cwi.survival_mode"
    private static let killSwitchEnabledKey = "cwi.killswitch.enabled"
    private static let killSwitchCachedAtKey = "cwi.killswitch.cached_at"

    static var baseUrl: String {
        get { defaults.string(forKey: baseUrlKey) ?? "" }
        set { defaults.set(newValue, forKey: baseUrlKey) }
    }

    static var tenantId: String {
        get { defaults.string(forKey: tenantIdKey) ?? "" }
        set { defaults.set(newValue, forKey: tenantIdKey) }
    }

    static var userId: String {
        get { defaults.string(forKey: userIdKey) ?? "" }
        set { defaults.set(newValue, forKey: userIdKey) }
    }

    static var policyMode: String {
        get { defaults.string(forKey: policyModeKey) ?? "approve" }
        set { defaults.set(newValue, forKey: policyModeKey) }
    }

    static var packName: String {
        get { defaults.string(forKey: packNameKey) ?? "" }
        set { defaults.set(newValue, forKey: packNameKey) }
    }

    static var consentGranted: Bool {
        get { defaults.bool(forKey: consentKey) }
        set { defaults.set(newValue, forKey: consentKey) }
    }

    static var offlineMode: Bool {
        get { defaults.bool(forKey: offlineModeKey) }
        set { defaults.set(newValue, forKey: offlineModeKey) }
    }

    static var survivalMode: String {
        get { defaults.string(forKey: survivalModeKey) ?? "normal" }
        set { defaults.set(newValue, forKey: survivalModeKey) }
    }

    static var killSwitchEnabled: Bool {
        get { defaults.bool(forKey: killSwitchEnabledKey) }
        set { defaults.set(newValue, forKey: killSwitchEnabledKey) }
    }

    static var killSwitchCachedAt: Date? {
        get { defaults.object(forKey: killSwitchCachedAtKey) as? Date }
        set { defaults.set(newValue, forKey: killSwitchCachedAtKey) }
    }
}

final class KillSwitchStore {
    func loadCached() -> KillSwitchStatus? {
        guard let cachedAt = AppDefaults.killSwitchCachedAt else {
            return nil
        }
        return KillSwitchStatus(enabled: AppDefaults.killSwitchEnabled, cachedAt: cachedAt)
    }

    func save(enabled: Bool, cachedAt: Date = Date()) {
        AppDefaults.killSwitchEnabled = enabled
        AppDefaults.killSwitchCachedAt = cachedAt
    }

    func clear() {
        AppDefaults.killSwitchEnabled = false
        AppDefaults.killSwitchCachedAt = nil
    }

    func isStale(maxAgeMinutes: Int) -> Bool {
        guard let cachedAt = AppDefaults.killSwitchCachedAt else {
            return true
        }
        let age = Date().timeIntervalSince(cachedAt) / 60
        return age >= Double(maxAgeMinutes)
    }
}

final class KeychainStore {
    private let service = "com.cwi.carplay"
    private let primaryAccount = "auth_token_v1"
    private let legacyAccount = "auth_token"
    private let refreshAccount = "refresh_token_v1"

    func saveToken(_ token: String, account: String? = nil) throws {
        let account = account ?? primaryAccount
        let data = Data(token.utf8)
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account,
            kSecValueData: data
        ]
        SecItemDelete(query as CFDictionary)
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw NSError(domain: "KeychainStore", code: Int(status), userInfo: nil)
        }
    }

    func loadToken() -> String? {
        if let token = loadToken(account: primaryAccount) {
            return token
        }
        if let legacy = loadToken(account: legacyAccount) {
            try? saveToken(legacy, account: primaryAccount)
            deleteToken(account: legacyAccount)
            return legacy
        }
        return nil
    }

    func saveRefreshToken(_ token: String) throws {
        try saveToken(token, account: refreshAccount)
    }

    func loadRefreshToken() -> String? {
        loadToken(account: refreshAccount)
    }

    func deleteRefreshToken() {
        deleteToken(account: refreshAccount)
    }

    func deleteToken(account: String? = nil) {
        let account = account ?? primaryAccount
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account
        ]
        SecItemDelete(query as CFDictionary)
    }

    private func loadToken(account: String) -> String? {
        let query: [CFString: Any] = [
            kSecClass: kSecClassGenericPassword,
            kSecAttrService: service,
            kSecAttrAccount: account,
            kSecReturnData: true,
            kSecMatchLimit: kSecMatchLimitOne
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }
}

@MainActor
final class SessionStore: ObservableObject {
    @Published private(set) var accessToken: String?
    @Published private(set) var refreshToken: String?
    private let keychain = KeychainStore()

    init() {
        accessToken = keychain.loadToken()
        refreshToken = keychain.loadRefreshToken()
    }

    func update(accessToken: String, refreshToken: String?) {
        self.accessToken = accessToken
        self.refreshToken = refreshToken
        try? keychain.saveToken(accessToken)
        if let refreshToken {
            try? keychain.saveRefreshToken(refreshToken)
        }
    }

    func logout() {
        accessToken = nil
        refreshToken = nil
        keychain.deleteToken()
        keychain.deleteRefreshToken()
    }
}
