import SwiftUI

struct NetworkDetail: View {
    var store: StatsStore

    var body: some View {
        Section("Now") {
            LabeledContent("Download", value: Format.rate(store.network.downBytesPerSecond))
            LabeledContent("Upload", value: Format.rate(store.network.upBytesPerSecond))
            LabeledContent("Peak", value: Format.rate(store.networkPeak))
            HistoryRow(values: store.networkHistory, color: Level.accent, ceiling: nil,
                       duration: store.historyDuration)
        }
        Section("Connection") {
            LabeledContent("Interface", value: store.networkInterface?.name ?? "Offline")
            LabeledContent("IPv4 address", value: store.networkInterface?.address ?? "–")
        }
        Section {
            LabeledContent("Received", value: Format.bytes(store.networkTotals.received))
            LabeledContent("Sent", value: Format.bytes(store.networkTotals.sent))
        } header: {
            Text("Since boot")
        } footer: {
            Text("Wi-Fi, Ethernet and cellular only; VPN tunnels are left out so traffic is not counted twice.")
        }
    }
}
