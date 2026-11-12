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
    /// The pause is a ticket no wrapper can read, held by a `lockf` process (run_queue.py explains why: every wrapper, old ones too,
    /// counts a held unreadable ticket as a running export and waits behind it).
    var pauseURL: URL { url.appendingPathComponent("t-pause.json") }
    var isPaused: Bool { lockedByOthers(pauseURL.path) }

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

    // read as strictly as the queue reads them (run_queue._whole_lanes, max_load): a whole number 0...8, a finite number above 0
    var shortLanes: Int? {
        guard let number = settings()["shortLanes"] as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID(),
              !CFNumberIsFloatType(number), 0...8 ~= number.intValue else { return nil }
        return number.intValue
    }
    var maxLoad: Double? {
        guard let number = settings()["maxLoad"] as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID(),
              number.doubleValue > 0, number.doubleValue.isFinite else { return nil }
        return number.doubleValue
    }

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

    @discardableResult
    func setPaused(_ on: Bool) -> Bool {
        if !on {
            try? FileManager.default.removeItem(at: pauseURL)  // the holder sees the file gone and ends within a second
            return true
        }
        if isPaused { return true }
        let hidden = url.appendingPathComponent(".pause-menubar-\(getpid())")
        let text = "{\"schema\": \"arcanum-run-queue-pause-v1\", \"by\": \"menubar\", \"since\": \(Date().timeIntervalSince1970)}"
        guard (try? text.write(to: hidden, atomically: false, encoding: .utf8)) != nil else { return false }
        chmod(hidden.path, 0o600)
        // its own session, so closing a terminal or killing this process group does not end the pause
        let script = "while [ -e \"$0\" ] || [ -e \"$1\" ]; do sleep 1; done"
        guard let pid = spawnDetached("/usr/bin/lockf", ["lockf", "-k", hidden.path, "/bin/sh", "-c", script, hidden.path, pauseURL.path]) else {
            try? FileManager.default.removeItem(at: hidden)
            return false
        }
        for _ in 0..<100 where !lockedByOthers(hidden.path) { usleep(50_000) }  // up to 5 s for the holder to take the lock
        guard lockedByOthers(hidden.path) else {
            kill(pid, SIGTERM)
            try? FileManager.default.removeItem(at: hidden)
            return false
        }
        rename(hidden.path, pauseURL.path)
        return true
    }
}

/// Start a process in a new session with no stdio (posix_spawn with POSIX_SPAWN_SETSID); its pid, or nil.
func spawnDetached(_ path: String, _ arguments: [String]) -> pid_t? {
    var attributes: posix_spawnattr_t? = nil
    var actions: posix_spawn_file_actions_t? = nil
    posix_spawnattr_init(&attributes)
    posix_spawn_file_actions_init(&actions)
    defer {
        posix_spawnattr_destroy(&attributes)
        posix_spawn_file_actions_destroy(&actions)
    }
    posix_spawnattr_setflags(&attributes, Int16(0x0400))  // POSIX_SPAWN_SETSID
    for descriptor in [0, 1, 2] as [Int32] {
        posix_spawn_file_actions_addopen(&actions, descriptor, "/dev/null", descriptor == 0 ? O_RDONLY : O_WRONLY, 0)
    }
    var argv: [UnsafeMutablePointer<CChar>?] = arguments.map { strdup($0) } + [nil]
    defer { for pointer in argv { free(pointer) } }
    var pid: pid_t = 0
    let status = posix_spawn(&pid, path, &actions, &attributes, &argv, environ)
    return status == 0 ? pid : nil
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

// MARK: - what the runs cost (ARC-DEV-1344: CPU and RAM of each running run's process tree)

/// One `ps` snapshot per sample: a run's tree is the processes of its command's process group (the queue starts every command
/// in a new session, so the group is the command's pid) plus their descendants. CPU is the change of the tree's accumulated CPU
/// time between two samples, in percent of one core (so 240 is 2.4 cores busy); the first sample of a run has no CPU yet.
final class Sampler {
    struct Usage { var cpu: Double?; var rssKB: Int }
    private let lock = NSLock()
    private var latest: [Int32: Usage] = [:]
    private var last: [Int32: (seconds: Double, at: Date)] = [:]
    private var busy = false

    func usage(of pid: Int32) -> Usage? {
        lock.lock(); defer { lock.unlock() }
        return latest[pid]
    }

    func sample(roots: [Int32]) {
        lock.lock()
        if busy { lock.unlock(); return }
        busy = true
        lock.unlock()
        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let self = self else { return }
            let snapshot = Sampler.snapshot()
            let now = Date()
            var result: [Int32: Usage] = [:]
            var seconds: [Int32: (seconds: Double, at: Date)] = [:]
            self.lock.lock()
            let before = self.last
            self.lock.unlock()
            for root in roots {
                let members = Sampler.tree(root: root, snapshot: snapshot)
                if members.isEmpty { continue }
                let total = members.reduce(0.0) { $0 + $1.cpuSeconds }
                let rss = members.reduce(0) { $0 + $1.rssKB }
                var cpu: Double? = nil
                if let old = before[root], now.timeIntervalSince(old.at) > 0.5, now.timeIntervalSince(old.at) < 20, total >= old.seconds {
                    cpu = (total - old.seconds) / now.timeIntervalSince(old.at) * 100
                }
                result[root] = Usage(cpu: cpu, rssKB: rss)
                seconds[root] = (total, now)
            }
            self.lock.lock()
            self.latest = result
            self.last = seconds
            self.busy = false
            self.lock.unlock()
        }
    }

    struct Row { let pid: Int32; let ppid: Int32; let pgid: Int32; let rssKB: Int; let cpuSeconds: Double }

    static func snapshot() -> [Row] {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/ps")
        process.arguments = ["-axo", "pid=,ppid=,pgid=,rss=,time="]
        process.environment = ["LC_ALL": "C"]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        guard (try? process.run()) != nil else { return [] }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        var rows: [Row] = []
        for line in String(decoding: data, as: UTF8.self).split(separator: "\n") {
            let parts = line.split(separator: " ", omittingEmptySubsequences: true)
            guard parts.count >= 5, let pid = Int32(parts[0]), let ppid = Int32(parts[1]), let pgid = Int32(parts[2]),
                  let rss = Int(parts[3]) else { continue }
            rows.append(Row(pid: pid, ppid: ppid, pgid: pgid, rssKB: rss, cpuSeconds: cpuSeconds(String(parts[4]))))
        }
        return rows
    }

    /// ps time: [[dd-]hh:]mm:ss[.xx]
    static func cpuSeconds(_ text: String) -> Double {
        var rest = Substring(text)
        var days = 0.0
        if let dash = rest.firstIndex(of: "-") {
            days = Double(rest[rest.startIndex..<dash]) ?? 0
            rest = rest[rest.index(after: dash)...]
        }
        var total = 0.0
        for part in rest.split(separator: ":") { total = total * 60 + (Double(part) ?? 0) }
        return days * 86400 + total
    }

    static func tree(root: Int32, snapshot: [Row]) -> [Row] {
        var inTree = Set<Int32>(snapshot.filter { $0.pgid == root || $0.pid == root }.map { $0.pid })
        var grew = true
        while grew {  // descendants that left the group (a build server that started its own)
            grew = false
            for row in snapshot where !inTree.contains(row.pid) && inTree.contains(row.ppid) {
                inTree.insert(row.pid)
                grew = true
            }
        }
        return snapshot.filter { inTree.contains($0.pid) }
    }
}

