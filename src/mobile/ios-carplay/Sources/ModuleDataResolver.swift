import Foundation

struct DataReference: Equatable {
    let source: String
    let path: String?
    let name: String?
    let method: String?
}

enum ModuleDataState: Equatable {
    case idle
    case loading
    case loaded(JSONValue)
    case error(String)
}

@MainActor
final class ModuleDataViewModel: ObservableObject {
    @Published private(set) var state: ModuleDataState = .idle

    func load(
        reference: DataReference,
        baseUrl: String,
        jwtToken: String,
        fixtureStore: FixtureStore,
        session: URLSession? = nil
    ) async {
        switch reference.source.lowercased() {
        case "fixture":
            if let name = reference.name, let value = fixtureStore.loadJson(named: name) {
                state = .loaded(value)
            } else {
                state = .error("Fixture not found")
            }
        case "api":
            guard let path = reference.path else {
                state = .error("Missing API path")
                return
            }
            let method = reference.method?.uppercased() ?? "GET"
            guard method == "GET" else {
                state = .error("Unsupported method")
                return
            }
            state = .loading
            do {
                let client = APIClient(baseUrl: baseUrl, jwtToken: jwtToken, session: session)
                let normalized = path.hasPrefix("/") ? path : "/" + path
                let value = try await client.fetchJson(path: normalized)
                state = .loaded(value)
            } catch {
                state = .error(error.localizedDescription)
            }
        default:
            state = .error("Unsupported data source")
        }
    }
}

extension PackUiModule {
    var dataReference: DataReference? {
        guard let data else { return nil }
        guard let source = data["source"]?.stringValue else { return nil }
        return DataReference(
            source: source,
            path: data["path"]?.stringValue,
            name: data["name"]?.stringValue,
            method: data["method"]?.stringValue
        )
    }
}
