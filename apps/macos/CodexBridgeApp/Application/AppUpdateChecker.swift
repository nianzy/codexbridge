import Foundation

struct AppReleaseAsset: Equatable, Sendable {
    let name: String
    let downloadURL: URL
    let sha256: String
    let size: Int64?
}

struct AppRelease: Equatable, Sendable {
    let version: String
    let title: String
    let pageURL: URL
    let publishedAt: Date?
    let isPrerelease: Bool
    let asset: AppReleaseAsset?
}

enum AppReleaseMetadata {
    static func version(in bundle: Bundle) -> String? {
        guard let url = bundle.url(forResource: "release-version", withExtension: "txt"),
              let value = try? String(contentsOf: url, encoding: .utf8)
                .trimmingCharacters(in: .whitespacesAndNewlines),
              NumericVersion(value) != nil else { return nil }
        return value
    }
}

enum AppUpdateState: Equatable, Sendable {
    case idle
    case checking
    case upToDate
    case available(AppRelease)
    case downloading(AppRelease)
    case installing(AppRelease)
    case failed(String, AppRelease?)
}

protocol AppUpdateChecking: Sendable {
    func latestRelease(newerThan currentVersion: String) async throws -> AppRelease?
}

enum AppUpdateCheckError: LocalizedError {
    case invalidResponse

    var errorDescription: String? {
        "暂时无法检查更新，请稍后重试。"
    }
}

struct GitHubReleaseUpdateChecker: AppUpdateChecking {
    private let releasesURL = URL(string: "https://api.github.com/repos/lijingpeng/codexbridge/releases?per_page=20")!

    func latestRelease(newerThan currentVersion: String) async throws -> AppRelease? {
        var request = URLRequest(url: releasesURL)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        request.setValue("Codex-Bridge/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        request.timeoutInterval = 12

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let response = response as? HTTPURLResponse, response.statusCode == 200 else {
            throw AppUpdateCheckError.invalidResponse
        }
        return try Self.newerRelease(in: data, than: currentVersion)
    }

    static func newerRelease(in data: Data, than currentVersion: String) throws -> AppRelease? {
        let releases = try JSONDecoder.codexBridgeReleaseDecoder.decode([GitHubRelease].self, from: data)
        guard let current = NumericVersion(currentVersion) else { return nil }
        return releases
            .filter { !$0.draft && (current.isPrerelease || !$0.isPrerelease) }
            .compactMap { release -> (NumericVersion, AppRelease)? in
                guard let version = NumericVersion(release.tagName), version > current,
                      let pageURL = URL(string: release.htmlURL) else { return nil }
                return (
                    version,
                    AppRelease(
                        version: version.description,
                        title: release.name?.isEmpty == false ? release.name! : release.tagName,
                        pageURL: pageURL,
                        publishedAt: release.publishedAt,
                        isPrerelease: release.isPrerelease,
                        asset: downloadableAsset(from: release.assets ?? [])
                    )
                )
            }
            .max { $0.0 < $1.0 }?
            .1
    }

    private static func downloadableAsset(from assets: [GitHubReleaseAsset]) -> AppReleaseAsset? {
        assets
            .compactMap { asset -> (Int, AppReleaseAsset)? in
                guard asset.name.lowercased().hasSuffix(".dmg"),
                      let downloadURL = URL(string: asset.browserDownloadURL),
                      let sha256 = normalizedSHA256(asset.digest) else { return nil }
                let lowercasedName = asset.name.lowercased()
                let score = lowercasedName.contains("universal") ? 2 : 1
                return (
                    score,
                    AppReleaseAsset(
                        name: asset.name,
                        downloadURL: downloadURL,
                        sha256: sha256,
                        size: asset.size
                    )
                )
            }
            .max { lhs, rhs in lhs.0 < rhs.0 }?
            .1
    }

    private static func normalizedSHA256(_ digest: String?) -> String? {
        guard let digest else { return nil }
        let parts = digest.lowercased().split(separator: ":", maxSplits: 1)
        guard parts.count == 2, parts[0] == "sha256", parts[1].count == 64,
              parts[1].allSatisfy({ $0.isHexDigit }) else { return nil }
        return String(parts[1])
    }
}

private struct GitHubRelease: Decodable {
    let tagName: String
    let name: String?
    let htmlURL: String
    let draft: Bool
    let publishedAt: Date?
    let prerelease: Bool?
    let assets: [GitHubReleaseAsset]?

    var isPrerelease: Bool { prerelease ?? false }

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case name
        case htmlURL = "html_url"
        case draft
        case publishedAt = "published_at"
        case prerelease
        case assets
    }
}

private struct GitHubReleaseAsset: Decodable {
    let name: String
    let browserDownloadURL: String
    let size: Int64?
    let digest: String?

    enum CodingKeys: String, CodingKey {
        case name
        case browserDownloadURL = "browser_download_url"
        case size
        case digest
    }
}

struct NumericVersion: Comparable, CustomStringConvertible, Sendable {
    private enum PrereleaseIdentifier: Equatable, Sendable {
        case number(Int)
        case text(String)
    }

    let components: [Int]
    private let prerelease: [PrereleaseIdentifier]

    var isPrerelease: Bool { !prerelease.isEmpty }

    init?(_ value: String) {
        var candidate = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if candidate.lowercased().hasPrefix("v") { candidate.removeFirst() }
        let versionAndMetadata = candidate.split(separator: "+", maxSplits: 1, omittingEmptySubsequences: false)[0]
        let parts = versionAndMetadata.split(separator: "-", maxSplits: 1, omittingEmptySubsequences: false)
        let values = parts[0].split(separator: ".").compactMap { Int($0) }
        guard !values.isEmpty, values.count == parts[0].split(separator: ".").count else { return nil }
        components = values + Array(repeating: 0, count: max(0, 3 - values.count))
        prerelease = parts.count == 2
            ? parts[1].split(separator: ".").map { value in
                Int(value).map(PrereleaseIdentifier.number) ?? .text(value.lowercased())
            }
            : []
    }

    var description: String {
        let core = components.prefix(3).map(String.init).joined(separator: ".")
        guard !prerelease.isEmpty else { return core }
        let suffix = prerelease.map { identifier in
            switch identifier {
            case let .number(value): String(value)
            case let .text(value): value
            }
        }.joined(separator: ".")
        return "\(core)-\(suffix)"
    }

    static func < (lhs: NumericVersion, rhs: NumericVersion) -> Bool {
        let count = max(lhs.components.count, rhs.components.count)
        for index in 0..<count {
            let left = index < lhs.components.count ? lhs.components[index] : 0
            let right = index < rhs.components.count ? rhs.components[index] : 0
            if left != right { return left < right }
        }
        if lhs.prerelease.isEmpty { return false }
        if rhs.prerelease.isEmpty { return true }
        for index in 0..<min(lhs.prerelease.count, rhs.prerelease.count) {
            let left = lhs.prerelease[index]
            let right = rhs.prerelease[index]
            guard left != right else { continue }
            switch (left, right) {
            case let (.number(left), .number(right)): return left < right
            case (.number, .text): return true
            case (.text, .number): return false
            case let (.text(left), .text(right)): return left < right
            }
        }
        return lhs.prerelease.count < rhs.prerelease.count
    }
}

private extension JSONDecoder {
    static var codexBridgeReleaseDecoder: JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }
}
