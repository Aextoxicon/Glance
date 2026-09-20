#!/usr/bin/env bash
set -euo pipefail

APP_NAME="Glance"
VERSION=1

print_config() {
    local name="$1"
    echo "Config{${name} v${VERSION}}"
}

main() {
    print_config "$APP_NAME"

    for arg in "$@"; do
        echo "arg: $arg"
    done

    if [[ "$VERSION" -gt 0 ]]; then
        echo "ready"
    fi
}

main "$@"
