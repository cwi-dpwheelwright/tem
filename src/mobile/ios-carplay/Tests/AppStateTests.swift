import XCTest
import CryptoKit
@testable import CwiIosCarplay

final class AppStateTests: XCTestCase {
    func testLoadCachedPackSetsTrustState() async throws {
        let store = PackStore()
        let rawJson = "{\"ui\":{\"modules\":[{\"id\":\"1\",\"type\":\"tile\"}]}}"
        let anchor = try computeAnchor(rawJson)
        let pack = PackDocument(
            name: "test",
            rawJson: rawJson,
            anchor: anchor,
            hash: anchor
        )
        store.savePack(pack)
        defer { removeCachedPackFiles() }

        let state = await MainActor.run { AppState() }
        await MainActor.run {
            state.loadCachedPack()
        }

        await MainActor.run {
            XCTAssertEqual(state.trustState, .verified)
            XCTAssertEqual(state.packModules.count, 1)
            XCTAssertTrue(state.offlineMode)
        }
    }

    func testAskHistoryStorePersistsItems() throws {
        let store = AskHistoryStore()
        let trace = AskResponse.Trace(
            traceId: "t1",
            sources: [],
            confidence: nil,
            policyMode: "observe",
            model: "pro",
            packHash: "hash",
            otelTraceId: nil
        )
        let item = AskHistoryItem(
            id: UUID(),
            question: "Q",
            answer: "A",
            citations: [],
            trace: trace,
            timestamp: Date()
        )
        store.save(items: [item])
        let loaded = store.load()
        XCTAssertEqual(loaded.count, 1)
        XCTAssertEqual(loaded.first?.question, "Q")
        store.clear()
    }

    func testKillSwitchStoreCachesStatus() {
        let store = KillSwitchStore()
        let now = Date()
        store.save(enabled: true, cachedAt: now)
        let cached = store.loadCached()
        XCTAssertEqual(cached?.enabled, true)
        guard let cachedAt = cached?.cachedAt else {
            XCTFail("Expected cached kill switch timestamp.")
            store.clear()
            return
        }
        XCTAssertEqual(cachedAt.timeIntervalSince1970, now.timeIntervalSince1970, accuracy: 1)
        store.clear()
    }

    func testActionRestrictionReasonUsesKillSwitch() async {
        let state = await MainActor.run { AppState() }
        await MainActor.run {
            state.killSwitchEnabled = true
            XCTAssertEqual(state.actionRestrictionReason, "Action disabled: kill switch active.")
        }
    }

    func testKeychainStoreRoundTrip() throws {
        let keychain = KeychainStore()
        try keychain.saveToken("token-1")
        let loaded = keychain.loadToken()
        XCTAssertEqual(loaded, "token-1")
        keychain.deleteToken()
    }

    func testKeychainStoreMigratesLegacyToken() throws {
        let keychain = KeychainStore()
        try keychain.saveToken("legacy-token", account: "auth_token")
        let loaded = keychain.loadToken()
        XCTAssertEqual(loaded, "legacy-token")
        keychain.deleteToken()
    }

    private func computeAnchor(_ rawJson: String) throws -> String {
        let data = try XCTUnwrap(rawJson.data(using: .utf8))
        let canonical = try AnchorVerifier.canonicalize(jsonData: data)
        let digest = SHA256.hash(data: Data(canonical.utf8))
        return digest.map { String(format: "%02x", $0) }.joined()
    }

    private func removeCachedPackFiles() {
        let doc = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
        let packUrl = doc?.appendingPathComponent("pack.json")
        let metaUrl = doc?.appendingPathComponent("pack.meta.json")
        if let packUrl { try? FileManager.default.removeItem(at: packUrl) }
        if let metaUrl { try? FileManager.default.removeItem(at: metaUrl) }
    }
}
