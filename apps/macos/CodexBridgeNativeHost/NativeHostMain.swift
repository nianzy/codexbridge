import AppKit
import Foundation

private enum NativeHostError: LocalizedError {
    case invalidMessage(String)

    var errorDescription: String? {
        switch self {
        case let .invalidMessage(message): message
        }
    }
}

private struct NativeMessage: Codable {
    let type: String
    let payload: CapturePayload?
}

private struct NativeResponse: Codable {
    let ok: Bool
    let error: String?
    let conversationID: String?
}

@main
enum CodexBridgeNativeHost {
    static func main() async {
        do {
            guard let data = try readMessage() else { return }
            let decoder = JSONDecoder()
            decoder.dateDecodingStrategy = .iso8601
            let message = try decoder.decode(NativeMessage.self, from: data)
            guard message.type == "capture.import", let payload = message.payload else {
                try writeResponse(.init(ok: false, error: "不支持的 Codex Bridge Native Messaging 请求。", conversationID: nil))
                return
            }

            let conversation = try CaptureValidator().validate(payload)
            try persistToInbox(payload)
            DistributedNotificationCenter.default().postNotificationName(
                Notification.Name("app.codexbridge.captureImported"),
                object: nil,
                userInfo: nil,
                deliverImmediately: true
            )
            await openCodexBridge()
            try writeResponse(.init(ok: true, error: nil, conversationID: conversation.id.uuidString))
        } catch {
            try? writeResponse(.init(ok: false, error: error.localizedDescription, conversationID: nil))
        }
    }

    private static func persistToInbox(_ payload: CapturePayload) throws {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let inbox = base
            .appendingPathComponent("Codex Bridge", isDirectory: true)
            .appendingPathComponent("CaptureInbox", isDirectory: true)
        try FileManager.default.createDirectory(at: inbox, withIntermediateDirectories: true)

        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        let data = try encoder.encode(payload)
        let name = "\(Date.now.timeIntervalSince1970)-\(UUID().uuidString)"
        let temporaryURL = inbox.appendingPathComponent(".\(name).tmp")
        let destinationURL = inbox.appendingPathComponent("\(name).json")
        try data.write(to: temporaryURL, options: .atomic)
        try FileManager.default.moveItem(at: temporaryURL, to: destinationURL)
    }

    @MainActor
    private static func openCodexBridge() async {
        guard let appURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "app.codexbridge.macos") else { return }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        _ = try? await NSWorkspace.shared.openApplication(at: appURL, configuration: configuration)
    }

    private static func readMessage() throws -> Data? {
        let input = FileHandle.standardInput
        guard let header = try input.read(upToCount: 4), !header.isEmpty else { return nil }
        guard header.count == 4 else { throw NativeHostError.invalidMessage("Native Messaging 消息头不完整") }
        let length = header.withUnsafeBytes { bytes in
            bytes.loadUnaligned(as: UInt32.self).littleEndian
        }
        guard length > 0, length <= UInt32(CaptureValidator.maximumPayloadBytes + 100_000) else {
            throw CaptureValidationError.oversized(Int(length))
        }
        var message = Data()
        while message.count < Int(length) {
            let remaining = Int(length) - message.count
            guard let chunk = try input.read(upToCount: remaining), !chunk.isEmpty else {
                throw NativeHostError.invalidMessage("Native Messaging 消息正文不完整")
            }
            message.append(chunk)
        }
        return message
    }

    private static func writeResponse(_ response: NativeResponse) throws {
        let data = try JSONEncoder().encode(response)
        var length = UInt32(data.count).littleEndian
        var framed = Data(bytes: &length, count: MemoryLayout<UInt32>.size)
        framed.append(data)
        try FileHandle.standardOutput.write(contentsOf: framed)
    }
}
