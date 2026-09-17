import AppKit
import SwiftUI

enum CodexBridgeRelease {
    static let channel = "Beta"

    static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "—"
    }

    static var displayVersion: String {
        "\(version) \(channel)"
    }
}

@main
struct CodexBridgeApp: App {
    @State private var model = AppModel.live()
    @State private var accessibility = AccessibilityPermissionController()
    @AppStorage("codexbridge.appearance") private var appearance = "system"

    init() {
        LegacyProductMigration.migrateUserDefaults()
    }

    var body: some Scene {
        Window("Codex Bridge", id: "main") {
            RootView(model: model, accessibility: accessibility)
            .background(MainWindowAccessor(appearance: appearance))
            .preferredColorScheme(preferredColorScheme)
            .task {
                model.synchronizePreparedBrowserExtension()
                await model.bootstrap()
                await model.checkForUpdatesAutomatically()
            }
        }
        .defaultSize(width: 1240, height: 780)
        .windowResizability(.contentMinSize)

        MenuBarExtra("Codex Bridge", systemImage: "sidebar.right") {
            Button("显示 Codex Bridge") {
                NSApplication.shared.activate(ignoringOtherApps: true)
                mainWindow?.makeKeyAndOrderFront(nil)
            }
            Divider()
            Button("退出 Codex Bridge") { NSApplication.shared.terminate(nil) }
        }
        .menuBarExtraStyle(.menu)

        Settings {
            SettingsView(model: model, accessibility: accessibility)
                .frame(width: 760, height: 560)
        }
    }

    private var mainWindow: NSWindow? {
        NSApplication.shared.windows.first {
            $0.identifier == MainWindowAccessor.windowIdentifier
        }
    }

    private var preferredColorScheme: ColorScheme? {
        switch appearance {
        case "light": .light
        case "dark": .dark
        default: nil
        }
    }
}

private enum LegacyProductMigration {
    static func migrateUserDefaults() {
        let current = UserDefaults.standard
        let legacy = UserDefaults(suiteName: "app.sidely.macos")
        let keys = [
            "appearance",
            "recent-workspaces",
            "pinned-conversation-ids",
            "archived-conversation-ids",
            "navigation-column-width",
            "conversation-column-width",
        ]
        for key in keys {
            let newKey = "codexbridge.\(key)"
            guard current.object(forKey: newKey) == nil else { continue }
            if let value = legacy?.object(forKey: "sidely.\(key)") {
                current.set(value, forKey: newKey)
            }
        }
    }
}

private struct MainWindowAccessor: NSViewRepresentable {
    static let windowIdentifier = NSUserInterfaceItemIdentifier("app.codexbridge.main-window")
    let appearance: String

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        configureWindow(for: view)
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        configureWindow(for: nsView)
    }

    private func configureWindow(for view: NSView) {
        DispatchQueue.main.async { [weak view] in
            guard let window = view?.window else { return }
            window.identifier = Self.windowIdentifier
            window.title = "Codex Bridge"
            window.titlebarAppearsTransparent = true
            window.styleMask.insert(.fullSizeContentView)
            window.isMovableByWindowBackground = false
            window.appearance = appearance == "system"
                ? nil
                : NSAppearance(named: appearance == "dark" ? .darkAqua : .aqua)
        }
    }
}
