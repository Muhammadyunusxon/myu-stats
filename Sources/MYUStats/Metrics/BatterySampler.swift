import Foundation
import IOKit
import IOKit.ps

/// Internal battery state; nil on Macs without a battery.
enum BatterySampler {
    static func sample() -> BatteryState? {
        guard let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let sources = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef]
        else { return nil }

        for source in sources {
            guard let description = IOPSGetPowerSourceDescription(info, source)?
                    .takeUnretainedValue() as? [String: Any],
                  description[kIOPSTypeKey] as? String == kIOPSInternalBatteryType,
                  let current = description[kIOPSCurrentCapacityKey] as? Int,
                  let max = description[kIOPSMaxCapacityKey] as? Int, max > 0
            else { continue }

            let isCharging = description[kIOPSIsChargingKey] as? Bool ?? false
            let remainingKey = isCharging ? kIOPSTimeToFullChargeKey : kIOPSTimeToEmptyKey
            let minutes = description[remainingKey] as? Int
            let registry = SmartBattery.read()

            return BatteryState(
                percent: current * 100 / max,
                isCharging: isCharging,
                isOnAC: description[kIOPSPowerSourceStateKey] as? String == kIOPSACPowerValue,
                minutesRemaining: (minutes ?? -1) > 0 ? minutes : nil,
                cycleCount: registry.cycleCount,
                healthPercent: registry.healthPercent,
                temperatureCelsius: registry.temperature,
                adapterWatts: adapterWatts()
            )
        }
        return nil
    }

    private static func adapterWatts() -> Int? {
        guard let details = IOPSCopyExternalPowerAdapterDetails()?.takeRetainedValue() as? [String: Any]
        else { return nil }
        return details[kIOPSPowerAdapterWattsKey] as? Int
    }
}

/// Health data the power-source API does not expose, read from the AppleSmartBattery registry entry.
private enum SmartBattery {
    struct Reading {
        var cycleCount: Int?
        var healthPercent: Int?
        var temperature: Double?
    }

    static func read() -> Reading {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSmartBattery"))
        guard service != 0 else { return Reading() }
        defer { IOObjectRelease(service) }

        func value(_ key: String) -> Int? {
            IORegistryEntryCreateCFProperty(service, key as CFString, kCFAllocatorDefault, 0)?
                .takeRetainedValue() as? Int
        }
        // Newer macOS nests capacities in BatteryData; older releases keep them top-level.
        let data = IORegistryEntryCreateCFProperty(service, "BatteryData" as CFString, kCFAllocatorDefault, 0)?
            .takeRetainedValue() as? [String: Any]
        func capacity(_ key: String) -> Int? { data?[key] as? Int ?? value(key) }

        // "Maximum Capacity" in System Settings is nominal full charge over design capacity.
        let nominal = capacity("NominalChargeCapacity") ?? value("AppleRawMaxCapacity")
        let design = capacity("DesignCapacity")
        var health: Int?
        if let nominal, let design, design > 0 {
            health = min(100, Int((Double(nominal) / Double(design) * 100).rounded()))
        }

        return Reading(
            cycleCount: value("CycleCount"),
            healthPercent: health,
            temperature: value("Temperature").map { Double($0) / 100 }
        )
    }
}
