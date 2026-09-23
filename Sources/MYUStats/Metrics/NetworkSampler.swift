import Darwin
import Foundation

/// Throughput across physical interfaces, read from 64-bit counters (NET_RT_IFLIST2)
/// so multi-gigabyte transfers never wrap.
final class NetworkSampler {
    private var previous: (totals: NetworkTotals, time: TimeInterval)?
    private(set) var totals = NetworkTotals(received: 0, sent: 0)

    func sample() -> NetworkRate {
        guard let current = Self.readTotals() else { return .zero }
        totals = current
        let now = ProcessInfo.processInfo.systemUptime
        defer { previous = (current, now) }
        guard let prev = previous, now > prev.time else { return .zero }

        let elapsed = now - prev.time
        let down = current.received >= prev.totals.received ? Double(current.received - prev.totals.received) : 0
        let up = current.sent >= prev.totals.sent ? Double(current.sent - prev.totals.sent) : 0
        return NetworkRate(downBytesPerSecond: down / elapsed, upBytesPerSecond: up / elapsed)
    }

    /// First active physical interface with an IPv4 address, e.g. en0 · 192.168.1.5.
    static func primaryInterface() -> NetworkInterface? {
        var head: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&head) == 0, let first = head else { return nil }
        defer { freeifaddrs(head) }

        for pointer in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let entry = pointer.pointee
            let flags = Int32(entry.ifa_flags)
            guard let address = entry.ifa_addr,
                  address.pointee.sa_family == UInt8(AF_INET),
                  flags & IFF_UP != 0, flags & IFF_RUNNING != 0, flags & IFF_LOOPBACK == 0
            else { continue }
            let name = String(cString: entry.ifa_name)
            guard isPhysical(name) else { continue }

            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            guard getnameinfo(address, socklen_t(address.pointee.sa_len), &host, socklen_t(host.count),
                              nil, 0, NI_NUMERICHOST) == 0 else { continue }
            return NetworkInterface(name: name, address: String(cString: host))
        }
        return nil
    }

    private static func readTotals() -> NetworkTotals? {
        var mib: [Int32] = [CTL_NET, PF_ROUTE, 0, 0, NET_RT_IFLIST2, 0]
        var length = 0
        guard sysctl(&mib, UInt32(mib.count), nil, &length, nil, 0) == 0, length > 0 else { return nil }
        var buffer = [UInt8](repeating: 0, count: length)
        guard sysctl(&mib, UInt32(mib.count), &buffer, &length, nil, 0) == 0 else { return nil }

        var result = NetworkTotals(received: 0, sent: 0)
        buffer.withUnsafeBytes { raw in
            var offset = 0
            while offset + MemoryLayout<if_msghdr>.size <= length {
                let header = raw.loadUnaligned(fromByteOffset: offset, as: if_msghdr.self)
                guard header.ifm_msglen > 0 else { break }
                if Int32(header.ifm_type) == RTM_IFINFO2 {
                    let message = raw.loadUnaligned(fromByteOffset: offset, as: if_msghdr2.self)
                    if isPhysical(index: message.ifm_index) {
                        result.received += message.ifm_data.ifi_ibytes
                        result.sent += message.ifm_data.ifi_obytes
                    }
                }
                offset += Int(header.ifm_msglen)
            }
        }
        return result
    }

    private static func isPhysical(index: UInt16) -> Bool {
        var name = [CChar](repeating: 0, count: Int(IF_NAMESIZE))
        guard if_indextoname(UInt32(index), &name) != nil else { return false }
        return isPhysical(String(cString: name))
    }

    // VPN tunnels (utun) and AWDL would double-count traffic already seen on en*.
    private static func isPhysical(_ name: String) -> Bool {
        name.hasPrefix("en") || name.hasPrefix("pdp_ip")
    }
}
