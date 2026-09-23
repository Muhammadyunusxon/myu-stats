import Foundation
import SMCKit

// Privileged fan helper. MYU STATS installs it once as a root-owned setuid binary in
// /Library/PrivilegedHelperTools; it does one thing and exits. Usage:
//   myustats-fan auto   <fan|all>
//   myustats-fan manual <fan|all> <rpm>
//   myustats-fan max    <fan|all>

func fail(_ message: String, code: Int32) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(code)
}

// Setuid root: the effective user is root, the real one is whoever ran it. Become root fully, since
// the SMC checks the caller's credentials, and drop the caller's environment before doing anything.
guard geteuid() == 0, setgid(0) == 0, setuid(0) == 0 else { fail("myustats-fan must run as root.", code: 77) }
for key in ProcessInfo.processInfo.environment.keys { unsetenv(key) }
guard let command = FanCommand.parse(Array(CommandLine.arguments.dropFirst())) else {
    fail("usage: myustats-fan auto|max <fan|all> | manual <fan|all> <rpm>", code: 64)
}
guard let control = FanControl() else { fail("Could not open the SMC.", code: 69) }

do {
    try control.apply(command)
} catch {
    fail("\(error)", code: 1)
}
