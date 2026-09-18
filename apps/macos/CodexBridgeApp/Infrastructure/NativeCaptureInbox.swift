import Foundation

enum NativeCaptureInbox {
    static func pendingCaptureURLs() throws -> [URL] {
        var directories = [try inboxDirectory()]
        if let legacyDirectory = try legacyInboxDirectory() {
            directories.append(legacyDirectory)
        }
        return directories.flatMap { directory in
            (try? FileManager.default.contentsOfDirectory(
                at: directory,
                includingPropertiesForKeys: [.creationDateKey],
                options: [.skipsHiddenFiles]
            )) ?? []
        }
        .filter { $0.pathExtension == "json" }
        .sorted { $0.lastPathComponent < $1.lastPathComponent }
    }

    static func quarantine(_ url: URL) throws {
        let directory = try supportDirectory().appendingPathComponent("InvalidCaptures", isDirectory: true).ensuringDirectory()
        let target = directory.appendingPathComponent(url.lastPathComponent)
        if FileManager.default.fileExists(atPath: target.path) {
            try FileManager.default.removeItem(at: target)
        }
        try FileManager.default.moveItem(at: url, to: target)
    }

    private static func inboxDirectory() throws -> URL {
        try supportDirectory().appendingPathComponent("CaptureInbox", isDirectory: true).ensuringDirectory()
    }

    private static func legacyInboxDirectory() throws -> URL? {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let directory = base
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("CaptureInbox", isDirectory: true)
        return FileManager.default.fileExists(atPath: directory.path) ? directory : nil
    }

    private static func supportDirectory() throws -> URL {
        let base = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        return try base.appendingPathComponent("Codex Bridge", isDirectory: true).ensuringDirectory()
    }
}
