"""JSON-RPC client to the OculiX JVM bridge over stdin/stdout.

The bridge is a fat JAR (~160 MB) containing oculixapi + Apertix OpenCV +
our minimal RPC server. It's downloaded once from GitHub Releases on first
use and cached under ``~/.oculix/lib/``.
"""

from __future__ import annotations

import atexit
import json
import os
import shutil
import subprocess
import threading
import urllib.request
import weakref
from pathlib import Path
from typing import Any, Optional

# --- bridge JAR distribution -------------------------------------------------

BRIDGE_VERSION = "0.1.0"
BRIDGE_JAR_NAME = f"operix-jvm-bridge-{BRIDGE_VERSION}.jar"
BRIDGE_JAR_URL = (
    "https://github.com/oculix-org/Operix/releases/download/"
    f"jvm-bridge-{BRIDGE_VERSION}/{BRIDGE_JAR_NAME}"
)
JAR_DIR = Path(os.path.expanduser("~/.oculix/lib"))


def _ensure_jar() -> Path:
    jar_path = JAR_DIR / BRIDGE_JAR_NAME
    if jar_path.exists():
        return jar_path
    JAR_DIR.mkdir(parents=True, exist_ok=True)
    print(f"[OculiX] Downloading {BRIDGE_JAR_NAME} (~160 MB)…")
    urllib.request.urlretrieve(BRIDGE_JAR_URL, jar_path)
    print(f"[OculiX] Saved to {jar_path}")
    return jar_path


# --- JSON-RPC client ---------------------------------------------------------

class BridgeError(RuntimeError):
    """Raised when the JVM side returned an error for a request."""


class Bridge:
    """Owns the JVM child process and serialises RPC requests over stdio."""

    def __init__(self, jar_path: Optional[Path] = None, java_bin: str = "java"):
        if shutil.which(java_bin) is None:
            raise RuntimeError(
                f"{java_bin!r} not found on PATH. Install Java 11+ "
                "(https://adoptium.net) and retry."
            )
        self._jar = jar_path or _ensure_jar()
        self._java = java_bin
        self._proc: Optional[subprocess.Popen] = None
        self._lock = threading.Lock()
        self._next_id = 0
        # Python-side identity dedup: same Java ref always yields the same
        # RemoteObject so __del__ doesn't kill a ref that's still in use.
        self._cache: "weakref.WeakValueDictionary[str, RemoteObject]" = (
            weakref.WeakValueDictionary())

    def start(self) -> None:
        if self._proc is not None:
            return
        self._proc = subprocess.Popen(
            [self._java, "-jar", str(self._jar)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            bufsize=1,           # line buffered
            text=True,
            encoding="utf-8",
        )
        atexit.register(self.stop)

    def stop(self) -> None:
        if self._proc is None:
            return
        try:
            self._proc.stdin.close()
        except Exception:
            pass
        try:
            self._proc.terminate()
            self._proc.wait(timeout=5)
        except Exception:
            self._proc.kill()
        self._proc = None

    def _request(self, payload: dict) -> Any:
        with self._lock:
            if self._proc is None:
                self.start()
            self._next_id += 1
            payload["id"] = self._next_id
            line = json.dumps(payload, ensure_ascii=False) + "\n"
            self._proc.stdin.write(line)
            self._proc.stdin.flush()

            response_line = self._proc.stdout.readline()
            if not response_line:
                stderr = self._proc.stderr.read() if self._proc.stderr else ""
                raise BridgeError(f"JVM bridge died. stderr:\n{stderr}")
            response = json.loads(response_line)
            if "error" in response:
                raise BridgeError(response["error"])
            return response["result"]

    # --- public RPC operations ----------------------------------------------

    def create(self, classname: str, args: list) -> "RemoteObject":
        result = self._request({"class": classname, "args": _encode_args(args)})
        return _decode(self, result)

    def call(self, ref: str, method: str, args: list) -> Any:
        result = self._request({"ref": ref, "method": method, "args": _encode_args(args)})
        return _decode(self, result)

    def call_static(self, classname: str, method: str, args: list) -> Any:
        result = self._request({
            "class": classname, "method": method,
            "static": True, "args": _encode_args(args),
        })
        return _decode(self, result)

    def release(self, ref: str) -> None:
        # Best-effort; ignore errors so __del__ never raises.
        try:
            self._request({"ref": ref, "release": True})
        except Exception:
            pass


# --- value codec -------------------------------------------------------------

class RemoteObject:
    """Opaque handle to a Java object held by the JVM bridge."""

    __slots__ = ("_bridge", "_ref", "_class", "__weakref__")

    def __init__(self, bridge: Bridge, ref: str, java_class: str):
        self._bridge = bridge
        self._ref = ref
        self._class = java_class

    def _call(self, method: str, *args) -> Any:
        return self._bridge.call(self._ref, method, list(args))

    def __repr__(self) -> str:
        return f"<RemoteObject {self._class}#{self._ref}>"

    def __del__(self):
        # Best-effort GC. May fail during interpreter shutdown.
        try:
            self._bridge.release(self._ref)
        except Exception:
            pass


def _encode_args(args: list) -> list:
    return [_encode(a) for a in args]


def _encode(v: Any) -> Any:
    if isinstance(v, RemoteObject):
        return {"__ref": v._ref}
    return v


def _decode(bridge: Bridge, v: Any) -> Any:
    if isinstance(v, dict) and "__ref" in v:
        ref = v["__ref"]
        cached = bridge._cache.get(ref)
        if cached is not None:
            return cached
        obj = RemoteObject(bridge, ref, v.get("__class", "?"))
        bridge._cache[ref] = obj
        return obj
    return v


# --- module-wide singleton ---------------------------------------------------

_default_bridge: Optional[Bridge] = None


def default_bridge() -> Bridge:
    global _default_bridge
    if _default_bridge is None:
        _default_bridge = Bridge()
    return _default_bridge
