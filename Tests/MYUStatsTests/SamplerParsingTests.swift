import Darwin
import Foundation
import Testing
@testable import MYUStats

@Suite("Network counters")
struct NetworkParsingTests {
    private func message(type: Int32, index: UInt16, received: UInt64, sent: UInt64) -> [UInt8] {
        var message = if_msghdr2()
        message.ifm_msglen = UInt16(MemoryLayout<if_msghdr2>.size)
        message.ifm_type = UInt8(type)
        message.ifm_index = index
        message.ifm_data.ifi_ibytes = received
        message.ifm_data.ifi_obytes = sent
        return withUnsafeBytes(of: message) { Array($0) }
    }

    @Test("only interface-info messages from physical interfaces are counted")
    func sumsPhysicalInterfaces() {
        let buffer = message(type: RTM_IFINFO2, index: 1, received: 1_000, sent: 400)
            + message(type: RTM_IFINFO2, index: 2, received: 9_000, sent: 9_000) // a VPN tunnel
            + message(type: RTM_NEWADDR, index: 1, received: 5_000, sent: 5_000) // not a counter message
            + message(type: RTM_IFINFO2, index: 3, received: 20, sent: 10)
        let totals = NetworkSampler.totals(fromRouteMessages: buffer) { $0 != 2 }
        #expect(totals == NetworkTotals(received: 1_020, sent: 410))
    }

    @Test("a truncated message ends the walk instead of reading past the buffer")
    func stopsAtTruncatedMessage() {
        let whole = message(type: RTM_IFINFO2, index: 1, received: 1_000, sent: 400)
        let cut = Array(message(type: RTM_IFINFO2, index: 1, received: 7, sent: 7).prefix(40))
        let totals = NetworkSampler.totals(fromRouteMessages: whole + cut) { _ in true }
        #expect(totals == NetworkTotals(received: 1_000, sent: 400))
    }
}

@Suite("Display choice")
struct DisplayChoiceTests {
    @Test func emptyMeansMainDisplay() {
        #expect(DisplayChoice.index(of: "", in: ["A", "B"]) == 0)
    }

    @Test func savedDisplayIsFound() {
        #expect(DisplayChoice.index(of: "B", in: ["A", "B"]) == 1)
    }

    @Test("a disconnected display falls back to the main one")
    func missingFallsBack() {
        #expect(DisplayChoice.index(of: "C", in: ["A", nil]) == 0)
    }

    @Test func noDisplays() {
        #expect(DisplayChoice.index(of: "A", in: []) == nil)
    }
}