func memory(_ kilobytes: Int) -> String {
    kilobytes >= 1_000_000 ? String(format: "%.1f GB", Double(kilobytes) / 1_048_576) : String(format: "%d MB", kilobytes / 1024)
}

/// The whole Mac: the one-minute load average and the RAM in use (active, wired and compressed pages) of the installed RAM.
func machineLine() -> String {
    var averages = [Double](repeating: 0, count: 1)
    let load = getloadavg(&averages, 1) == 1 ? String(format: "%.1f", averages[0]) : "?"
    var stats = vm_statistics64()
    var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size / MemoryLayout<integer_t>.size)
    let status = withUnsafeMutablePointer(to: &stats) {
        $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &count) }
    }
    var ram = ""
    if status == KERN_SUCCESS {
        let used = (UInt64(stats.active_count) + UInt64(stats.wire_count) + UInt64(stats.compressor_page_count)) * UInt64(vm_kernel_page_size)
        ram = String(format: "  ·  RAM %.1f of %.0f GB", Double(used) / 1_073_741_824, Double(ProcessInfo.processInfo.physicalMemory) / 1_073_741_824)
    }
    return "Mac: load \(load)  ·  \(ProcessInfo.processInfo.activeProcessorCount) cores" + ram
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
    let sampler = Sampler()
    var sampledAt = Date.distantPast
    var machineItem: NSMenuItem?
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
            if let pid = job.childPid, let usage = sampler.usage(of: pid) {
                text += "  ·  " + (usage.cpu.map { String(format: "cpu %.0f%%  ", $0) } ?? "") + memory(usage.rssKB)
            }
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
        var shown = choices
        if let current = current, !choices.contains(where: { $0.1 == current }) { shown.append(("\(current) short + 1 heavy", current)) }
        for (label, value) in shown {
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
        if (menuIsOpen || force) && !running.isEmpty && Date().timeIntervalSince(sampledAt) >= 2 {
            sampledAt = Date()
            sampler.sample(roots: running.compactMap { $0.childPid })
        }

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
            machineItem?.title = machineLine()
            return
        }
        rows = [:]
        menu.removeAllItems()
        menu.addItem(header("Launch queue: \(running.count) running, \(waiting.count) waiting" + (paused ? "  —  PAUSED" : "")))
        let machine = header(machineLine())
        machineItem = machine
        menu.addItem(machine)
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
            guard value == "default" || Int(value).map({ 0...8 ~= $0 }) == true else {
                FileHandle.standardError.write("--set-lanes takes default or a whole number from 0 to 8\n".data(using: .utf8)!)
                exit(2)
            }
            QueueFolder(url: queueURL).change(["shortLanes": Int(value)])
        } else {
            guard value == "off" || Double(value).map({ $0 > 0 && $0.isFinite }) == true else {
                FileHandle.standardError.write("--set-max-load takes off or a positive number\n".data(using: .utf8)!)
                exit(2)
            }
            QueueFolder(url: queueURL).change(["maxLoad": Double(value)])
        }
        exit(0)
    case "--set-pause", "--clear-pause":  // what the menu's Pause item does, for scripts and the self-check: act and exit
        exit(QueueFolder(url: queueURL).setPaused(flag == "--set-pause") ? 0 : 1)
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
