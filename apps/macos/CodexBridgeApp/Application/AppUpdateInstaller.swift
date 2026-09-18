import CryptoKit
import Foundation

struct PreparedAppUpdate: Sendable {
    let release: AppRelease
    let stagedApplicationURL: URL
    let workingDirectoryURL: URL
}

protocol AppUpdateInstalling: Sendable {
    func prepare(_ release: AppRelease) async throws -> PreparedAppUpdate
    func launchInstallation(
        _ update: PreparedAppUpdate,
        replacing currentApplicationURL: URL,
        currentProcessID: Int32
    ) throws
}

enum AppUpdateInstallError: LocalizedError {
    case missingAsset
    case invalidDownload
    case untrustedDownloadSource
    case unexpectedFileSize
    case checksumMismatch
    case diskImageUnavailable
    case applicationMissing
    case applicationIdentityMismatch
    case applicationVersionMismatch
    case invalidCodeSignature
    case installationLocationReadOnly
    case installerUnavailable

    var errorDescription: String? {
        switch self {
        case .missingAsset:
            "这个版本没有可安全安装的下载文件，请改用发布页面。"
        case .invalidDownload:
            "更新文件下载失败，请稍后重试。"
        case .untrustedDownloadSource:
            "更新下载地址未通过安全检查，请改用发布页面。"
        case .unexpectedFileSize, .checksumMismatch:
            "更新文件校验失败，已停止安装。请改用发布页面。"
        case .diskImageUnavailable, .applicationMissing:
            "无法读取更新文件，请改用发布页面。"
        case .applicationIdentityMismatch, .applicationVersionMismatch:
            "下载的 App 与所选版本不一致，已停止安装。"
        case .invalidCodeSignature:
            "下载的 App 未通过代码签名校验，已停止安装。"
        case .installationLocationReadOnly:
            "Codex Bridge 所在位置无法直接更新，请从发布页面手动安装。"
        case .installerUnavailable:
            "无法启动更新程序，请改用发布页面。"
        }
    }
}

struct GitHubReleaseAppUpdateInstaller: AppUpdateInstalling {
    private static let bundleIdentifier = "app.codexbridge.macos"

    func prepare(_ release: AppRelease) async throws -> PreparedAppUpdate {
        guard let asset = release.asset else { throw AppUpdateInstallError.missingAsset }
        guard Self.isTrustedReleaseAssetURL(asset.downloadURL) else {
            throw AppUpdateInstallError.untrustedDownloadSource
        }

        var request = URLRequest(url: asset.downloadURL)
        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.setValue("Codex-Bridge/\(release.version)", forHTTPHeaderField: "User-Agent")
        request.timeoutInterval = 60
        let temporaryURL: URL
        let response: URLResponse
        do {
            (temporaryURL, response) = try await URLSession.shared.download(for: request)
        } catch {
            throw AppUpdateInstallError.invalidDownload
        }
        guard let response = response as? HTTPURLResponse,
              response.statusCode == 200,
              response.url.map(Self.isTrustedDownloadResponseURL) == true else {
            throw AppUpdateInstallError.invalidDownload
        }

        let workingDirectory = try makeWorkingDirectory(for: release.version)
        do {
            let diskImageURL = workingDirectory.appendingPathComponent(asset.name)
            try FileManager.default.moveItem(at: temporaryURL, to: diskImageURL)
            if let expectedSize = asset.size {
                let values = try diskImageURL.resourceValues(forKeys: [.fileSizeKey])
                guard Int64(values.fileSize ?? -1) == expectedSize else {
                    throw AppUpdateInstallError.unexpectedFileSize
                }
            }
            let digest = try Self.sha256(of: diskImageURL)
            guard digest == asset.sha256.lowercased() else {
                throw AppUpdateInstallError.checksumMismatch
            }

            let stagedApplicationURL = try await Task.detached(priority: .userInitiated) {
                try stageApplication(
                    from: diskImageURL,
                    in: workingDirectory,
                    expectedRelease: release
                )
            }.value
            return PreparedAppUpdate(
                release: release,
                stagedApplicationURL: stagedApplicationURL,
                workingDirectoryURL: workingDirectory
            )
        } catch {
            try? FileManager.default.removeItem(at: workingDirectory)
            throw error
        }
    }

