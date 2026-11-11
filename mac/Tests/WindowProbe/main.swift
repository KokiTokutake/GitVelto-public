// Opens real windows (like the app does) to reproduce layout crashes.
//   window-probe <action> <path>
import AppKit
setvbuf(stdout, nil, _IONBF, 0)
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let action = GitAction(rawValue: CommandLine.arguments[1])!
let path = CommandLine.arguments[2]
DispatchQueue.main.async {
    WindowManager.shared.perform(action, paths: [path])
    print("opened \(action.rawValue)")
}
DispatchQueue.main.asyncAfter(deadline: .now() + 4) { print("survived"); exit(0) }
app.run()
