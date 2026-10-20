APP_NAME := MYU STATS
APP := build/$(APP_NAME).app
INSTALLED := /Applications/$(APP_NAME).app

.PHONY: build app dmg icon strings install run test clean windows-test windows-check

build:
	swift build -c release

app:
	./Scripts/bundle.sh

# build/MYU-STATS.dmg: the app plus an Applications link, for a GitHub release.
dmg:
	./Scripts/make-dmg.sh

# Regenerates Resources/AppIcon.icns, and the Windows app's icon, from Scripts/make-icon.swift.
icon:
	rm -rf build/AppIcon.iconset
	swift Scripts/make-icon.swift build/AppIcon.iconset --ico Windows/MYUStats/Assets/AppIcon.ico
	iconutil -c icns build/AppIcon.iconset -o Resources/AppIcon.icns

# Refreshes Resources/Localizable.xcstrings from the strings the compiler finds in the source.
# New strings are added, removed ones are marked stale; translations are kept.
strings:
	rm -rf build/stringsdata build/strings-build
	swift build --scratch-path build/strings-build --target MYUStats \
		-Xswiftc -emit-localized-strings -Xswiftc -emit-localized-strings-path -Xswiftc "$(CURDIR)/build/stringsdata"
	xcrun xcstringstool sync Resources/Localizable.xcstrings --stringsdata build/stringsdata/*.stringsdata

install: app
	-pkill -x MYUStats
	rm -rf "$(INSTALLED)"
	ditto "$(APP)" "$(INSTALLED)"
	open "$(INSTALLED)"

run: app
	-pkill -x MYUStats
	open "$(APP)"

test:
	swift test

# The Windows app's core library tests, which run anywhere .NET 10 does.
windows-test:
	cd Windows && dotnet test --project MYUStats.Tests

# Type-checks the WinUI app on macOS; building it needs Windows (Windows/build.ps1).
windows-check:
	cd Windows && dotnet build MacCheck

clean:
	rm -rf .build build
