import SwiftUI

enum PackParser {
    static func parseModules(from rawJson: String) -> [PackUiModule] {
        guard let data = rawJson.data(using: .utf8),
              let modules = decodeModules(from: data) else {
            return []
        }
        return modules.filter { $0.type != "admin_page" }
    }

    private static func decodeModules(from data: Data) -> [PackUiModule]? {
        if let root = try? JSONDecoder().decode(PackRoot.self, from: data),
           let modules = root.ui?.modules {
            return modules
        }
        if let legacy = try? JSONDecoder().decode(PackRootLegacy.self, from: data) {
            return legacy.modules
        }
        return nil
    }
}

private struct PackRootLegacy: Decodable {
    let modules: [PackUiModule]
}

struct ModuleRenderer: View {
    let modules: [PackUiModule]
    let actionDisabled: Bool
    let actionDisabledReason: String?
    let baseUrl: String
    let jwtToken: String
    let fixtureStore: FixtureStore
    let onAction: (PackAction) -> Void

    var body: some View {
        let tabs = modules.filter { $0.type == "tab" }
        let nonTabs = modules.filter { $0.type != "tab" }

        VStack(alignment: .leading, spacing: 12) {
            if !tabs.isEmpty {
                TabView {
                    ForEach(tabs) { tab in
                        ModuleNodeView(
                            module: tab,
                            actionDisabled: actionDisabled,
                            actionDisabledReason: actionDisabledReason,
                            baseUrl: baseUrl,
                            jwtToken: jwtToken,
                            fixtureStore: fixtureStore,
                            onAction: onAction
                        )
                        .tabItem {
                            Text(tab.title ?? tab.type)
                        }
                    }
                }
                .frame(minHeight: 220)
            }

            ForEach(nonTabs) { module in
                ModuleNodeView(
                    module: module,
                    actionDisabled: actionDisabled,
                    actionDisabledReason: actionDisabledReason,
                    baseUrl: baseUrl,
                    jwtToken: jwtToken,
                    fixtureStore: fixtureStore,
                    onAction: onAction
                )
            }
        }
    }
}

private extension JSONValue {
    var rendered: String {
        switch self {
        case .string(let value):
            return value
        case .number(let value):
            return String(value)
        case .bool(let value):
            return value ? "true" : "false"
        case .object(let value):
            return value.map { "\($0):\($1.rendered)" }.sorted().joined(separator: ", ")
        case .array(let value):
            return value.map { $0.rendered }.joined(separator: ", ")
        case .null:
            return "null"
        }
    }
}

private struct ModuleNodeView: View {
    let module: PackUiModule
    let actionDisabled: Bool
    let actionDisabledReason: String?
    let baseUrl: String
    let jwtToken: String
    let fixtureStore: FixtureStore
    let onAction: (PackAction) -> Void

    var body: some View {
        switch module.type {
        case "screen":
            screenView
        case "card":
            cardView
        case "tile":
            tileView
        case "tab":
            screenView
        default:
            cardView
        }
    }

    private var screenView: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            content
        }
        .padding()
        .background(Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

    private var cardView: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            content
        }
        .padding()
        .background(Color(.tertiarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
    }

    private var tileView: some View {
        VStack(alignment: .leading, spacing: 8) {
            header
            content
        }
        .padding(10)
        .background(Color(.systemGray6))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }

    @ViewBuilder
    private var header: some View {
        Text(module.title ?? module.type)
            .font(.headline)
        Text("Type: \(module.type)")
            .font(.caption)
            .foregroundStyle(.secondary)
    }

    @ViewBuilder
    private var content: some View {
        if let reference = module.dataReference {
            ModuleDataReferenceView(
                reference: reference,
                baseUrl: baseUrl,
                jwtToken: jwtToken,
                fixtureStore: fixtureStore
            )
        } else if let data = module.data, !data.isEmpty {
            VStack(alignment: .leading, spacing: 2) {
                Text("Data")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                ForEach(data.keys.sorted(), id: \.self) { key in
                    if let value = data[key] {
                        Text("\(key): \(value.rendered)")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        if let actions = module.actions {
            ForEach(actions) { action in
                Button(action.label) {
                    onAction(action)
                }
                .buttonStyle(.bordered)
                .disabled(actionDisabled)
            }
            if actionDisabled, let reason = actionDisabledReason {
                Text(reason)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }
        }
        if let children = module.children, !children.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                ForEach(children) { child in
                    ModuleNodeView(
                        module: child,
                        actionDisabled: actionDisabled,
                        actionDisabledReason: actionDisabledReason,
                        baseUrl: baseUrl,
                        jwtToken: jwtToken,
                        fixtureStore: fixtureStore,
                        onAction: onAction
                    )
                }
            }
        }
    }
}

private struct ModuleDataReferenceView: View {
    let reference: DataReference
    let baseUrl: String
    let jwtToken: String
    let fixtureStore: FixtureStore
    @StateObject private var model = ModuleDataViewModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Data Reference (\(reference.source))")
                .font(.caption)
                .foregroundStyle(.secondary)
            switch model.state {
            case .idle:
                Text("Waiting for data")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            case .loading:
                ProgressView("Loading data...")
                    .font(.caption2)
            case .loaded(let value):
                Text(value.jsonString(pretty: true))
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            case .error(let message):
                Text(message)
                    .font(.caption2)
                    .foregroundStyle(.red)
            }
        }
        .task {
            await model.load(reference: reference, baseUrl: baseUrl, jwtToken: jwtToken, fixtureStore: fixtureStore)
        }
    }
}
