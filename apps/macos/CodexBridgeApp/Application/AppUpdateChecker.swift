import Foundation

struct AppRelease: Equatable, Sendable {
    let version: String
    let title: String
    let pageURL: URL
    let publishedAt: Date?
}

enum AppUpdateState: Equatable, Sendable {
    case idle
    case checking
    case upToDate
    case available(AppRelease)
    case failed(String)
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
            .filter { !$0.draft }
            .compactMap { release -> (NumericVersion, AppRelease)? in
                guard let version = NumericVersion(release.tagName), version > current,
                      let pageURL = URL(string: release.htmlURL) else { return nil }
                return (
                    version,
                    AppRelease(
                        version: version.description,
                        title: release.name?.isEmpty == false ? release.name! : release.tagName,
                        pageURL: pageURL,
                        publishedAt: release.publishedAt
                    )
                )
            }
            .max { $0.0 < $1.0 }?
            .1
    }
}

private struct GitHubRelease: Decodable {
    let tagName: String
    let name: String?
    let htmlURL: String
    let draft: Bool
    let publishedAt: Date?

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case name
        case htmlURL = "html_url"
        case draft
        case publishedAt = "published_at"
    }
}

private struct NumericVersion: Comparable, CustomStringConvertible {
    let components: [Int]

    init?(_ value: String) {
        var candidate = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if candidate.lowercased().hasPrefix("v") { candidate.removeFirst() }
        let normalized = candidate
            .split(separator: "-", maxSplits: 1)
            .first
            .map(String.init) ?? candidate
        let values = normalized.split(separator: ".").compactMap { Int($0) }
        guard !values.isEmpty else { return nil }
        components = values + Array(repeating: 0, count: max(0, 3 - values.count))
    }

    var description: String {
        components.prefix(3).map(String.init).joined(separator: ".")
    }

    static func < (lhs: NumericVersion, rhs: NumericVersion) -> Bool {
        let count = max(lhs.components.count, rhs.components.count)
        for index in 0..<count {
            let left = index < lhs.components.count ? lhs.components[index] : 0
            let right = index < rhs.components.count ? rhs.components[index] : 0
            if left != right { return left < right }
        }
        return false
    }
}

private extension JSONDecoder {
    static var codexBridgeReleaseDecoder: JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }
}
