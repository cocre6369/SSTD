# Automation capabilities and action reference

## Implemented observation/action mechanisms

| Capability | Real backend | Verification / limitation |
|---|---|---|
| Installed app discovery | Start-menu `.lnk` target resolution, App Paths registry **read**, Windows AppsFolder enumeration | Resolves generic installed apps; no fixed supported-app list |
| Launch ordinary apps | `Process.Start` with executable path and no arguments; packaged-app activation manager | Visible matching window checked; launch is not proof of the overall task |
| Window focus/switch | Win32 foreground/restore APIs | Foreground handle and process identity checked |
| Graceful close | `WM_CLOSE` | Does not force-kill; save/confirmation dialogs require inspection |
| UI hierarchy | Windows UI Automation, bounded breadth-first tree | Names, types, values, bounds, enabled/offscreen/selected state, common patterns |
| Click | Invoke, SelectionItem, Toggle, ExpandCollapse; guarded clickable-point fallback | Observes changed UI; a changed UI alone does not prove a semantic goal |
| Find off-screen content | FindElement searches current elements, chooses a live scroll container, scrolls and reinspects | Stable IDs; bounded steps; stops at unchanged content/boundary; no blind clicks |
| Scroll | UIA ScrollPattern, guarded wheel fallback | Checks viewport change; virtualized lists expose only currently realizable children |
| Type | Unicode `SendInput`, or UIA ValuePattern for explicit replace/append | Read-back when supported; otherwise explicitly unverified |
| Keyboard | Allow-listed key/chord validation and real input events | No arbitrary OS or shell shortcuts |
| Read/select text | UIA element values and TextPattern | Visible/permitted app content only |
| Copy | Real Ctrl+C | Clipboard is not read or stored, so copy content is not claimed verified |
| Visual fallback | Relevant-window/region capture + Windows.Media.Ocr | OCR line boxes; optional image to a local vision-capable model; no persistent screenshots |
| OCR click | Fresh observed line box, live foreground/hit-test checks | Text recognition is not button semantics; stale boxes rejected |
| Browser navigation | Ctrl+L, URL input, Enter in a focused browser | HTTP(S) public hosts only; inspect actual loaded content before relying on it |
| Document composition | Separate local Ollama request | Returns a draft; does not pretend it was inserted |
| New document files | Exclusive text or OpenXML DOCX writer | One-shot approval; content read-back; no overwrite |
| External commitment | Known risky control/draft detection + modal approval | Unfamiliar opaque UI cannot be classified perfectly |

## Registered actions

`OpenApplication`, `OpenDocument`, `FocusWindow`, `SwitchWindow`, `CloseApplication`, `ClickElement`, `TypeText`, `PasteText`, `PressKey`, `UseShortcut`, `Scroll`, `FindElement`, `ReadText`, `SelectText`, `CopyText`, `WaitForElement`, `WaitForApplication`, `InspectWindow`, `InspectUIHierarchy`, `CaptureRelevantScreenRegion`, `Navigate`, `ComposeText`, `CreateDocument`.

Arguments are strictly validated in `SSTD.Core/Actions/AgentAction.cs`. No arbitrary command action exists. `elementId` and `windowId` must refer to current observations; model-invented IDs are rejected. The backend reacquires UIA elements and checks process identity before acting.

## Application examples — not special cases

- **Notepad/Word:** open the installed app, inspect its startup UI, select a new blank document, locate the editor, compose locally, enter text, inspect read-back. Exact ribbon labels vary by Office version. Creating a new approved DOCX is a safer alternative when direct editing is inaccessible.
- **Spotify/YouTube/media:** inspect actual controls, search, scroll results, select a matching item, then inspect title/player state. There is no Spotify API integration or guaranteed playback-state detector for custom canvases.
- **Chrome/Edge/Firefox:** use the browser's accessibility tree where exposed. SSTD does not inject browser extensions or read the DOM through a debugging port. Browser processes may need accessibility enabled; OCR is a fallback, not a DOM substitute.
- **Discord/Slack/email:** inspect channels and visible messages; explicit approval before entering/committing external messages where detected. Authentication is manual.
- **Multi-app:** one action and fresh observation at a time. There is no pre-generated blind click batch.

## Not implemented as universal guarantees

Pixel-perfect understanding of every game/canvas; DirectX/OpenGL overlay automation; human-equivalent video comprehension; automatic CAPTCHA solving; arbitrary Office formatting macros; browser-internal DOM access; robust detection of every purchase/send icon; autonomous recovery from every modal; full OS sandboxing. These are explicit limitations, not hidden mock implementations.
