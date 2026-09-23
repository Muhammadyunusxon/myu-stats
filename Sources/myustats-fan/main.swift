import Foundation
import SMCKit

// Privileged fan helper. MYU STATS runs it through an administrator prompt for each change;
// it does one thing and exits. Usage:
//   myustats-fan auto   <fan|all>
//   myustats-fan manual <fan|all> <rpm>
//   myustats-fan max    <fan|all>

func fail(_ message: String, code: Int32) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(code)
}

guard getuid() == 0 else { fail("myustats-fan must run as root.", code: 77) }
guard let command = FanCommand.parse(Array(CommandLine.arguments.dropFirst())) else {
    fail("usage: myustats-fan auto|max <fan|all> | manual <fan|all> <rpm>", code: 64)
}
guard let control = FanControl() else { fail("Could not open the SMC.", code: 69) }

do {
    try control.apply(command)
} catch {
    fail("\(error)", code: 1)
}
