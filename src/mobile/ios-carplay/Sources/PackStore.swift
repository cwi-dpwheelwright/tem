import Foundation

struct PackCacheResult {
    let pack: PackDocument?
    let trustState: TrustState
    let error: String?
}

struct PackStore {
    private var packUrl: URL? {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first?
            .appendingPathComponent("pack.json")
    }

    private var metaUrl: URL? {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first?
            .appendingPathComponent("pack.meta.json")
    }

    func savePack(_ pack: PackDocument) {
        guard let packUrl, let metaUrl else {
            return
        }
        let meta: [String: String] = [
            "name": pack.name,
            "anchor": pack.anchor,
            "hash": pack.hash,
            "verified_at": ISO8601DateFormatter().string(from: Date())
        ]
        do {
            try pack.rawJson.data(using: .utf8)?.write(to: packUrl, options: .atomic)
            let metaData = try JSONSerialization.data(withJSONObject: meta, options: [.prettyPrinted])
            try metaData.write(to: metaUrl, options: .atomic)
        } catch {
            // ignore write failures for now
        }
    }

    func loadPack() -> PackDocument? {
        guard let packUrl, let metaUrl else {
            return nil
        }
        guard let rawData = try? Data(contentsOf: packUrl),
              let metaData = try? Data(contentsOf: metaUrl),
              let rawJson = String(data: rawData, encoding: .utf8),
              let meta = try? JSONSerialization.jsonObject(with: metaData, options: []) as? [String: String],
              let name = meta["name"],
              let anchor = meta["anchor"],
              let hash = meta["hash"] else {
            return nil
        }
        return PackDocument(name: name, rawJson: rawJson, anchor: anchor, hash: hash)
    }

    func loadVerifiedPack() -> PackCacheResult {
        guard let pack = loadPack() else {
            return PackCacheResult(pack: nil, trustState: .unknown, error: "No cached pack available.")
        }
        do {
            _ = try AnchorVerifier.verify(rawJson: pack.rawJson, expectedAnchor: pack.anchor)
            return PackCacheResult(pack: pack, trustState: .verified, error: nil)
        } catch {
            return PackCacheResult(pack: nil, trustState: .unverified, error: error.localizedDescription)
        }
    }
}