    func launchInstallation(
        _ update: PreparedAppUpdate,
        replacing currentApplicationURL: URL,
        currentProcessID: Int32
    ) throws {
        let fileManager = FileManager.default
        let parentURL = currentApplicationURL.deletingLastPathComponent()
        guard currentApplicationURL.pathExtension == "app",
              fileManager.isWritableFile(atPath: parentURL.path),
              fileManager.fileExists(atPath: update.stagedApplicationURL.path) else {
            throw AppUpdateInstallError.installationLocationReadOnly
        }

        let helperURL = fileManager.temporaryDirectory
            .appendingPathComponent("codexbridge-updater-\(UUID().uuidString).sh")
        let logURL = fileManager.temporaryDirectory
            .appendingPathComponent("codexbridge-updater-\(UUID().uuidString).log")
        do {
            try Self.installerScript.write(to: helperURL, atomically: true, encoding: .utf8)
            try fileManager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: helperURL.path)

            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/bin/sh")
            process.arguments = [
                helperURL.path,
                String(currentProcessID),
                update.stagedApplicationURL.path,
                currentApplicationURL.path,
                update.workingDirectoryURL.path,
                logURL.path,
            ]
            try process.run()
        } catch {
            try? fileManager.removeItem(at: helperURL)
            throw AppUpdateInstallError.installerUnavailable
        }
    }

    static func sha256(of fileURL: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: fileURL)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 1_048_576), !chunk.isEmpty {
            hasher.update(data: chunk)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    static func isTrustedReleaseAssetURL(_ url: URL) -> Bool {
        url.scheme == "https"
            && url.host?.lowercased() == "github.com"
            && url.path.hasPrefix("/lijingpeng/codexbridge/releases/download/")
    }

    private static func isTrustedDownloadResponseURL(_ url: URL) -> Bool {
        guard url.scheme == "https", let host = url.host?.lowercased() else { return false }
        return isTrustedReleaseAssetURL(url)
            || host == "githubusercontent.com"
            || host.hasSuffix(".githubusercontent.com")
    }

    private func makeWorkingDirectory(for version: String) throws -> URL {
        let fileManager = FileManager.default
        let baseURL = try fileManager.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        .appendingPathComponent("Codex Bridge", isDirectory: true)
        .appendingPathComponent("Updates", isDirectory: true)
        try fileManager.createDirectory(at: baseURL, withIntermediateDirectories: true)

        let safeVersion = version.replacingOccurrences(
            of: "[^A-Za-z0-9._-]",
            with: "-",
            options: .regularExpression
        )
        let workingDirectory = baseURL
            .appendingPathComponent("\(safeVersion)-\(UUID().uuidString)", isDirectory: true)
        try fileManager.createDirectory(at: workingDirectory, withIntermediateDirectories: true)
        return workingDirectory
    }

    private func stageApplication(
        from diskImageURL: URL,
        in workingDirectory: URL,
        expectedRelease: AppRelease
    ) throws -> URL {
        let mountData: Data
        do {
            mountData = try UpdateProcess.run(
                executable: "/usr/bin/hdiutil",
                arguments: ["attach", diskImageURL.path, "-readonly", "-nobrowse", "-plist"]
            )
        } catch {
            throw AppUpdateInstallError.diskImageUnavailable
        }
        let mountPoints = Self.mountPoints(from: mountData)
        guard !mountPoints.isEmpty else { throw AppUpdateInstallError.diskImageUnavailable }
        defer {
            for mountPoint in mountPoints.reversed() {
                _ = try? UpdateProcess.run(
                    executable: "/usr/bin/hdiutil",
                    arguments: ["detach", mountPoint.path, "-force"]
                )
            }
        }

        guard let sourceApplication = Self.findApplication(in: mountPoints) else {
            throw AppUpdateInstallError.applicationMissing
        }
        try Self.validateApplication(sourceApplication, expectedRelease: expectedRelease)

        let stagedApplication = workingDirectory.appendingPathComponent("Codex Bridge.app", isDirectory: true)
        _ = try UpdateProcess.run(
            executable: "/usr/bin/ditto",
            arguments: [sourceApplication.path, stagedApplication.path]
        )
        try Self.validateApplication(stagedApplication, expectedRelease: expectedRelease)
        return stagedApplication
    }

    private static func mountPoints(from plistData: Data) -> [URL] {
        guard let plist = try? PropertyListSerialization.propertyList(from: plistData, format: nil),
              let root = plist as? [String: Any],
              let entities = root["system-entities"] as? [[String: Any]] else { return [] }
        return entities.compactMap { entity in
            (entity["mount-point"] as? String).map { URL(fileURLWithPath: $0, isDirectory: true) }
        }
    }

    private static func findApplication(in mountPoints: [URL]) -> URL? {
        let fileManager = FileManager.default
        for mountPoint in mountPoints {
            let preferred = mountPoint.appendingPathComponent("Codex Bridge.app", isDirectory: true)
            if fileManager.fileExists(atPath: preferred.path) { return preferred }
            guard let children = try? fileManager.contentsOfDirectory(
                at: mountPoint,
                includingPropertiesForKeys: [.isDirectoryKey],
                options: [.skipsHiddenFiles]
            ) else { continue }
            if let application = children.first(where: { $0.pathExtension == "app" }) {
                return application
            }
        }
        return nil
    }

    private static func validateApplication(_ applicationURL: URL, expectedRelease: AppRelease) throws {
        guard let bundle = Bundle(url: applicationURL),
              bundle.bundleIdentifier == bundleIdentifier else {
            throw AppUpdateInstallError.applicationIdentityMismatch
        }
        let bundledReleaseVersion = AppReleaseMetadata.version(in: bundle)
        if let bundledReleaseVersion {
            guard bundledReleaseVersion == expectedRelease.version else {
                throw AppUpdateInstallError.applicationVersionMismatch
            }
        } else {
            guard NumericVersion(expectedRelease.version)?.isPrerelease == false else {
                throw AppUpdateInstallError.applicationVersionMismatch
            }
            guard let shortVersion = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
                  NumericVersion(shortVersion)?.components == NumericVersion(expectedRelease.version)?.components else {
                throw AppUpdateInstallError.applicationVersionMismatch
            }
        }
        do {
            _ = try UpdateProcess.run(
                executable: "/usr/bin/codesign",
                arguments: ["--verify", "--deep", "--strict", applicationURL.path]
            )
        } catch {
            throw AppUpdateInstallError.invalidCodeSignature
        }
    }

    static let installerScript = #"""
    #!/bin/sh
    set -u
    APP_PID="$1"
    SOURCE_APP="$2"
    DESTINATION_APP="$3"
    WORKING_DIRECTORY="$4"
    LOG_FILE="$5"
    exec >>"$LOG_FILE" 2>&1

    attempts=0
    while /bin/kill -0 "$APP_PID" 2>/dev/null; do
      /bin/sleep 0.2
      attempts=$((attempts + 1))
      if [ "$attempts" -gt 150 ]; then exit 1; fi
    done

    BACKUP_APP="${DESTINATION_APP}.codexbridge-backup-$$"
    rollback_and_reopen() {
      /bin/rm -rf "$DESTINATION_APP"
      if [ -e "$BACKUP_APP" ]; then /bin/mv "$BACKUP_APP" "$DESTINATION_APP"; fi
      if [ -e "$DESTINATION_APP" ]; then /usr/bin/open "$DESTINATION_APP"; fi
      exit 1
    }

    /bin/rm -rf "$BACKUP_APP"
    if [ -e "$DESTINATION_APP" ]; then
      /bin/mv "$DESTINATION_APP" "$BACKUP_APP" || {
        /usr/bin/open "$DESTINATION_APP"
        exit 1
      }
    fi

    if ! /usr/bin/ditto "$SOURCE_APP" "$DESTINATION_APP"; then
      rollback_and_reopen
    fi
    if ! /usr/bin/codesign --verify --deep --strict "$DESTINATION_APP"; then
      rollback_and_reopen
    fi

    if ! /usr/bin/open "$DESTINATION_APP"; then
      rollback_and_reopen
    fi
    /bin/rm -rf "$BACKUP_APP" "$WORKING_DIRECTORY"
    /bin/rm -f "$0"
    """#
}

private enum UpdateProcess {
    static func run(executable: String, arguments: [String]) throws -> Data {
        let process = Process()
        let output = Pipe()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardOutput = output
        process.standardError = output
        try process.run()
        process.waitUntilExit()
        let data = output.fileHandleForReading.readDataToEndOfFile()
        guard process.terminationStatus == 0 else {
            throw AppUpdateInstallError.installerUnavailable
        }
        return data
    }
}
