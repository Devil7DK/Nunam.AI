#!/bin/sh
# Runs before nginx starts (from /docker-entrypoint.d). Uses the certificate and key at
# NUNAM_TLS_CERT and NUNAM_TLS_KEY; when neither exists, creates a self-signed pair there, kept in the
# mounted folder so its fingerprint, which indexers pin, survives restarts and image updates.
set -eu

cert=$NUNAM_TLS_CERT
key=$NUNAM_TLS_KEY
info=/etc/nunam/run/certificate.json
mkdir -p "$(dirname "$info")"

if [ ! -e "$cert" ] && [ ! -e "$key" ]; then
    san=DNS:localhost,IP:127.0.0.1
    for name in $(echo "${NUNAM_TLS_NAMES:-}" | tr ',' ' '); do
        case $name in
            *[!0-9.]*) case $name in *:*) san="$san,IP:$name" ;; *) san="$san,DNS:$name" ;; esac ;;
            *) san="$san,IP:$name" ;;
        esac
    done
    mkdir -p "$(dirname "$cert")" "$(dirname "$key")"
    # Ten years: indexers pin this exact certificate rather than check its dates, and a new one
    # means reconnecting every machine.
    (umask 077 && openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -days 3650 \
        -subj /CN=Nunam -addext "subjectAltName=$san" -keyout "$key" -out "$cert" 2>/dev/null) || {
        echo "nunam: could not write a certificate to $cert; mount a writable folder there or supply your own." >&2
        exit 1
    }
    chmod 644 "$cert"
    echo "nunam: created a self-signed certificate for $san"
elif [ ! -e "$cert" ] || [ ! -e "$key" ]; then
    echo "nunam: found only one of $cert and $key; supply both or neither." >&2
    exit 1
fi

openssl x509 -in "$cert" -noout >/dev/null 2>&1 || { echo "nunam: $cert is not a PEM certificate." >&2; exit 1; }

sha256=$(openssl x509 -in "$cert" -outform der | openssl dgst -sha256 -hex | sed 's/.*= *//' | tr a-f A-F)
spki=$(openssl x509 -in "$cert" -pubkey -noout | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | base64)

# The Add machine command pins the certificate when machines can't verify it on their own: when it
# isn't issued by a public authority. A private CA's certificate gets pinned too; pinning one that
# renews means reconnecting machines, so NUNAM_TLS_PIN=no turns it off.
case ${NUNAM_TLS_PIN:-auto} in
    yes) pin=true ;;
    no) pin=false ;;
    *) if openssl verify -CAfile /etc/ssl/certs/ca-certificates.crt -untrusted "$cert" "$cert" >/dev/null 2>&1
       then pin=false; else pin=true; fi ;;
esac

printf '{"sha256":"%s","publicKeySha256":"%s","pin":%s}\n' "$sha256" "$spki" "$pin" > "$info"
echo "nunam: HTTPS certificate SHA-256 $(echo "$sha256" | sed 's/../&:/g; s/:$//') (pinned by new machines: $pin)"
