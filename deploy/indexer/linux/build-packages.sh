#!/usr/bin/env bash
# Builds the Linux indexer packages, artifacts/packages/nunam-indexer_<version>_amd64.deb and
# nunam-indexer-<version>-1.x86_64.rpm. Needs the .NET SDK and Docker; the packaging tools run in
# containers. VERSION=x.y.z overrides the version in Nunam.Indexer.csproj.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
project="$repo/Nunam.Indexer/Nunam.Indexer.csproj"
out="$repo/artifacts/packages"
debian_image="debian:trixie@sha256:fac46bff2e02f51425b6e33b0e1169f55dfb053d83511ca28aa50c09fd5ed7a4"
fedora_image="fedora:44@sha256:15da97ed9aa94dd3d92c3925eb89520f6c80e07ea7ee24cba848d1f8b6912b3e"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$out"
version=${VERSION:-$(dotnet msbuild "$project" -getProperty:Version)}

# Self-contained, so no .NET runtime is needed on the machine.
root="$work/root"
lib="$root/usr/lib/nunam-indexer"
dotnet publish "$project" -c Release -r linux-x64 --self-contained -p:Version="$version" -o "$lib"
rm -f "$lib/appsettings.Development.json"
# Syncfusion copies its native libraries for every platform; keep this one's and the OCR language data.
find "$lib/runtimes" -mindepth 1 -maxdepth 1 ! -name linux-x64 ! -name tessdata -exec rm -rf {} +
chmod -R u=rwX,go=rX "$lib"
chmod 0755 "$lib/Nunam.Indexer"

install -D -m 0644 "$here/nunam-indexer.service" "$root/usr/lib/systemd/system/nunam-indexer.service"
install -D -m 0644 "$here/nunam-indexer.sysusers" "$root/usr/lib/sysusers.d/nunam-indexer.conf"
install -D -m 0640 "$here/appsettings.Local.example.json" "$root/etc/nunam-indexer/appsettings.Local.json"
chmod 0750 "$root/etc/nunam-indexer"
# The indexer reads its settings from its own folder; packaged settings belong in /etc.
ln -s /etc/nunam-indexer/appsettings.Local.json "$lib/appsettings.Local.json"

cp "$here"/*.sh "$here/nunam-indexer.spec" "$work/"
docker run --rm -v "$work:/work:z" -v "$out:/out:z" "$fedora_image" sh -c "
    dnf -y -q install rpm-build >/dev/null 2>&1 &&
    rpmbuild -bb --quiet \
        --define 'pkg_version $version' --define 'tree /work/root' --define 'scripts /work' \
        --define '_rpmdir /out' --define '_build_name_fmt %%{NAME}-%%{VERSION}-%%{RELEASE}.%%{ARCH}.rpm' \
        /work/nunam-indexer.spec &&
    chown $(id -u):$(id -g) /out/nunam-indexer-$version-1.x86_64.rpm"

mkdir "$root/DEBIAN"
cp "$here/after-install.sh" "$root/DEBIAN/postinst"
cp "$here/before-remove.sh" "$root/DEBIAN/prerm"
cp "$here/after-remove.sh" "$root/DEBIAN/postrm"
echo /etc/nunam-indexer/appsettings.Local.json > "$root/DEBIAN/conffiles"
cat > "$root/DEBIAN/control" <<CONTROL
Package: nunam-indexer
Version: $version
Architecture: amd64
Maintainer: Nunam <nunam@localhost>
Section: utils
Priority: optional
Installed-Size: $(du -sk --exclude=DEBIAN "$root" | cut -f1)
Depends: libc6, libgcc-s1, libstdc++6, libssl3t64 | libssl3, zlib1g, libicu76 | libicu74 | libicu72, systemd
Recommends: libleptonica6 | liblept5, ffmpeg, acl
Description: Indexes local files into the Nunam search index
 Runs on a machine with files: scans and watches its folders, parses documents, images, audio and
 video, and writes what it finds to the Nunam database on the homelab.
CONTROL
docker run --rm --user "$(id -u):$(id -g)" -v "$work:/work:z" -v "$out:/out:z" "$debian_image" \
    dpkg-deb --root-owner-group -Zxz --build /work/root "/out/nunam-indexer_${version}_amd64.deb"

ls -lh "$out"/nunam-indexer*"$version"*
