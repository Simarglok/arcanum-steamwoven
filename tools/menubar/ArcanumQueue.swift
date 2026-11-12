// ARC-DEV-1344: the menu-bar view of the launch queue (docs/workflow/toolchain.md#queue-menu-bar-app-arc-dev-1344).
//
// One file, AppKit only, no dependencies: built on this Mac by tools/local_dev/menubar.py (swiftc) and never committed as
// a binary. It only READS the queue's ticket files (the queue has no daemon: each ticket is a file whose flock its run holds)
// and sets or clears the pause flag (the file `paused` in the queue folder), which every waiting run checks.
import AppKit
import Darwin
import Foundation

// MARK: - the queue folder

struct Job {
    let seq: Int
    let kind: String
    let lane: String?
    let label: String
    let command: String
    let checkout: String
    let enqueuedAt: Double
    let startedAt: Double?
    let childPid: Int32?
    let priority: Int
    let estimate: Double?
}

func lockedByOthers(_ path: String) -> Bool {
    let fd = open(path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW)
    if fd < 0 { return false }
    defer { close(fd) }
    if flock(fd, LOCK_SH | LOCK_NB) == 0 {
        flock(fd, LOCK_UN)
        return false
    }
    return errno == EWOULDBLOCK || errno == EAGAIN
}

func processAlive(_ pid: Any?) -> Bool {
    guard let number = pid as? NSNumber, number.int32Value > 0 else { return false }
    return kill(number.int32Value, 0) == 0 || errno == EPERM
}

struct QueueFolder {
    let url: URL
    var pauseURL: URL { url.appendingPathComponent("paused") }
    var isPaused: Bool { FileManager.default.fileExists(atPath: pauseURL.path) }

    /// Live tickets in the queue's own order (priority, then arrival plus three of the run's estimates, then number;
    /// by number alone while a ticket of an older wrapper, with no estimate, waits).
    func jobs() -> [Job] {
        let names = (try? FileManager.default.contentsOfDirectory(atPath: url.path)) ?? []
        var found: [Job] = []
        for name in names where name.hasPrefix("t-") && name.hasSuffix(".json") {
            let path = url.appendingPathComponent(name).path
            guard let data = FileManager.default.contents(atPath: path),
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  json["schema"] as? String == "arcanum-run-queue-v1", let seq = json["seq"] as? Int else {
                // a held ticket this version cannot read (another version's format) blocks the queue like a running export
                if lockedByOthers(path), let seq = Int(name.dropFirst(2).dropLast(5)) {
                    found.append(Job(seq: seq, kind: "export", lane: nil, label: "unreadable ticket", command: "", checkout: "?",
                                     enqueuedAt: Date().timeIntervalSince1970, startedAt: Date().timeIntervalSince1970,
                                     childPid: nil, priority: 0, estimate: nil))
                }
                continue
            }
            guard lockedByOthers(path), processAlive(json["pid"]) || processAlive(json["childPid"]) else { continue }
            let cwd = json["cwd"] as? String ?? "?"
            found.append(Job(seq: seq, kind: json["kind"] as? String ?? "?", lane: json["lane"] as? String,
                             label: json["label"] as? String ?? "", command: json["command"] as? String ?? "",
                             checkout: URL(fileURLWithPath: json["checkout"] as? String ?? cwd).lastPathComponent,
                             enqueuedAt: (json["enqueuedAt"] as? NSNumber)?.doubleValue ?? 0,
                             startedAt: (json["startedAt"] as? NSNumber)?.doubleValue,
                             childPid: (json["childPid"] as? NSNumber)?.int32Value,
                             priority: json["priority"] as? Int ?? 1,
                             estimate: (json["estimateSeconds"] as? NSNumber)?.doubleValue))
        }
        let byNumber = found.contains { $0.startedAt == nil && $0.estimate == nil }
        let defaults = ["window": 60.0, "headless": 60.0, "export": 300.0, "dotnet": 60.0, "quick": 15.0]
        func estimate(_ job: Job) -> Double {
            if let value = job.estimate, value >= 0 { return min(value, 86400) }
            return defaults[job.kind] ?? 60
        }
        func key(_ job: Job) -> (Int, Double, Int) {
            (job.priority, byNumber ? 0 : job.enqueuedAt + 3 * estimate(job), job.seq)
        }
        return found.sorted { key($0) < key($1) }
    }

