import AppKit

// When git/ssh invoke us as GIT_ASKPASS / SSH_ASKPASS, show a credential prompt and exit.
if ProcessInfo.processInfo.environment["GITGLIDE_ASKPASS"] == "1" {
    AskPass.run(prompt: CommandLine.arguments.dropFirst().joined(separator: " "))
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
