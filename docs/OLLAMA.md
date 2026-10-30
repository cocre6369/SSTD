# Ollama setup

SSTD uses **http://localhost:11434** by default. Only loopback HTTP endpoints are accepted. It never falls back to a cloud model.

1. Install Ollama yourself from the official Ollama Windows download. SSTD's install action opens that page only after confirmation; it does not silently install software.
2. Start Ollama normally (not as administrator). Verify `ollama list` yourself if desired. SSTD itself never launches a terminal.
3. In SSTD Settings, select a model or explicitly download one. A download shows its exact name/size when available and requires confirmation. Pull progress is streamed and cancellable.
4. Enable desktop control only after reading the security boundary.

## Model choice

Start with a recent instruction-following model that fits your RAM/VRAM, such as `qwen2.5:7b-instruct` or `llama3.1:8b`. Larger 14B/32B models may plan better but need more memory and are slower. Names are examples, not a supported-app list; use the tags actually available in your Ollama installation. Tool/function calling support alone does not guarantee reliable structured desktop planning.

For visual fallback choose a vision model such as `qwen2.5vl:7b` or `llama3.2-vision:11b`, subject to available hardware and Ollama support. SSTD queries `/api/show` capabilities and will not send screenshots to a model not identified as vision-capable. Windows OCR does not require a vision model.

Model files can be several gigabytes. SSTD does not download one at startup. If Ollama is absent, the dashboard reports the actual connection error and chat/automation remain unavailable; settings and history still work. If a selected model is missing, choose an installed model or explicitly pull it.

## Privacy and limits

Prompts, bounded observations and optional screenshots go to your loopback Ollama service. Ollama's own configuration/model provenance is outside SSTD's control. Do not configure a proxy that forwards local traffic elsewhere. No screenshots or clipboard contents are persisted by SSTD. Model inference can be slow; STOP cancels the HTTP stream, though Ollama may continue briefly releasing server resources.
