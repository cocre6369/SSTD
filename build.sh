#!/usr/bin/env bash
# Linux can test portable components and cross-publish Windows binaries, not execute WPF.
set -euo pipefail
cd "$(dirname "$0")"
command -v dotnet >/dev/null || { echo 'Install the .NET 8 SDK first. See README.md.' >&2; exit 1; }
mkdir -p artifacts
if [[ "${1:-test}" == "test" ]]; then
  dotnet test tests/SSTD.Tests/SSTD.Tests.csproj -c Release
elif [[ "${1:-}" == "cross-publish" ]]; then
  dotnet publish src/SSTD.UI/SSTD.UI.csproj -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o artifacts/publish
  echo 'Cross-published artifacts/publish/SSTD.exe. Run Windows smoke tests on a Windows desktop.'
else
  echo 'Usage: ./build.sh [test|cross-publish]' >&2; exit 2
fi
