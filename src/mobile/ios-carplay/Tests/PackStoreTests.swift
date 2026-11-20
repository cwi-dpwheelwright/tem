import XCTest
import CryptoKit
@testable import CwiIosCarplay

final class PackStoreTests: XCTestCase {
    func testSaveAndLoadVerifiedPack() throws {
        let store = PackStore()
        let rawJson = "{\"name\":\"enterprise\"}"
        let anchor = try computeAnchor(rawJson)
        let pack = PackDocument(name: "enterprise", rawJson: rawJson, anchor: anchor, hash: anchor)

        store.savePack(pack)
        defer { removeCachedPackFiles() }
        let loaded = store.loadVerifiedPack()

        XCTAssertNotNil(loaded.pack)
        XCTAssertEqual(pack.name, loaded.pack?.name)
        XCTAssertEqual(loaded.trustState, .verified)
    }

    func testLoadVerifiedPackFailsOnMismatch() throws {
        let store = PackStore()
        let rawJson = "{\"name\":\"enterprise\"}"
        let pack = PackDocument(name: "enterprise", rawJson: rawJson, anchor: "bad", hash: "bad")
        store.savePack(pack)
        defer { removeCachedPackFiles() }

        let loaded = store.loadVerifiedPack()
        XCTAssertNil(loaded.pack)
        XCTAssertEqual(loaded.trustState, .unverified)
        XCTAssertNotNil(loaded.error)
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
