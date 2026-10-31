# Testing and verification

## Verified in GitHub Actions Windows x64

Tag `v1.0.4`, commit `c4eb572`, run [`41163764486`](https://github.com/cocre6369/SSTD/actions/runs/41163764486) completed the native Windows packaging path and published the release:

- Restore/build: passed.
- Portable security/contract/unit suite: passed (142 tests).
- Real Windows UI Automation integration: **4 passed / 0 failed**.
  - guarded fixture typing, clicking, adaptive off-screen search and verification;
  - password-context refusal;
  - real Notepad text insertion and read-back.
- Self-contained publish: passed.
- Executable startup smoke: passed (`PASS: WPF dashboard constructed; DI resolved; SQLite initialized; Ollama availability handled; independent CMD policy rejection passed.`).
- Portable ZIP packaging: passed.
- Native WiX v4 MSI build: passed.
- Downloaded MSI wizard smoke with Mark-of-the-Web: **passed**. The MSI was copied to a temporary download-like path, given `Zone.Identifier=3`, and launched through `msiexec`; the SSTD setup wizard appeared and was dismissed.
- MSI install/repair/uninstall smoke: **passed**.
  - silent per-user install exited 0;
  - installed `SSTD.exe` startup smoke passed;
  - same-package reinstall/repair exited 0 and startup smoke passed again;
  - Start Menu shortcut and Windows Installer uninstall registration were present;
  - uninstall removed the application executable and shortcut;
  - a sentinel in `%LOCALAPPDATA%\SSTD` was retained.
- Downloaded portable ZIP smoke with Mark-of-the-Web: **passed**. The ZIP was extracted, extracted files were given `Zone.Identifier=3`, and `SSTD.exe --smoke-test` passed.

The Windows UIA tests and downloaded portable startup test run under a temporary standard local user because the hosted runner token is elevated; this matches the product boundary that refuses elevated targets. The silent MSI lifecycle test runs as the runner administrator because the hosted runner sets `DisableMsi=1`, which blocks silent per-user MSI installs for non-admin users with error 1625. The MSI smoke still verifies per-user product assignment, user-local install path, HKCU component registration, and scoped uninstall.

Published `v1.0.4` assets:

| File | Size | SHA-256 |
|---|---:|---|
| `SSTD-Setup.msi` | 65,717,059 bytes | `1ce36b902c832b14eccfce75ed10fb839828463c23ffc1dd9031c029f0b9e63b` |
| `SSTD-Portable.zip` | 80,452,547 bytes | `0affbd3d0217958b75fefffc58d4b598c96c3d9533354ff200918d8c755a1568` |
| `SHA256SUMS.txt` | 167 bytes | contains the two hashes above |

## Not verified by CI

- The user's specific Trend Micro configuration. CI proves the native MSI wizard and portable startup path with Mark-of-the-Web, but it cannot reproduce every third-party antivirus interception rule.
- End-to-end natural-language tasks against a live Ollama model. CI does not install Ollama or download models.
- Word/LibreOffice/browser-specific workflows on arbitrary user machines. The generic UIA path is tested, but application accessibility quality varies.
- OCR/vision quality on real screenshots with a vision model.
- Long-running idle memory/CPU behavior on consumer hardware.
- Code signing / SmartScreen reputation. The release is unsigned unless explicitly identified otherwise.

## Reproduce locally on Windows

```powershell
./build.ps1 -Package all -Version 1.0.4
```

For UIA tests from an elevated shell, use the same standard-user wrapper as CI:

```powershell
./tools/Run-DesktopTests.ps1
./tools/Run-DesktopTests.ps1 -Command "./tools/Smoke.ps1 -Exe artifacts/publish/SSTD.exe"
```

Reports are written to `artifacts/test-results`, `artifacts/windows.log`, `artifacts/smoke-result.txt`, `artifacts/msi-smoke.txt`, `artifacts/msi-motw.txt`, and `artifacts/portable-motw.txt`.
