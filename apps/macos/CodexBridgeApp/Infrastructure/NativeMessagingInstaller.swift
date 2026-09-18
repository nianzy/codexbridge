import AppKit
import Foundation

enum NativeMessagingInstallError: LocalizedError {
    case missingHelper
    case missingExtension
    case browserUnavailable(String)
    case defaultBrowserUnavailable

    var errorDescription: String? {
        switch self {
        case .missingHelper: "Codex Bridge 安装包中缺少浏览器连接组件，请重新安装 App。"
        case .missingExtension: "Codex Bridge 安装包中缺少浏览器扩展文件。"
        case let .browserUnavailable(name): "没有找到\(name)，请先安装后再继续。"
        case .defaultBrowserUnavailable: "无法使用系统默认浏览器打开 ChatGPT。"
        }
    }
}

enum SupportedExtensionBrowser: String, CaseIterable, Identifiable, Sendable {
    case chrome
    case edge

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .chrome: "Google Chrome"
        case .edge: "Microsoft Edge"
        }
    }

    var shortName: String {
        switch self {
        case .chrome: "Chrome"
        case .edge: "Edge"
        }
    }

    var bundleIdentifier: String {
        switch self {
        case .chrome: "com.google.Chrome"
        case .edge: "com.microsoft.edgemac"
        }
    }

    var extensionsPage: String {
        switch self {
        case .chrome: "chrome://extensions/"
        case .edge: "edge://extensions/"
        }
    }

    var applicationSupportComponents: [String] {
        switch self {
        case .chrome: ["Google", "Chrome"]
        case .edge: ["Microsoft Edge"]
        }
    }
}

enum BrowserExtensionInstallation: Equatable, Sendable {
    case missing
    case disabled(browser: SupportedExtensionBrowser, profile: String)
    case enabled(browser: SupportedExtensionBrowser, profile: String)
}

struct PreparedBrowserExtension: Equatable, Sendable {
    let url: URL
    let version: String
    let replacedExistingInstallation: Bool
}

struct NativeMessagingInstaller: Sendable {
    static let extensionID = "pnpgopcjhgfmnkefmdnoebmcbnnhnhee"

    private let extensionSourceURL: URL?
    private let applicationSupportURL: URL?

    init(extensionSourceURL: URL? = nil, applicationSupportURL: URL? = nil) {
        self.extensionSourceURL = extensionSourceURL
        self.applicationSupportURL = applicationSupportURL
    }

