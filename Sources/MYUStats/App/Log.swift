import os

/// Unified-log categories. Read them with
/// `log stream --predicate 'subsystem == "com.muhammadyunusxon.myustats"'`.
enum Log {
    private static let subsystem = "com.muhammadyunusxon.myustats"

    static let app = Logger(subsystem: subsystem, category: "app")
    static let sampling = Logger(subsystem: subsystem, category: "sampling")
    static let fans = Logger(subsystem: subsystem, category: "fans")
    static let storage = Logger(subsystem: subsystem, category: "storage")
    static let settings = Logger(subsystem: subsystem, category: "settings")
}
