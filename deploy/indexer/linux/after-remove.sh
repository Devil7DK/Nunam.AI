#!/bin/sh
# After removal. $1: "remove"/"purge"/… (deb) or the number of versions left (rpm).
set -e

if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
# dpkg purges the settings file it installed; the certificate was added by hand.
if [ "$1" = "purge" ]; then
    rm -rf /etc/nunam-indexer
fi
