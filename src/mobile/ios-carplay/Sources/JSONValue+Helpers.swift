import Foundation

extension JSONValue {
    var stringValue: String? {
        if case .string(let value) = self {
            return value
        }
        return nil
    }

    var objectValue: [String: JSONValue]? {
        if case .object(let value) = self {
            return value
        }
        return nil
    }

    var isEmpty: Bool {
        switch self {
        case .object(let value):
            return value.isEmpty
        case .array(let value):
            return value.isEmpty
        case .string(let value):
            return value.isEmpty
        default:
            return false
        }
    }

    func toAny() -> Any {
        switch self {
        case .string(let value):
            return value
        case .number(let value):
            return value
        case .bool(let value):
            return value
        case .object(let value):
            return value.mapValues { $0.toAny() }
        case .array(let value):
            return value.map { $0.toAny() }
        case .null:
            return NSNull()
        }
    }

    func jsonString(pretty: Bool = false) -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = pretty ? [.prettyPrinted, .sortedKeys] : [.sortedKeys]
        guard let data = try? encoder.encode(self),
              let string = String(data: data, encoding: .utf8) else {
            return "{}"
        }
        return string
    }
}
