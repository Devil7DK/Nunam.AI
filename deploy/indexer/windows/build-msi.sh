#!/usr/bin/env bash
# Builds the Windows indexer installer, artifacts/packages/nunam-indexer-<version>-x64.msi, on Linux.
# Needs the .NET SDK and wixl/wixl-heat (msitools: dnf install msitools, apt install wixl).
# VERSION=x.y.z overrides the version in Nunam.Indexer.csproj.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
project="$repo/Nunam.Indexer/Nunam.Indexer.csproj"
out="$repo/artifacts/packages"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$out"
version=${VERSION:-$(dotnet msbuild "$project" -getProperty:Version)}

# Self-contained, so no .NET runtime is needed on the machine.
pub="$work/publish"
dotnet publish "$project" -c Release -r win-x64 --self-contained -p:Version="$version" -o "$pub"
rm -f "$pub/appsettings.Development.json"

# The executable is listed in Nunam.Indexer.wxs, in the component that installs the service.
(cd "$work" && find publish -type f ! -path publish/Nunam.Indexer.exe | sort |
    wixl-heat --prefix publish/ --directory-ref INSTALLDIR --component-group PublishedFiles --var var.SourceDir --win64) \
    > "$work/files.wxs"

wixl --arch x64 -D Win64=yes -D Version="$version" -D SourceDir="$pub" -D ScriptDir="$here" \
    -o "$out/nunam-indexer-$version-x64.msi" "$here/Nunam.Indexer.wxs" "$work/files.wxs"

ls -lh "$out/nunam-indexer-$version-x64.msi"
