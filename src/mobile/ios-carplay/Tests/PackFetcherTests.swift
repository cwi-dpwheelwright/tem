import XCTest
import CryptoKit
@testable import CwiIosCarplay

final class PackFetcherTests: XCTestCase {
    override func setUp() {
        super.setUp()
        URLProtocol.registerClass(URLProtocolStub.self)
    }

    override func tearDown() {
        URLProtocol.unregisterClass(URLProtocolStub.self)
        URLProtocolStub.handler = nil
        super.tearDown()
    }

    func testFetchReturnsVerifiedPack() async throws {
        let packName = "demo"
        let rawJson = "{\"ui\":{\"modules\":[{\"id\":\"1\",\"type\":\"tile\"}]}}"
        let jsonData = Data(rawJson.utf8)
        let canonical = try AnchorVerifier.canonicalize(jsonData: jsonData)
        let digest = SHA256.hash(data: Data(canonical.utf8))
        let anchor = digest.map { String(format: "%02x", $0) }.joined()

        URLProtocolStub.handler = { request in
            let url = try XCTUnwrap(request.url)
            let response = HTTPURLResponse(
                url: url,
                statusCode: 200,
                httpVersion: nil,
                headerFields: nil
            )
            if url.path.hasSuffix("/pack.json") {
                return URLProtocolStub.Stub(response: response!, data: jsonData)
            }
            if url.path.hasSuffix("/anchor.txt") {
                return URLProtocolStub.Stub(response: response!, data: Data(anchor.utf8))
            }
            return URLProtocolStub.Stub(response: response!, data: Data())
        }

        let fetcher = PackFetcher(baseUrl: "http://localhost:8080", session: makeTestSession())
        let pack = try await fetcher.fetch(packName: packName)
        XCTAssertEqual(pack.name, packName)
        XCTAssertEqual(pack.hash, anchor)
        XCTAssertEqual(pack.rawJson, rawJson)
    }
}

private func makeTestSession() -> URLSession {
    let configuration = URLSessionConfiguration.ephemeral
    configuration.protocolClasses = [URLProtocolStub.self]
    return URLSession(configuration: configuration)
}
