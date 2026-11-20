import Foundation

struct FixtureStore {
    private let bundle: Bundle

    init(bundle: Bundle = .main) {
        self.bundle = bundle
    }

    func loadJson(named name: String) -> JSONValue? {
        let url = bundle.url(forResource: name, withExtension: "json", subdirectory: "fixtures")
            ?? bundle.url(forResource: name, withExtension: "json")
        guard let url, let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(JSONValue.self, from: data)
    }

    func loadString(named name: String) -> String? {
        let url = bundle.url(forResource: name, withExtension: "txt", subdirectory: "fixtures")
            ?? bundle.url(forResource: name, withExtension: "txt")
        guard let url, let data = try? Data(contentsOf: url) else { return nil }
        return String(data: data, encoding: .utf8)
    }
}
