# SSTD security boundary

## What this boundary is — and is not

SSTD is a **policy-constrained automation client**, not an OS sandbox, antivirus product, or proof that an arbitrary GUI is harmless. It runs as the signed-in user (`asInvoker`, no `uiAccess`). Windows accounts, application permissions, AppLocker/WDAC, and virtualization remain the actual operating-system isolation mechanisms. No prompt or classifier is treated as an authorization mechanism.

A normal application can have capabilities that are not discoverable from its name or accessibility tree. SSTD cannot prove that an unfamiliar program will never spawn a shell, change a file through an opaque custom control, or send data through an unlabeled icon. Use a separate standard Windows account or Windows Sandbox for stronger isolation. Do not use SSTD with sensitive accounts until you have reviewed the target workflow.

## Enforcement path

```text
User request
  → local Ollama: one structured next step
  → closed action schema (unknown names/parameters rejected)
  → SSTD.Security: live target/application/path/input policy
  → one-shot UI approval where needed
  → reauthorize + recheck destination/draft
  → SSTD.Automation: native UIA / SendInput backend
  → fresh observation and explicit verification state
```

`SSTD.Security` references only `SSTD.Core`; it has no Ollama dependency. `ActionExecutor` authorizes every action, and `DesktopBackend` independently checks policy again before calling Windows. The UI cannot supply an “approved” flag to bypass either check. The model cannot generate shell commands, scripts, arbitrary delegates, registry operations, or an elevation verb.

## Hard refusals

- Consoles and scripting hosts: CMD, PowerShell, Windows Terminal, ISE, WSL/bash, common remote shells and script interpreters.
- Registry, management consoles, device/disk/services/policy/scheduler tools, Task Manager, system configuration, recovery, credential vaults and common password managers.
- Unknown executables under Windows system directories, with narrow exceptions for ordinary inbox Notepad/Explorer/Paint. This deliberately favors refusal over guessing.
- Packaged Terminal/PowerShell/Settings/Security/administration and password-manager app identities.
- Already-open prohibited windows: no click, type, read, or screenshot fallback.
- Elevated target windows, protected processes whose identity cannot be verified, and SSTD's own approval UI.
- Win/Run/Task-Manager shortcuts, arbitrary key chords, developer-console shortcuts, terminal controls, macro editors and “Run as administrator”.
- Password/authentication fields, secret-looking text, authentication windows and credential paths. OCR is not permitted to replace UIA password screening.
- Deleting, renaming, moving or replacing files; destructive buttons and file-manager mutation shortcuts.
- Save/Save As shortcuts and controls. They can silently overwrite existing files. **New file creation** is the supported alternative.
- Shell/file/protocol navigation, browser credential pages, private-network administration URLs and arbitrary command strings in address bars.

Application paths are checked as local executable files. Shortcut arguments are discarded. Start-menu shortcuts and App Paths are discovery sources, not execution authorities. Renamed system tools are additionally screened using original-filename/description metadata and system-directory defaults. A maliciously crafted ordinary-looking executable is still outside what this policy can certify.

## External commitments

Known send/reply/post/publish/submit/upload/share/purchase/install/save controls require an explicit, one-shot modal approval. Message text entry in recognized communication apps also requires review. The approval shows actual visible draft text when UIA exposes it, otherwise explicitly says it cannot verify the draft and asks the user to inspect the target. The destination and visible draft are rechecked after approval.

**Conservative limitation:** semantic detection of external commitment is heuristic for unfamiliar/custom UIs. An unlabeled or misleading button cannot be classified with certainty. For high-risk sites/accounts, use manual operation or a separate sandbox. No “trust all sends” switch is provided.

## Files and clipboard

File opening is restricted to fixed local drives under Documents/Desktop, with a document-extension allow-list. Reparse points and credential filenames are rejected. New `.txt`, `.md`, `.docx` creation requires approval and uses exclusive `CreateNew`; a competing file causes failure, never replacement. New DOCX content is read back from OpenXML before verification is reported.

Copy sends Ctrl+C but does **not** read clipboard contents into the model or database. PasteText enters model-provided text using Unicode input events; it does not inspect the clipboard. Normal Ctrl+V remains available in approved editors, so users should not copy credentials while automation is active. Password fields and authentication contexts stop execution.

## Observation and privacy

- UIA is preferred; screenshots are on demand only.
- OCR uses installed Windows recognition languages.
- A screenshot is sent to Ollama only if the user enables that setting and `/api/show` reports `vision` capability.
- Screenshots are not written to history. OCR text is secret-screened before exposure.
- Password fields cause the observation to be dropped, not merely masked after display.
- SQLite contains requests, assistant replies, action phases and outcomes, not raw screenshots or clipboard contents. Secret-pattern redaction is best effort, not a DLP guarantee.
- Local SQLite is not encrypted. Protect the Windows account and disk with Windows sign-in/BitLocker as appropriate.

## Cancellation and race conditions

STOP cancels model streaming, waits, approvals and future action steps. Already delivered input, an invoked UIA button, a started process, a created file or an accepted external transaction cannot be rolled back. Keyboard chunks are small; target identity is checked before each chunk. There remains an inherent race between checking the foreground and Windows processing input: do not type or switch windows during automation. Native UIA calls may take time to return; cancellation is cooperative, not a kernel-level input firewall.

## Reporting a bypass

Do not include real credentials or personal documents in a report. Provide a minimal synthetic application/action sequence, the expected policy result, and the observed result. Security fixes should add an independent regression test before changing model prompts.
