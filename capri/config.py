"""Config loading. Defaults live here; config.yaml in the repo root overrides them."""
from __future__ import annotations

import copy
import base64
import json
import os
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # yaml is optional, defaults still work
    yaml = None

FROZEN = getattr(sys, "frozen", False)  # the portable dev build (Capri.exe)
# ROOT: where config.yaml, data\ and (portable) models\ + engines\ live. Frozen, that's the folder Capri.exe is in,
# while the assets ship inside the bundle.
_SRC = Path(__file__).resolve().parent.parent
ROOT = Path(sys.executable).resolve().parent if FROZEN else _SRC
if os.environ.get("CAPRI_HOME"):  # run from source against another folder (testing the portable layout)
    ROOT = Path(os.environ["CAPRI_HOME"]).resolve()
ASSETS = (Path(getattr(sys, "_MEIPASS", _SRC)) if FROZEN else _SRC) / "assets"
DATA = ROOT / "data"
API_PROFILE = DATA / "api_profile.json"

DEFAULTS: dict = {
    # "full": everything (DJ, ERU AI). "dev": the portable test build for other devs: no DJ,
    # models + engines in its own folder (portable.py)
    "edition": "full",
    "eru_ai_root": r"D:\ERU AI",
    # ERU AI's Qdrant vector memory (in Capri's own store under data\); falls back to JSON facts if it can't load
    "eru": {"memory": True},
    # how she lives on the desktop
    "desk": {
        "size": 240,  # her height in pixels
        "video_size": 360,  # her height in video mode (big enough for a 9:16 phone crop)
        "roam_seconds": [18, 40],  # she zaps somewhere new every this-many seconds
        "intro": True,  # her entrance on launch (the assets/desk/splash flipbook: fullscreen, then shrinks onto her)
        # these four are first-run defaults; after that her right-click menu remembers your choice
        "dj": False,  # let her swap your YouTube music for Cyberpunk OSTs when she thinks it's not nova
        "vibe": True,  # her hair glitches to the beat of whatever's playing
        "eyes": True,  # she glances at the window you're in (title + Windows OCR, nothing saved)
        "chatter": True,  # she pipes up when you've been quiet a while, and reacts to what you open
        "chatter_idle_minutes": 6,  # no keyboard / mouse this long: she says something
        "react_cooldown_minutes": 4,  # at most one unprompted remark per this many minutes
    },
    "stt": {
        "mode": "hold",  # hold = hold the talk key while you speak; auto = she listens all the time; off
        "talk_key": "ctrl+1",
        "input_device": "",  # part of the mic's name ("FDUCE"); "" = Windows default (also pickable in her menu)
        "model": "small",  # only if ERU's whisper model isn't found (multilingual: she hears all eight languages)
        "hotwords": "Capri, Campy, Capricorn, choom, chooms, preem, nova",
        "silence_seconds": 0.7,  # auto mode: this much quiet ends a sentence
        "energy_threshold": 0.015,
    },
    "llm": {
        # openai: any OpenAI-compatible server; this custom build uses Groq GPT-OSS by default
        "backend": "openai",
        "model_path": "",
        "n_ctx": 5120,
        "n_gpu_layers": -1,
        "base_url": "https://api.groq.com/openai/v1",
        "api_key": "",
        "api_key_env": "GROQ_API_KEY",
        "max_tokens_param": "max_completion_tokens",
        "reasoning_effort": "low",
        "model": "openai/gpt-oss-120b",
        "temperature": 0.7,
        "max_tokens": 256,  # enough room for hosted reasoning models to finish a short reply
    },
    # Claude Code as her brain: an extra pick in her Brain menu when the `claude` CLI is installed and signed in
    "claude_code": {
        "exe": "",  # "" = found on PATH / where its installer puts it
        "model": "",  # "" = Claude Code's default; "sonnet", "haiku", "opus" or a full model name
        "chat_effort": "low",  # her chat lines are short: low effort answers fastest
        "task_effort": "medium",  # jobs on the PC (her hands)
        "chat_timeout": 120,
        "task_timeout": 300,
    },
    "tts": {
        # blips: no voice, old-school RPG talking blips while the bubble types (the default for now)
        # gpt_sovits: Capri's cloned voice through ERU's GPT-SoVITS (voice_server/sovits_server.py, started for you)
        # none: silent text
        "backend": "blips",
        "gpt_sovits": {
            "url": "http://127.0.0.1:9880/tts",
            "autostart": True,
            "version": "v2",
            "ref_audio_path": "",  # empty = voices/capri/ref.wav
            "prompt_text": "",  # empty = voices/capri/ref.txt
            "prompt_lang": "en",
            "text_lang": "en",
            "speed_factor": 1.0,
        },
    },
    "spotify": {
        "client_id": "",
        "client_secret": "",
        "redirect_uri": "http://127.0.0.1:8888/callback",
    },
    "memory": {"file": "data/memory.json", "history_turns": 12},
    # web search: a Brave Search API key (free tier: https://brave.com/search/api/) or a SearXNG instance's URL;
    # with neither she still looks things up on Wikipedia + DuckDuckGo instant answers
    "search": {"brave_api_key": "", "searxng_url": ""},
    "tools": {"files_dir": ""},  # where the text files she writes go ("" = Documents/Capri)
    # MCP servers she can use as tools (capri/abilities/mcp.py has an example)
    "mcp": {"servers": {}},
    # her real eyes (looking at pictures for "rate this"): InternVL3.5-1B, ~2.5 GB of VRAM. "" = off
    "sight": {"model_dir": "D:/ERU AI/models/vision/InternVL3_5-1B-HF"},
    "user_name": "you",
}


