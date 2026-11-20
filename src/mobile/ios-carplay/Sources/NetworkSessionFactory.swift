import Foundation
import CryptoKit
import Security

enum NetworkSessionFactory {
    static func makeSession(config: AppConfig = .shared) -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = config.apiTimeoutSeconds
        configuration.timeoutIntervalForResource = config.apiTimeoutSeconds * 2
        #if DEBUG
        let isDebug = true
        #else
        let isDebug = false
        #endif

        let shouldPin = config.certPinEnabled && !isDebug && !config.certPinHost.isEmpty && !config.certPinSHA256.isEmpty
        if shouldPin {
            let delegate = PinnedSessionDelegate(host: config.certPinHost, pins: config.certPinSHA256)
            return URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
        }
        return URLSession(configuration: configuration)
    }
}

final class PinnedSessionDelegate: NSObject, URLSessionDelegate {
    private let host: String
    private let pins: Set<String>

    init(host: String, pins: [String]) {
        self.host = host
        self.pins = Set(pins.map { PinnedSessionDelegate.normalizePin($0) })
        super.init()
    }

    func urlSession(
        _ session: URLSession,
        didReceive challenge: URLAuthenticationChallenge,
        completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
    ) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let serverTrust = challenge.protectionSpace.serverTrust else {
            completionHandler(.performDefaultHandling, nil)
            return
        }

        guard challenge.protectionSpace.host == host else {
            completionHandler(.performDefaultHandling, nil)
            return
        }

        if !SecTrustEvaluateWithError(serverTrust, nil) {
            completionHandler(.cancelAuthenticationChallenge, nil)
            return
        }

        let certificateCount = SecTrustGetCertificateCount(serverTrust)
        for index in 0..<certificateCount {
            guard let certificate = SecTrustGetCertificateAtIndex(serverTrust, index) else {
                continue
            }
            let data = SecCertificateCopyData(certificate) as Data
            let digest = SHA256.hash(data: data)
            let pin = "sha256/\(Data(digest).base64EncodedString())"
            if pins.contains(pin) {
                completionHandler(.useCredential, URLCredential(trust: serverTrust))
                return
            }
        }

        completionHandler(.cancelAuthenticationChallenge, nil)
    }

    private static func normalizePin(_ pin: String) -> String {
        let trimmed = pin.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.lowercased().hasPrefix("sha256/") {
            return trimmed
        }
        return "sha256/\(trimmed)"
    }
}
