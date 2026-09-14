import AppKit
import ApplicationServices
import Observation

@MainActor
@Observable
final class AccessibilityPermissionController {
    var isTrusted = AXIsProcessTrusted()

    func requestAccess() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        isTrusted = AXIsProcessTrustedWithOptions(options)
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    func refresh() {
        isTrusted = AXIsProcessTrusted()
    }
}