def _merge(base: dict, extra: dict) -> dict:
    out = copy.deepcopy(base)
    for k, v in (extra or {}).items():
        if isinstance(v, dict) and isinstance(out.get(k), dict):
            out[k] = _merge(out[k], v)
        else:
            out[k] = v
    return out


def load() -> dict:
    cfg = DEFAULTS
    path = Path(os.environ.get("CAPRI_CONFIG", ROOT / "config.yaml"))
    if path.exists() and yaml is not None:
        with open(path, encoding="utf-8") as f:
            cfg = _merge(DEFAULTS, yaml.safe_load(f) or {})
    # API settings made in Capri's Brain menu live in a separate user-data file, so changing providers doesn't
    # rewrite config.yaml or its comments. Windows protects the key with DPAPI for this user account.
    try:
        profile = json.loads(API_PROFILE.read_text(encoding="utf-8"))
        if not isinstance(profile, dict):
            raise ValueError("saved API settings must be an object")
        api_key = profile.pop("api_key", "")
        encrypted = profile.pop("api_key_dpapi", "")
        if encrypted and os.name == "nt":
            import win32crypt

            api_key = win32crypt.CryptUnprotectData(base64.b64decode(encrypted), None, None, None, 0)[1].decode("utf-8")
        if isinstance(profile, dict):
            cfg["llm"].update({k: v for k, v in profile.items() if k in _API_PROFILE_KEYS})
            if api_key:
                cfg["llm"]["api_key"] = api_key
    except Exception as e:
        print(f"[capri] couldn't load saved API settings: {e}")
    DATA.mkdir(exist_ok=True)
    return cfg


_API_PROFILE_KEYS = {
    "backend", "base_url", "model", "api_key_env", "max_tokens_param", "reasoning_effort", "temperature",
    "max_tokens", "model_path", "n_ctx", "n_gpu_layers",
}


def save_api_profile(llm: dict) -> None:
    """Persist the active brain settings without rewriting config.yaml; protect the API key on Windows."""
    profile = {k: llm[k] for k in _API_PROFILE_KEYS if k in llm}
    key = llm.get("api_key", "")
    if key and os.name == "nt":
        import win32crypt

        encrypted = win32crypt.CryptProtectData(key.encode("utf-8"), "Capri API key", None, None, None, 0)
        profile["api_key_dpapi"] = base64.b64encode(encrypted).decode("ascii")
    elif key:
        profile["api_key"] = key
    DATA.mkdir(parents=True, exist_ok=True)
    API_PROFILE.write_text(json.dumps(profile, indent=2), encoding="utf-8")
