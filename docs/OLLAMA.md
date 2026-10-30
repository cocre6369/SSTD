# Ollama setup

SSTD does **not** contain Ollama, its service, or model weights. It connects to a loopback HTTP API, normally `http://localhost:11434`.

## Windows installation

1. Download Ollama yourself from https://ollama.com/download/windows, or use SSTD's **Install Ollama** button to open that page. SSTD never runs the installer.
2. Review and run the official installer. Start Ollama normally.
3. Install a model with explicit permission. Either use SSTD Settings → AI engine → Download…, or run these commands yourself in your own terminal:
   ```powershell
   ollama pull qwen2.5:7b
   ollama list
   ```
4. In SSTD, retry the connection and select the installed model.
5. Test conversation first: “What can you do, and what will you refuse?”
6. Review Settings → Automation, then enable desktop control. Start with Notepad.

SSTD's terminal blocking applies to its **automation engine**. You may use your own terminal to administer Ollama; SSTD will not operate that terminal.

## Model recommendations

| System | Suggested starting point | Trade-off |
|---|---|---|
| 8 GB RAM / modest CPU | `qwen2.5:3b` | Lower memory; more planning errors and clarification |
| 16 GB RAM / modern CPU or 8 GB VRAM | `qwen2.5:7b` | Practical starting point for structured planning |
| 24–32 GB RAM / 12–16 GB VRAM | `qwen2.5:14b` | Better reasoning, slower and more memory |
| Vision needed | `qwen2.5vl:7b` or another available Ollama vision model | Requires more memory; SSTD checks `/api/show` capabilities |

These are recommendations, not bundled dependencies or benchmark guarantees. Model tags/availability and hardware requirements can change. Check the official library before downloading. Tool-capable instruct models and low temperature (0.1–0.3) are more important than branding. A larger model does not remove the security boundary or guarantee task success.

Start with 8192 context and 2048 maximum output tokens. Increase only if the model and memory allow it. A model load or CPU-only inference may take minutes; SSTD uses bounded request/inactivity timeouts and a visible Stop command.

## API behavior

- `GET /api/version`: availability.
- `GET /api/tags`: installed models.
- `POST /api/show`: capability discovery, including vision.
- `POST /api/chat`: streaming NDJSON; constrained JSON response for action planning.
- `POST /api/pull`: model download only after explicit modal approval, with actual Ollama progress.

Endpoints with remote hosts, credentials, query strings or non-root paths are rejected. Automatic redirects are disabled. SSTD does not call cloud AI APIs. Websites and applications being automated still make their own normal network requests.

## Troubleshooting

- **Offline:** confirm Ollama is running, check the port in Settings, then Retry. SSTD does not silently start or install a service.
- **No models:** explicitly download one; model installation is separate from the app installer.
- **Model not found:** refresh tags and reselect an installed model.
- **Invalid JSON / repeated plans:** use a tool-capable instruct model, lower temperature, or simplify the request. Malformed actions are never executed.
- **Slow first reply:** model loading is normal; try a smaller model. STOP cancels the request, but Ollama may keep the model loaded according to its own keep-alive policy.
- **OCR unavailable:** install a Windows language pack with OCR support yourself. SSTD does not change language or system settings.
