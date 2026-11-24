#!/bin/sh
# After removal. $1: "remove"/"purge"/… (deb) or the number of versions left (rpm).
set -e

if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
if [ "$1" = 0 ] || [ "$1" = remove ] || [ "$1" = purge ]; then
    if command -v semanage >/dev/null 2>&1 && semanage fcontext -l -C 2>/dev/null | grep -qF /usr/lib/nunam-indexer/; then
        semanage fcontext -d '/usr/lib/nunam-indexer/Nunam\.Indexer' || true
    fi
fi
# dpkg purges the settings file it installed; the certificate was added by hand.
if [ "$1" = "purge" ]; then
    rm -rf /etc/nunam-indexer
    rm -rf /etc/systemd/system/nunam-indexer.service.d
fi
