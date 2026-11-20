import Foundation
import CryptoKit

struct AnchorVerifier {
    static func verify(jsonData: Data, expectedAnchor: String) throws -> String {
        let canonical = try canonicalize(jsonData: jsonData)
        let hash = sha256Hex(canonical)
        guard hash.lowercased() == expectedAnchor.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() else {
            throw NSError(domain: "AnchorVerifier", code: 1, userInfo: [
                NSLocalizedDescriptionKey: "Pack anchor mismatch."
            ])
        }
        return hash
    }

    static func verify(rawJson: String, expectedAnchor: String) throws -> String {
        guard let data = rawJson.data(using: .utf8) else {
            throw NSError(domain: "AnchorVerifier", code: 3, userInfo: [
                NSLocalizedDescriptionKey: "Invalid JSON string."
            ])
        }
        return try verify(jsonData: data, expectedAnchor: expectedAnchor)
    }

    static func canonicalize(jsonData: Data) throws -> String {
        let json = try JSONSerialization.jsonObject(with: jsonData, options: [])
        return try canonicalizeValue(json)
    }

    private static func canonicalizeValue(_ value: Any) throws -> String {
        if let dict = value as? [String: Any] {
            let keys = dict.keys.sorted()
            let parts = try keys.map { key -> String in
                guard let item = dict[key] else {
                    return ""
                }
                return "\(try encodeString(key)):\(try canonicalizeValue(item))"
            }
            return "{\(parts.joined(separator: ","))}"
        }
        if let array = value as? [Any] {
            let parts = try array.map { try canonicalizeValue($0) }
            return "[\(parts.joined(separator: ","))]"
        }
        if let str = value as? String {
            return try encodeString(str)
        }
        if let number = value as? NSNumber {
            if CFGetTypeID(number) == CFBooleanGetTypeID() {
                return number.boolValue ? "true" : "false"
            }
            return try encodeNumber(number)
        }
        if value is NSNull {
            return "null"
        }
        throw NSError(domain: "AnchorVerifier", code: 2, userInfo: [
            NSLocalizedDescriptionKey: "Canonicalization failed."
        ])
    }

    private static func encodeString(_ value: String) throws -> String {
        let data = try JSONSerialization.data(
            withJSONObject: [value],
            options: [.withoutEscapingSlashes]
        )
        guard let json = String(data: data, encoding: .utf8),
              json.first == "[",
              json.last == "]" else {
            throw NSError(domain: "AnchorVerifier", code: 4, userInfo: [
                NSLocalizedDescriptionKey: "String encoding failed."
            ])
        }
        return String(json.dropFirst().dropLast())
    }

    private static func encodeNumber(_ value: NSNumber) throws -> String {
        if !CFNumberIsFloatType(value) {
            return value.stringValue
        }
        let doubleValue = value.doubleValue
        guard doubleValue.isFinite else {
            throw NSError(domain: "AnchorVerifier", code: 5, userInfo: [
                NSLocalizedDescriptionKey: "Non-finite number."
            ])
        }
        let formatter = NumberFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.numberStyle = .decimal
        formatter.usesSignificantDigits = true
        formatter.minimumSignificantDigits = 1
        formatter.maximumSignificantDigits = 17
        formatter.decimalSeparator = "."
        formatter.groupingSeparator = ""
        guard let formatted = formatter.string(from: NSNumber(value: doubleValue)) else {
            throw NSError(domain: "AnchorVerifier", code: 5, userInfo: [
                NSLocalizedDescriptionKey: "Number encoding failed."
            ])
        }
        return formatted
    }

    private static func sha256Hex(_ input: String) -> String {
        let data = Data(input.utf8)
        let digest = SHA256.hash(data: data)
        return digest.compactMap { String(format: "%02x", $0) }.joined()
    }
}
