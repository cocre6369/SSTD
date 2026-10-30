# Testing and verification

## Verified in GitHub Actions Windows x64

Run `41056877597` on branch `arena/6dc16062-sstd` completed the native Windows packaging path:

- Restore/build: passed.
- Portable security/contract/unit suite: passed (140 tests in the portable suite).
- Real Windows UI Automation integration: **4 passed / 0 failed**.
  - guarded fixture typing, clicking, adaptive off-screen search and verification;
  - password-context refusal;
  - real Notepad text insertion and read-back.
- Self-contained publish: passed.
- Executable startup smoke: passed (`PASS: WPF dashboard constructed; DI resolved; SQLite initialized; Ollama availability handled; independent CMD policy rejection passed.`).
- Inno Setup installer build: passed.
- Installer smoke: passed (silent install, installed executable startup, Start Menu shortcut, scoped uninstaller, removal of app/shortcut, user data retained).

The Windows UIA tests run under a temporary standard local user because the hosted runner token is elevated; this matches the product boundary that refuses elevated targets.

## Not verified by CI

- End-to-end natural-language tasks against a live Ollama model. CI does not install Ollama or download models.
- Word/LibreOffice/browser-specific workflows on arbitrary user machines. The generic UIA path is tested, but application accessibility quality varies.
- OCR/vision quality on real screenshots with a vision model.
- Long-running idle memory/CPU behavior on consumer hardware.

## Reproduce locally on Windows

```powershell
./build.ps1 -Package all -Version 1.0.0
```

For UIA tests from an elevated shell, use the same standard-user wrapper as CI:

```powershell
./tools/Run-DesktopTests.ps1
./tools/Run-DesktopTests.ps1 -Command "./tools/Smoke.ps1 -Exe artifacts/publish/SSTD.exe"
```

Reports are written to `artifacts/test-results`, `artifacts/windows.log`, `artifacts/smoke-result.txt`, and `artifacts/installer-smoke.txt`.
