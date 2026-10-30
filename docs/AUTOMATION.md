# Automation architecture

## Pipeline

1. `SSTD.UI` submits a request and exposes actual runner events.
2. `SSTD.AI.OllamaClient` streams a local Ollama response. `OllamaPlanner` requires one schema-constrained action or a final answer.
3. `SSTD.Core.ActionContract` parses and validates action names, argument names/types, ranges and lengths. Unknown fields/actions are rejected.
4. `SSTD.Security.SecurityEngine` independently evaluates policy against live window/process/application metadata.
5. `ActionExecutor` requests approval when needed, rechecks the decision and delegates only to the controlled backend.
6. `WindowsDesktop` uses Windows UI Automation, runtime IDs, control patterns and window/process metadata. `InputController` uses real UIA patterns or Win32 SendInput.
7. The runner observes again after each action. Success messages distinguish dispatched from verified results. Repetition, invalid output, step count and elapsed time are bounded.

## Registered actions

OpenApplication, OpenDocument, FocusWindow, CloseApplication, ClickElement, TypeText, PasteText, PressKey, UseShortcut, Scroll, FindElement, ReadText, SelectText, CopyText, SwitchApplication, WaitForElement, WaitForApplication, InspectWindow, InspectUIHierarchy, CaptureRelevantScreenRegion, Navigate, ComposeText, CreateDocument.

Element IDs refer to observed controls, not model-invented screen coordinates. UIA invoke/value/selection/text/scroll patterns are preferred. Fallback pointer clicks use the live clickable point and hit-test the same process; occluded/out-of-window targets are refused. There is no arbitrary-coordinate action.

Application discovery dynamically enumerates Start Menu shortcuts, App Paths and Windows AppsFolder. It is not a fixed supported-app list. Launches have no model-supplied arguments or elevation. Installed applications and packaged apps must still pass the blocked-capability policy.

`FindElement` searches the observed hierarchy, then performs bounded policy-checked scrolling and fresh observations. It does not click a similar-looking item just to make progress. Virtualized lists and inaccessible canvases may require a different UIA view or may not be automatable.

`CreateDocument` writes UTF-8 text/Markdown or a minimal valid OOXML Word document with headings, paragraphs and bullets. It reads back the result. It is not a full Word layout engine and does not edit/overwrite existing files. Multi-app transfer can use observed text and typed entry without exposing clipboard contents.

## Visual fallback

Windows OCR and optional local vision are supplements, not continuous screen recording. Visual observation is guarded by password/window policy. OCR IDs are short-lived and must be reobserved. A model vision capability is queried from Ollama before sending an image. If reliable observation is unavailable, the task stops or asks for clarification.
