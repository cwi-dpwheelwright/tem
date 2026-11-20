import Foundation

struct PackFetcher {
    let baseUrl: String
    let session: URLSession
    private let config: AppConfig

    init(baseUrl: String, session: URLSession? = nil, config: AppConfig = .shared) {
        self.baseUrl = baseUrl
        self.config = config
        self.session = session ?? NetworkSessionFactory.makeSession(config: config)
    }

    func fetch(packName: String) async throws -> PackDocument {
        if config.simulateOffline {
            throw URLError(.notConnectedToInternet)
        }
        if config.simulateBackpressureMs > 0 {
            try await Task.sleep(nanoseconds: config.simulateBackpressureMs * 1_000_000)
        }

        let trimmed = baseUrl.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        guard let packUrl = URL(string: "\(trimmed)/packs/\(packName)/pack.json"),
              let anchorUrl = URL(string: "\(trimmed)/packs/\(packName)/anchor.txt") else {
            throw URLError(.badURL)
        }

        let (packData, packResponse) = try await session.data(from: packUrl)
        guard let packHttp = packResponse as? HTTPURLResponse, packHttp.statusCode == 200 else {
            throw URLError(.badServerResponse)
        }

        let (anchorData, anchorResponse) = try await session.data(from: anchorUrl)
        guard let anchorHttp = anchorResponse as? HTTPURLResponse, anchorHttp.statusCode == 200 else {
            throw URLError(.badServerResponse)
        }

        let anchor = String(data: anchorData, encoding: .utf8) ?? ""
        let hash = try AnchorVerifier.verify(jsonData: packData, expectedAnchor: anchor)
        let rawJson = String(data: packData, encoding: .utf8) ?? "{}"
        return PackDocument(name: packName, rawJson: rawJson, anchor: anchor, hash: hash)
    }
}