    /// The owner's caps (ARC-DEV-1353: `./arcanum queue --set-lanes / --set-max-load`): the file `settings.json` of the queue
    /// folder, {"shortLanes": 0...8, "maxLoad": number}; a missing key is the default / no guard.
    var settingsURL: URL { url.appendingPathComponent("settings.json") }

    func settings() -> [String: Any] {
        guard let data = FileManager.default.contents(atPath: settingsURL.path),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        return json
    }

    var shortLanes: Int? { (settings()["shortLanes"] as? NSNumber).flatMap { 0...8 ~= $0.intValue ? $0.intValue : nil } }
    var maxLoad: Double? { (settings()["maxLoad"] as? NSNumber).flatMap { $0.doubleValue > 0 ? $0.doubleValue : nil } }

    /// Merge changes into settings.json the way the queue does: under the queue lock, written aside and renamed (nil removes a key).
    func change(_ changes: [String: Any?]) {
        let lock = open(url.appendingPathComponent("queue.lock").path, O_RDWR | O_CREAT | O_CLOEXEC | O_NOFOLLOW, 0o600)
        if lock < 0 { return }
        defer { close(lock) }
        flock(lock, LOCK_EX)
        defer { flock(lock, LOCK_UN) }
        var merged = settings()
        for (key, value) in changes {
            if let value = value { merged[key] = value } else { merged.removeValue(forKey: key) }
        }
        guard let data = try? JSONSerialization.data(withJSONObject: merged, options: [.sortedKeys]) else { return }
        let hidden = url.appendingPathComponent(".settings-menubar-\(getpid())")
        guard (try? data.write(to: hidden)) != nil else { return }
        chmod(hidden.path, 0o600)
        rename(hidden.path, settingsURL.path)
    }

    func setPaused(_ on: Bool) {
        if !on {
            try? FileManager.default.removeItem(at: pauseURL)
            return
        }
        if isPaused { return }
        let hidden = url.appendingPathComponent(".pause-menubar-\(getpid())")
        let text = "{\"by\": \"menubar\", \"since\": \(Date().timeIntervalSince1970)}"
        guard (try? text.write(to: hidden, atomically: false, encoding: .utf8)) != nil else { return }
        chmod(hidden.path, 0o600)
        rename(hidden.path, pauseURL.path)
    }
}

let defaultShortLanes = 2  // run_queue.DEFAULT_SHORT_LANES

func age(_ seconds: Double) -> String {
    let total = max(0, Int(seconds))
    return total >= 3600 ? String(format: "%dh%02dm", total / 3600, total % 3600 / 60)
                         : String(format: "%dm%02ds", total / 60, total % 60)
}

func clip(_ text: String, _ limit: Int) -> String {
    text.count > limit ? String(text.prefix(limit - 1)) + "…" : text
}

// MARK: - the app

final class Controller: NSObject, NSApplicationDelegate, NSMenuDelegate {
    let queue: QueueFolder
    let idleMinutes: Double
    let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    let menu = NSMenu()
    var emptySince: Date? = Date()
    var timer: Timer?
    var openMenu = false
    var menuIsOpen = false
    var signature = ""
    var rows: [Int: (NSMenuItem, Job)] = [:]  // the run rows of the menu on screen, by ticket number

