#!/bin/sh
# Installs the Nunam indexer on this machine and connects it to the server it came from.
# The web UI's Machines > Add machine shows the command to run, with a one-time code:
#   curl -fsSL <server>/api/agent/install.sh | sudo sh -s -- --server <server> --code <code>
# Behind a certificate the machine can't verify, the command pins it: curl gets --pinnedpubkey with
# -k, and this script the same key plus the certificate's SHA-256 for the indexer.
set -eu

# Piped into sh, stdin is the rest of this script: nothing runs until it has all been read, and the
# programs it starts read /dev/null or the terminal instead.
main() {
    server='' code='' name='' remote='' certificate='' public_key=''
    while [ $# -gt 0 ]; do
        case $1 in
            --server) server=${2%/}; shift 2 ;;
            --code) code=$2; shift 2 ;;
            --name) name=$2; shift 2 ;;
            --remote-management) remote=1; shift ;;
            --certificate-sha256) certificate=$2; shift 2 ;;
            --public-key-sha256) public_key=$2; shift 2 ;;
            *) echo "Unknown option: $1" >&2; exit 2 ;;
        esac
    done
    [ -n "$server" ] && [ -n "$code" ] || { echo "Usage: install.sh --server <url> --code <code> [--name <name>] [--remote-management] [--certificate-sha256 <hex> --public-key-sha256 <base64>]" >&2; exit 2; }
    [ "$(id -u)" -eq 0 ] || { echo "Run it with sudo." >&2; exit 1; }

    case $(uname -m) in
        x86_64) arch=x64 ;;
        aarch64) arch=arm64 ;;
        *) echo "No indexer package for $(uname -m)." >&2; exit 1 ;;
    esac
    if command -v apt-get >/dev/null 2>&1; then kind=deb
    elif command -v dnf >/dev/null 2>&1; then kind=rpm
    else echo "Needs apt (Debian, Ubuntu) or dnf (Fedora)." >&2; exit 1
    fi

    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    # apt reads the package as its own unprivileged user.
    chmod 755 "$tmp"
    package="$tmp/nunam-indexer.$kind"
    echo "Downloading the indexer from $server..."
    if [ -n "$public_key" ]; then
        curl -fsSL -k --pinnedpubkey "sha256//$public_key" -o "$package" "$server/api/agent/packages/$kind?arch=$arch"
    else
        curl -fsSL -o "$package" "$server/api/agent/packages/$kind?arch=$arch"
    fi
    chmod 644 "$package"

    if [ "$kind" = deb ]; then
        apt-get install -y "$package" </dev/null
    else
        dnf install -y "$package" </dev/null
    fi

    set -- enroll --server "$server" --code "$code"
    [ -n "$name" ] && set -- "$@" --name "$name"
    [ -n "$remote" ] && set -- "$@" --remote-management
    [ -n "$certificate" ] && set -- "$@" --certificate-sha256 "$certificate"
    /usr/lib/nunam-indexer/Nunam.Indexer "$@" </dev/null

    # The account menu asks on the terminal, when there is one.
    if (exec </dev/tty) 2>/dev/null; then
        nunam-indexer-account </dev/tty
    else
        echo "Runs as the nunam system user. To run as yourself instead: sudo nunam-indexer-account \$USER"
    fi

    systemctl enable nunam-indexer >/dev/null 2>&1
    systemctl restart nunam-indexer
    echo "The indexer is running. It shows up under Machines in the web UI; logs: journalctl -u nunam-indexer"
}

main "$@"
