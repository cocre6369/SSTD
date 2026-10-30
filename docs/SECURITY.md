# Security boundary

SSTD is a desktop assistant, **not an operating-system sandbox or a security product**. It runs as the signed-in user. Do not run it elevated. Keep Windows, Ollama and applications updated.

## Independent enforcement

Ollama proposes one typed action from the closed registry in `SSTD.Core`. It cannot supply a shell command, executable arguments, an elevation verb, an arbitrary HTTP endpoint, or a policy override. `SSTD.Security` evaluates the request using live OS metadata. `SSTD.Automation.ActionExecutor` is the only production execution entry point; it checks policy before execution and again before input. The backend repeats process/window checks immediately before each input chunk.

The model, planner and UI have no approval-authority flag. An approval is valid only for one action and its observed destination. A changed window/control/draft invalidates it. Cancelling approval performs no action. Disabling desktop control permits conversation and local text composition, not application control.

## Prohibited

- Command Prompt, PowerShell, Windows Terminal, WSL/bash, scripting hosts, interpreters used as command runners, remote shells and console-subsystem executables.
- Registry/management consoles, system configuration, services, device/disk management, scheduled tasks, security settings, credential stores, password managers and elevated applications.
- Developer consoles, command palettes, shell/file-manager address-bar input, Run dialogs and executable activation from file lists.
- Password/authentication fields and authentication windows. UIA password screening occurs before text or image exposure. Ambiguous credential labels fail closed. Passwords, private keys, API tokens and verification codes must be entered manually.
- Deleting, formatting, uninstalling, overwriting/replacing files, moving files through shell shortcuts, account changes, or disabling security controls.
- Arbitrary executable arguments, `runas`, `cmd /c`, PowerShell expressions, scripts and shortcut arguments. Shortcut targets are resolved and their arguments discarded.

Ordinary editing/navigation shortcuts are allow-listed. Save/Save As shortcuts and buttons require explicit approval; the safer `CreateDocument` action creates only a new `.txt`, `.md` or `.docx` under Documents/Desktop, using exclusive creation. It never replaces an existing file. Existing documents may be opened and edited in their application, but SSTD will not promise an overwrite-safe save through an arbitrary third-party Save dialog.

## External changes

Sending, replying, posting, publishing, submitting, uploading, sharing, purchasing, subscribing, installing, account changes and comparable commits require a visible confirmation showing the action, destination and available draft. Pressing Enter in browser forms/message composers also requires confirmation. Navigation is separately bounded to public HTTP(S); private/loopback administration URLs and executable/browser-internal schemes are blocked.

Confirmation is mandatory for these categories. There is no model-controlled “trusted” bypass. Review the destination and content yourself. UI labels are heuristics: an unfamiliar app can mislabel a button. The policy is defense in depth, not proof of an application's semantics.

## Data handling

Inference is restricted to loopback HTTP Ollama. Redirects are rejected. No cloud reasoning fallback exists. Screenshots are on demand, disabled by default, bounded to the permitted window and not persisted. Vision-model image transmission additionally requires a detected vision-capable model. OCR is local Windows OCR.

Clipboard copy uses the actual application shortcut but does not read/store clipboard contents. Paste uses Unicode input rather than reading the clipboard. Secret-pattern screening and bounded/redacted task history reduce accidental retention; they cannot identify every possible secret in arbitrary text. Conversation/task history can be disabled or cleared. Screenshots and clipboard contents are not stored in SQLite.

The installer is per-user, has no admin/system configuration steps and never installs/removes Ollama or models. Uninstall removes SSTD's installed files/shortcuts only; user history/settings are retained unless cleared in the app.

## Residual risks and stop behavior

An ordinary application may have capabilities SSTD cannot fully infer from accessibility metadata. UIA support, custom controls, browser page structure, localization, focus races and OCR errors limit reliability. This implementation deliberately refuses uncertain/protected cases rather than claiming perfect universal blocking. It cannot protect against a malicious application already running as you, compromised Windows, or a user manually performing an action.

STOP cancels HTTP streaming, observation waits, approvals and subsequent input. It cannot retract a click, keystroke, message or file already accepted by Windows/app. Input is chunked and targets are rechecked, but a native call already in flight may finish. A physical user focus change can race with input; avoid using the computer while a task runs.
