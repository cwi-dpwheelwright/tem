import XCTest
@testable import CwiIosCarplay

final class AnchorVerifierTests: XCTestCase {
    func testCanonicalizationVectorsMatchExpected() throws {
        let vectors = try loadVectors()
        for vector in vectors {
            let canonical = try AnchorVerifier.canonicalize(jsonData: Data(vector.json.utf8))
            XCTAssertEqual(vector.canonical, canonical, "Canonical mismatch for \(vector.name)")
        }
    }

    func testVerifyMatchesExpectedAnchor() throws {
        let vectors = try loadVectors()
        for vector in vectors {
            let computed = try AnchorVerifier.verify(rawJson: vector.json, expectedAnchor: vector.anchor)
            XCTAssertEqual(vector.anchor, computed, "Anchor mismatch for \(vector.name)")
        }
    }
}

private struct AnchorVector: Decodable {
    let name: String
    let json: String
    let canonical: String
    let anchor: String
}

private func loadVectors() throws -> [AnchorVector] {
    let bundle = Bundle(for: AnchorVerifierTests.self)
    guard let url = bundle.url(forResource: "anchor_vectors", withExtension: "json") else {
        throw NSError(domain: "AnchorVerifierTests", code: 1, userInfo: [
            NSLocalizedDescriptionKey: "Missing anchor_vectors.json in test bundle."
        ])
    }
    let data = try Data(contentsOf: url)
    return try JSONDecoder().decode([AnchorVector].self, from: data)
}