    func install() throws -> [URL] {
        let helperURL = Bundle.main.bundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Helpers", isDirectory: true)
            .appendingPathComponent("CodexBridgeNativeHost")
        guard FileManager.default.isExecutableFile(atPath: helperURL.path) else {
            throw NativeMessagingInstallError.missingHelper
        }

        let applicationSupport = try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        )
        let manifest: [String: Any] = [
            "name": "app.codexbridge.nativehost",
            "description": "Codex Bridge Native Messaging Host",
            "path": helperURL.path,
            "type": "stdio",
            "allowed_origins": ["chrome-extension://\(Self.extensionID)/"],
        ]
        let legacyManifest: [String: Any] = [
            "name": "app.sidely.nativehost",
            "description": "Codex Bridge Native Messaging Host compatibility entry",
            "path": helperURL.path,
            "type": "stdio",
            "allowed_origins": ["chrome-extension://\(Self.extensionID)/"],
        ]
        let data = try JSONSerialization.data(withJSONObject: manifest, options: [.prettyPrinted, .sortedKeys])
        let legacyData = try JSONSerialization.data(withJSONObject: legacyManifest, options: [.prettyPrinted, .sortedKeys])
        return try SupportedExtensionBrowser.allCases.map { browser in
            let directory = browser.applicationSupportComponents.reduce(applicationSupport) { partial, component in
                partial.appendingPathComponent(component, isDirectory: true)
            }
            .appendingPathComponent("NativeMessagingHosts", isDirectory: true)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let manifestURL = directory.appendingPathComponent("app.codexbridge.nativehost.json")
            try data.write(to: manifestURL, options: .atomic)
            let legacyManifestURL = directory.appendingPathComponent("app.sidely.nativehost.json")
            try legacyData.write(to: legacyManifestURL, options: .atomic)
            return manifestURL
        }
    }

    func extensionDirectory() throws -> URL {
        guard let directory = extensionSourceURL
                ?? Bundle.main.resourceURL?.appendingPathComponent("extension", isDirectory: true),
              FileManager.default.fileExists(atPath: directory.appendingPathComponent("manifest.json").path) else {
            throw NativeMessagingInstallError.missingExtension
        }
        return directory
    }

    /// Chromium 浏览器的“加载已解压的扩展程序”需要普通文件夹，不能直接选择 App 包内资源。
    func prepareExtensionDirectory() throws -> PreparedBrowserExtension {
        let source = try extensionDirectory()
        guard let sourceVersion = extensionVersion(at: source) else {
            throw NativeMessagingInstallError.missingExtension
        }
        let applicationSupport = try applicationSupportDirectory(create: true)
        let legacyDestination = applicationSupport
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("ChromeExtension", isDirectory: true)
        let support = applicationSupport.appendingPathComponent("Codex Bridge", isDirectory: true)
        try FileManager.default.createDirectory(at: support, withIntermediateDirectories: true)
        // 已加载的 unpacked extension 绑定原路径；首次改名时原地更新，避免浏览器失去扩展。
        let destination = FileManager.default.fileExists(atPath: legacyDestination.path)
            ? legacyDestination
            : support.appendingPathComponent("ChromeExtension", isDirectory: true)
        let destinationExists = FileManager.default.fileExists(atPath: destination.path)
        let installedVersion = extensionVersion(at: destination)
        let requiresCopy = !destinationExists || installedVersion != sourceVersion
        if requiresCopy {
            let staging = support.appendingPathComponent(".ChromeExtension-\(UUID().uuidString)", isDirectory: true)
            try FileManager.default.copyItem(at: source, to: staging)
            if destinationExists {
                try FileManager.default.removeItem(at: destination)
            }
            try FileManager.default.moveItem(at: staging, to: destination)
        }
        return PreparedBrowserExtension(
            url: destination,
            version: sourceVersion,
            replacedExistingInstallation: destinationExists && requiresCopy
        )
    }

    func preparedExtensionDirectory() -> URL? {
        guard let support = try? applicationSupportDirectory(create: false) else { return nil }
        let directory = support
            .appendingPathComponent("Codex Bridge", isDirectory: true)
            .appendingPathComponent("ChromeExtension", isDirectory: true)
        if FileManager.default.fileExists(atPath: directory.appendingPathComponent("manifest.json").path) {
            return directory
        }
        let legacyDirectory = support
            .appendingPathComponent("Sidely", isDirectory: true)
            .appendingPathComponent("ChromeExtension", isDirectory: true)
        return FileManager.default.fileExists(atPath: legacyDirectory.appendingPathComponent("manifest.json").path)
            ? legacyDirectory
            : nil
    }

    private func extensionVersion(at directory: URL) -> String? {
        let manifestURL = directory.appendingPathComponent("manifest.json")
        guard let data = try? Data(contentsOf: manifestURL),
              let manifest = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }
        return manifest["version"] as? String
    }

    private func applicationSupportDirectory(create: Bool) throws -> URL {
        if let applicationSupportURL {
            if create {
                try FileManager.default.createDirectory(at: applicationSupportURL, withIntermediateDirectories: true)
            }
            return applicationSupportURL
        }
        return try FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: create
        )
    }

    func hasCurrentNativeHostManifest() -> Bool {
        let helperURL = Bundle.main.bundleURL
            .appendingPathComponent("Contents", isDirectory: true)
            .appendingPathComponent("Helpers", isDirectory: true)
            .appendingPathComponent("CodexBridgeNativeHost")
        guard let support = try? FileManager.default.url(
                for: .applicationSupportDirectory,
                in: .userDomainMask,
                appropriateFor: nil,
                create: false
              ) else { return false }
        guard FileManager.default.isExecutableFile(atPath: helperURL.path) else { return false }
        return SupportedExtensionBrowser.allCases.contains { browser in
            let root = browser.applicationSupportComponents.reduce(support) { partial, component in
                partial.appendingPathComponent(component, isDirectory: true)
            }
            let manifestURL = root
                .appendingPathComponent("NativeMessagingHosts", isDirectory: true)
                .appendingPathComponent("app.codexbridge.nativehost.json")
            guard let data = try? Data(contentsOf: manifestURL),
                  let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return false }
            return object["path"] as? String == helperURL.path
        }
    }

    func browserExtensionInstallation() -> BrowserExtensionInstallation {
        guard let support = try? FileManager.default.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: false
        ) else { return .missing }
        let roots = SupportedExtensionBrowser.allCases.map { browser in
            let root = browser.applicationSupportComponents.reduce(support) { partial, component in
                partial.appendingPathComponent(component, isDirectory: true)
            }
            return (browser, root)
        }
        return Self.browserExtensionInstallation(browserRoots: roots)
    }

    static func browserExtensionInstallation(
        browserRoots: [(SupportedExtensionBrowser, URL)]
    ) -> BrowserExtensionInstallation {
        var disabled: BrowserExtensionInstallation?
        for (browser, browserRoot) in browserRoots {
            guard let profiles = try? FileManager.default.contentsOfDirectory(
                at: browserRoot,
                includingPropertiesForKeys: [.isDirectoryKey],
                options: [.skipsHiddenFiles]
            ) else { continue }
            for profile in profiles.sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {
                for fileName in ["Preferences", "Secure Preferences"] {
                    let preferencesURL = profile.appendingPathComponent(fileName)
                    guard let data = try? Data(contentsOf: preferencesURL, options: [.mappedIfSafe]),
                          let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                          let extensions = root["extensions"] as? [String: Any],
                          let settings = extensions["settings"] as? [String: Any],
                          let entry = settings[extensionID] as? [String: Any] else { continue }
                    let state = (entry["state"] as? NSNumber)?.intValue
                    let disableReasons = entry["disable_reasons"] as? [Any] ?? []
                    if state != 0, disableReasons.isEmpty {
                        return .enabled(browser: browser, profile: profile.lastPathComponent)
                    }
                    disabled = .disabled(browser: browser, profile: profile.lastPathComponent)
                }
            }
        }
        return disabled ?? .missing
    }

    @MainActor
    func isBrowserInstalled(_ browser: SupportedExtensionBrowser) -> Bool {
        NSWorkspace.shared.urlForApplication(withBundleIdentifier: browser.bundleIdentifier) != nil
    }

    @MainActor
    func openExtensionsPage(in browser: SupportedExtensionBrowser) async throws {
        guard let browserURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: browser.bundleIdentifier) else {
            throw NativeMessagingInstallError.browserUnavailable(browser.displayName)
        }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.arguments = [browser.extensionsPage]
        _ = try await NSWorkspace.shared.openApplication(at: browserURL, configuration: configuration)
    }

    @MainActor
    func openChatGPTWeb() async throws {
        guard let url = URL(string: "https://chatgpt.com/"), NSWorkspace.shared.open(url) else {
            throw NativeMessagingInstallError.defaultBrowserUnavailable
        }
    }
}

extension URL {
    func ensuringDirectory() throws -> URL {
        try FileManager.default.createDirectory(at: self, withIntermediateDirectories: true)
        return self
    }
}
