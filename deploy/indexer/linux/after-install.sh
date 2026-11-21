#!/bin/sh
# After install or upgrade. $1: "configure" (deb) or the number of installed versions (rpm).
set -e

systemd-sysusers /usr/lib/sysusers.d/nunam-indexer.conf
# The settings hold the database password and license key: readable by the service, not by everyone.
chgrp nunam /etc/nunam-indexer /etc/nunam-indexer/appsettings.Local.json
chmod 0750 /etc/nunam-indexer
chmod 0640 /etc/nunam-indexer/appsettings.Local.json

if [ -d /run/systemd/system ]; then
    systemctl daemon-reload
    if systemctl is-enabled --quiet nunam-indexer; then
        systemctl try-restart nunam-indexer
        exit 0
    fi
fi

cat <<'MSG'
Nunam indexer installed. To start it:
  1. Fill in /etc/nunam-indexer/appsettings.Local.json and save the database
     certificate as /etc/nunam-indexer/nunam-db.crt.
  2. Let the nunam user read each watch path, e.g. for ~/Documents:
       sudo setfacl -m u:nunam:x ~
       sudo setfacl -R -m u:nunam:rX -m d:u:nunam:rX ~/Documents
  3. sudo systemctl enable --now nunam-indexer
MSG
