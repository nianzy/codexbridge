import AppKit
import SwiftUI

struct RootView: View {
    @Bindable var model: AppModel
    @Bindable var accessibility: AccessibilityPermissionController
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ConversationWorkspace(model: model)
            .frame(minWidth: 960, minHeight: 620)
            .sheet(isPresented: $model.isHandoffPresented) {
                HandoffEditorView(model: model)
            }
            .sheet(isPresented: $model.isManualCapturePresented) {
                ManualChatGPTCaptureView(model: model).frame(width: 620, height: 540)
            }
            .sheet(isPresented: $model.isSourcesPresented) {
                SourcesView(model: model, accessibility: accessibility)
                    .frame(width: 690, height: 610)
            }
            .sheet(isPresented: $model.isChatGPTDraftPresented) {
                SimpleChatGPTDraftEditor(model: model)
                    .frame(minWidth: 760, minHeight: 580)
            }
            .sheet(isPresented: $model.isContinuePresented) {
                ContinueCodexDialog(model: model)
                    .frame(width: 540, height: 300)
            }
            .sheet(item: $model.pendingCodexInteraction) { request in
                CodexInteractionDialog(model: model, request: request)
                    .frame(width: 560, height: request.kind == .userInput ? 430 : 390)
                    .interactiveDismissDisabled()
            }
            .overlay {
                if let error = model.errorMessage {
                    ZStack {
                        Color.black.opacity(0.22).ignoresSafeArea()
                        CodexBridgeErrorDialog(message: error, dismiss: model.dismissError)
                    }
                    .transition(.opacity)
                    .zIndex(20)
                }
            }
            .overlay(alignment: .bottom) {
                if let message = model.statusMessage {
                    CodexBridgeToast(message: message)
                        .padding(.bottom, 18)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                        .zIndex(10)
                }
            }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.2), value: model.statusMessage)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.18), value: model.errorMessage)
            .onReceive(DistributedNotificationCenter.default().publisher(for: Notification.Name("app.codexbridge.captureImported"))) { _ in
                Task { await model.reloadConversations() }
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
                Task {
                    await model.refreshAutomaticallyIfReady(reconcileArchives: true)
                    await model.checkForUpdatesAutomatically()
                }
            }
            .task(id: model.selectedConversationID) {
                await model.loadSelectedConversationDetails()
            }
            .task {
                while !Task.isCancelled {
                    do {
                        try await Task.sleep(for: .seconds(15))
                    } catch {
                        break
                    }
                    guard NSApplication.shared.isActive else { continue }
                    await model.refreshAutomaticallyIfReady()
                    await model.checkForUpdatesAutomatically()
                }
            }
    }
}
