import Foundation
import Testing

@Suite("String catalog")
struct LocalizationTests {
    private static let catalog = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        .appendingPathComponent("Resources/Localizable.xcstrings")

    private static func strings() throws -> [String: [String: Any]] {
        let data = try Data(contentsOf: catalog)
        let json = try #require(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        return try #require(json["strings"] as? [String: [String: Any]])
    }

    @Test("every string has an Uzbek translation")
    func uzbekIsComplete() throws {
        let untranslated = try Self.strings().filter { _, entry in
            let uz = (entry["localizations"] as? [String: Any])?["uz"] as? [String: Any]
            let unit = uz?["stringUnit"] as? [String: Any]
            return unit?["state"] as? String != "translated"
        }
        #expect(untranslated.isEmpty, "Run `make strings`, then translate: \(untranslated.keys.sorted())")
    }

    @Test("translations keep every format specifier of the English string")
    func specifiersMatch() throws {
        let specifier = try Regex(#"%(?:\d+\$)?(?:lld|@|d)"#)
        let position = try Regex(#"\d+\$"#)
        func kinds(_ text: String) -> [String] {
            text.matches(of: specifier).map { String(text[$0.range]).replacing(position, with: "") }.sorted()
        }
        for (key, entry) in try Self.strings() {
            let uz = (entry["localizations"] as? [String: Any])?["uz"] as? [String: Any]
            guard let value = (uz?["stringUnit"] as? [String: Any])?["value"] as? String else { continue }
            #expect(kinds(key) == kinds(value), "\(key)")
        }
    }
}
