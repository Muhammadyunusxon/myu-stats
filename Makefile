APP_NAME := MYU STATS
APP := build/$(APP_NAME).app
INSTALLED := /Applications/$(APP_NAME).app

.PHONY: build app dmg icon strings install run test clean

build:
	swift build -c release

app:
	./Scripts/bundle.sh

# build/MYU-STATS.dmg: the app plus an Applications link, for a GitHub release.
dmg:
	./Scripts/make-dmg.sh

# Regenerates Resources/AppIcon.icns from Scripts/make-icon.swift.
icon:
	rm -rf build/AppIcon.iconset
	swift Scripts/make-icon.swift build/AppIcon.iconset
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

clean:
	rm -rf .build build
