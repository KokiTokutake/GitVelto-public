// Simulates the Finder extension: asks the running GitVelto app for badges of a directory.
//   ipc-test <dir>
import Foundation
setvbuf(stdout, nil, _IONBF, 0)
let dir = CommandLine.arguments[1]
let dnc = DistributedNotificationCenter.default()
var received = 0
let obs = dnc.addObserver(forName: Notification.Name(KG.Note.badges), object: nil, queue: .main) { n in
    guard let json = n.object as? String, let p = try? JSONDecoder().decode(BadgePayload.self, from: Data(json.utf8)),
          p.dir == dir else { return }
    received += 1
    print("badges for \(p.dir) repo=\(p.repo ?? "-") children-repos=\(p.repoChildren)")
    for (k, v) in p.items.sorted(by: { $0.key < $1.key }) { print("   \(v.isEmpty ? "(none)" : v)\t\(k)") }
}
let cfgObs = dnc.addObserver(forName: Notification.Name(KG.Note.menuConfig), object: nil, queue: .main) { n in
    print("config received: \(n.object as? String ?? "-")")
}
dnc.postNotificationName(Notification.Name(KG.Note.requestConfig), object: "", userInfo: nil, deliverImmediately: true)
dnc.postNotificationName(Notification.Name(KG.Note.observe), object: dir, userInfo: nil, deliverImmediately: true)
RunLoop.main.run(until: Date().addingTimeInterval(4))
dnc.postNotificationName(Notification.Name(KG.Note.unobserve), object: dir, userInfo: nil, deliverImmediately: true)
print(received > 0 ? "OK: \(received) payload(s)" : "NG: no reply")
_ = obs; _ = cfgObs
