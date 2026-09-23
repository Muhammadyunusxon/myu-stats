APP_NAME := MYU STATS
APP := build/$(APP_NAME).app
INSTALLED := /Applications/$(APP_NAME).app

.PHONY: build app install run test clean

build:
	swift build -c release

app:
	./Scripts/bundle.sh

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