    init(queue: QueueFolder, idleMinutes: Double, openMenu: Bool) {
        self.queue = queue
        self.idleMinutes = idleMinutes
        self.openMenu = openMenu
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        menu.delegate = self
        item.menu = menu
        refresh()
        let timer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in self?.refresh() }
        RunLoop.main.add(timer, forMode: .common)  // also while the menu is open
        self.timer = timer
        if openMenu {  // a diagnostic: open the menu by itself (the screenshot of the thread)
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in self?.item.button?.performClick(nil) }
        }
    }

    func menuNeedsUpdate(_ menu: NSMenu) { refresh(force: true) }
    func menuWillOpen(_ menu: NSMenu) { menuIsOpen = true }
    func menuDidClose(_ menu: NSMenu) { menuIsOpen = false }

    @objc func togglePause() {
        queue.setPaused(!queue.isPaused)
        refresh()
    }

    @objc func setLanes(_ sender: NSMenuItem) {
        queue.change(["shortLanes": sender.tag < 0 ? nil : sender.tag])
        refresh(force: true)
    }

    @objc func setMaxLoad(_ sender: NSMenuItem) {
        queue.change(["maxLoad": (sender.representedObject as? NSNumber)?.doubleValue])
        refresh(force: true)
    }

    @objc func quit() { NSApp.terminate(nil) }
    @objc func nothing() {}

    func symbol(_ name: String) -> NSImage? {
        let image = NSImage(systemSymbolName: name, accessibilityDescription: "Arcanum launch queue")
        image?.isTemplate = true
        return image
    }

    func text(_ job: Job, position: Int?, now: Double) -> String {
        let lane = job.lane.map { "/" + $0 } ?? ""
        let label = job.label.isEmpty ? clip(job.command, 48) : clip(job.label, 48)
        var text = "#\(job.seq)  \(job.kind)\(lane)  \(label)"
        if let started = job.startedAt {
            text += "  ·  running \(age(now - started))"
        } else {
            text += "  ·  waiting \(age(now - job.enqueuedAt))" + (position.map { " (\($0 + 1))" } ?? "")
        }
        return text + "  ·  \(job.checkout)"
    }

    func row(_ job: Job, position: Int?, now: Double) -> NSMenuItem {
        let entry = NSMenuItem(title: text(job, position: position, now: now), action: #selector(nothing), keyEquivalent: "")
        entry.target = self  // enabled, so it keeps the normal text colour
        entry.toolTip = job.command + (job.childPid.map { "\npid \($0)" } ?? "")
        entry.image = symbol(job.startedAt != nil ? "play.fill" : "hourglass")
        return entry
    }

    /// "Godot runs at once": the number of short lanes (the heavy lane and an export come on top), as `--set-lanes` sets it.
    func lanesItem(_ current: Int?) -> NSMenuItem {
        let effective = current ?? defaultShortLanes
        let title = effective == 0 ? "Godot runs at once: 1" : "Godot runs at once: up to \(effective) short + 1 heavy"
        let item = NSMenuItem(title: title + (current == nil ? " (default)" : ""), action: nil, keyEquivalent: "")
        let sub = NSMenu()
        sub.addItem(header("Short Godot lanes (a heavy run and an export come on top)"))
        let choices: [(String, Int)] = [("1 Godot run at a time", 0), ("1 short + 1 heavy", 1), ("2 short + 1 heavy", 2),
                                        ("3 short + 1 heavy", 3), ("4 short + 1 heavy", 4)]
        for (label, value) in choices {
            let entry = NSMenuItem(title: label + (value == defaultShortLanes ? "  (default)" : ""), action: #selector(setLanes(_:)), keyEquivalent: "")
            entry.target = self
            entry.tag = value
            entry.state = (current ?? -1) == value ? .on : .off
            sub.addItem(entry)
        }
        let reset = NSMenuItem(title: "Default (\(defaultShortLanes) short + 1 heavy)", action: #selector(setLanes(_:)), keyEquivalent: "")
        reset.target = self
        reset.tag = -1
        reset.state = current == nil ? .on : .off
        sub.addItem(.separator())
        sub.addItem(reset)
        item.submenu = sub
        return item
    }

    /// "Load guard": a run that is next waits while another Godot run is going and the one-minute load is above X (`--set-max-load`).
    func loadItem(_ current: Double?) -> NSMenuItem {
        var averages = [Double](repeating: 0, count: 1)
        let load = getloadavg(&averages, 1) == 1 ? String(format: "%.1f", averages[0]) : "?"
        let item = NSMenuItem(title: "Load guard: " + (current.map { String(format: "%g", $0) } ?? "off"), action: nil, keyEquivalent: "")
        let sub = NSMenu()
        sub.addItem(header("A run waits while another Godot run is going and load > X (now \(load))"))
        var options: [Double?] = [nil, 8, 12, 16, 24, 32, 48]
        if let current = current, !options.contains(where: { $0 == current }) { options.append(current) }
        for option in options {
            let entry = NSMenuItem(title: option.map { String(format: "%g", $0) } ?? "Off", action: #selector(setMaxLoad(_:)), keyEquivalent: "")
            entry.target = self
            entry.representedObject = option.map { NSNumber(value: $0) }
            entry.state = option == current ? .on : .off
            sub.addItem(entry)
        }
        item.submenu = sub
        return item
    }

    func header(_ text: String) -> NSMenuItem {
        let entry = NSMenuItem(title: text, action: nil, keyEquivalent: "")
        entry.isEnabled = false
        return entry
    }

    func refresh(force: Bool = false) {
        let jobs = queue.jobs()
        let paused = queue.isPaused
        let lanes = queue.shortLanes
        let guardLimit = queue.maxLoad
        let now = Date().timeIntervalSince1970
        let running = jobs.filter { $0.startedAt != nil }
        let waiting = jobs.filter { $0.startedAt == nil }

        if let button = item.button {
            button.image = symbol(paused ? "pause.circle.fill" : (jobs.isEmpty ? "tray" : "tray.full.fill"))
            button.title = jobs.isEmpty ? "" : " \(running.count)·\(waiting.count)"
            button.toolTip = "Arcanum launch queue: \(running.count) running, \(waiting.count) waiting" + (paused ? ", PAUSED" : "")
        }

        if !menuIsOpen && !force {  // a closed menu is built when it is opened (menuNeedsUpdate), not every second
            if jobs.isEmpty && !paused && idleMinutes > 0 {
                if let since = emptySince, Date().timeIntervalSince(since) > idleMinutes * 60 { NSApp.terminate(nil) }
            } else {
                emptySince = Date()
            }
            return
        }
        // an open menu is only given new times while the same runs are in it: rebuilding it under the pointer flickers
        let positions = Dictionary(waiting.enumerated().map { ($1.seq, $0) }, uniquingKeysWith: { first, _ in first })
        let before = signature
        signature = jobs.map { "\($0.seq):\($0.startedAt != nil)" }.joined(separator: ",") + (paused ? "|paused" : "") + "|lanes=\(lanes.map(String.init) ?? "-")|load=\(guardLimit.map { String($0) } ?? "-")"
        if menuIsOpen && signature == before {
            for (seq, entry) in rows { entry.0.title = text(entry.1, position: positions[seq], now: now) }
            return
        }
        rows = [:]
        menu.removeAllItems()
        menu.addItem(header("Launch queue: \(running.count) running, \(waiting.count) waiting" + (paused ? "  —  PAUSED" : "")))
        menu.addItem(.separator())
        if jobs.isEmpty {
            menu.addItem(header("No runs"))
        }
        if !running.isEmpty {
            menu.addItem(header("Running"))
            for job in running { let entry = row(job, position: nil, now: now); rows[job.seq] = (entry, job); menu.addItem(entry) }
        }
        if !waiting.isEmpty {
            menu.addItem(header(paused ? "Waiting (paused: none starts)" : "Waiting, in order"))
            for (index, job) in waiting.enumerated() { let entry = row(job, position: index, now: now); rows[job.seq] = (entry, job); menu.addItem(entry) }
        }
        menu.addItem(.separator())
        let pause = NSMenuItem(title: "Pause (new runs do not start)", action: #selector(togglePause), keyEquivalent: "p")
        pause.target = self
        pause.state = paused ? .on : .off
        menu.addItem(pause)
        menu.addItem(.separator())
        menu.addItem(lanesItem(lanes))
        menu.addItem(loadItem(guardLimit))
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit (starts again with the next run)", action: #selector(quit), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        // leave by itself after a quiet while: nothing queued and not paused (the next run starts it again)
        if jobs.isEmpty && !paused && idleMinutes > 0 {
            if let since = emptySince, Date().timeIntervalSince(since) > idleMinutes * 60 { NSApp.terminate(nil) }
        } else {
            emptySince = Date()
        }
    }
}

// MARK: - start

func defaultQueueURL() -> URL {
    if let home = ProcessInfo.processInfo.environment["ARCANUM_LOCAL_HOME"], !home.isEmpty {
        return URL(fileURLWithPath: (home as NSString).expandingTildeInPath).appendingPathComponent("run-queue")
    }
    return FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/Arcanum Steamwoven/run-queue")
}

var queueURL = defaultQueueURL()
var lockURL: URL? = nil
var idleMinutes = 15.0
var openMenu = false
var arguments = Array(CommandLine.arguments.dropFirst())
while let flag = arguments.first {
    arguments.removeFirst()
    switch flag {
    case "--queue-dir": if let value = arguments.first { queueURL = URL(fileURLWithPath: value); arguments.removeFirst() }
    case "--lock": if let value = arguments.first { lockURL = URL(fileURLWithPath: value); arguments.removeFirst() }
    case "--idle-minutes": if let value = arguments.first, let minutes = Double(value) { idleMinutes = minutes; arguments.removeFirst() }
    case "--open-menu": openMenu = true
    case "--set-lanes", "--set-max-load":  // the same writes as the menu's submenus, for the self-check: N|default, X|off
        let value = arguments.isEmpty ? "" : arguments.removeFirst()
        if flag == "--set-lanes" {
            QueueFolder(url: queueURL).change(["shortLanes": Int(value)])
        } else {
            QueueFolder(url: queueURL).change(["maxLoad": Double(value)])
        }
        exit(0)
    case "--set-pause", "--clear-pause":  // what the menu's Pause item does, for scripts and the self-check: act and exit
        QueueFolder(url: queueURL).setPaused(flag == "--set-pause")
        exit(0)
    default:
        FileHandle.standardError.write("usage: ArcanumQueue [--queue-dir DIR] [--lock FILE] [--idle-minutes N (0: never)] [--open-menu] [--set-pause|--clear-pause] [--set-lanes N|default] [--set-max-load X|off]\n".data(using: .utf8)!)
        exit(2)
    }
}

// one app per lock file: the lock is held for the life of the process, so the kernel frees it when the app dies
let lockPath = (lockURL ?? queueURL.deletingLastPathComponent().appendingPathComponent("menubar/app.lock")).path
try? FileManager.default.createDirectory(atPath: (lockPath as NSString).deletingLastPathComponent,
                                         withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
let lockFd = open(lockPath, O_RDWR | O_CREAT | O_CLOEXEC | O_NOFOLLOW, 0o600)
var locked = false
for _ in 0..<20 where lockFd >= 0 && !locked {  // a probe of the starter holds the lock for microseconds: try for a second
    locked = flock(lockFd, LOCK_EX | LOCK_NB) == 0
    if !locked { usleep(50_000) }
}
if !locked {
    exit(0)  // already running
}

let application = NSApplication.shared
application.setActivationPolicy(.accessory)  // no Dock icon, no menu bar of its own
let controller = Controller(queue: QueueFolder(url: queueURL), idleMinutes: idleMinutes, openMenu: openMenu)
application.delegate = controller
application.run()
