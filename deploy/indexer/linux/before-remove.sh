#!/bin/sh
# Before removal or upgrade. $1: "remove"/"upgrade"/… (deb) or the number of versions left (rpm).
set -e

case "$1" in
    remove|0)
        if [ -d /run/systemd/system ]; then
            systemctl disable --now nunam-indexer || true
        fi
        ;;
esac
