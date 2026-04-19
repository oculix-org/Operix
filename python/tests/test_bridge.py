"""Integration tests for the Python wrapper.

Spawns the actual JVM bridge JAR and exchanges JSON-RPC over stdio. The
JAR is built via ``mvn package`` in ``../jvm-bridge/``.
"""

from __future__ import annotations

import os
import subprocess
from pathlib import Path

import pytest

from oculix._bridge import Bridge, BridgeError, RemoteObject

REPO_ROOT = Path(__file__).resolve().parents[2]
LOCAL_JAR = REPO_ROOT / "jvm-bridge" / "target" / "operix-jvm-bridge.jar"


def _java_available() -> bool:
    try:
        subprocess.run(["java", "-version"], capture_output=True, check=True)
        return True
    except Exception:
        return False


pytestmark = pytest.mark.skipif(
    not LOCAL_JAR.exists() or not _java_available(),
    reason=f"Bridge JAR not built or Java missing: {LOCAL_JAR}",
)


@pytest.fixture
def bridge():
    b = Bridge(jar_path=LOCAL_JAR)
    b.start()
    yield b
    b.stop()


def test_construct_and_call_static_jdk_class(bridge):
    sb = bridge.create("java.lang.StringBuilder", [])
    assert isinstance(sb, RemoteObject)
    assert sb._call("append", "hello ") is sb or sb._call("append", "hello ")._ref == sb._ref
    sb._call("append", "operix")
    assert sb._call("length") == 12
    assert sb._call("toString") == "hello operix"


def test_static_method_call(bridge):
    # java.lang.Integer.toBinaryString(42) -> "101010"
    result = bridge.call_static("java.lang.Integer", "toBinaryString", [42])
    assert result == "101010"


def test_overload_resolution(bridge):
    # Math.abs has overloads for int, long, float, double — sending an int
    # must hit Math.abs(int) and stay an int.
    assert bridge.call_static("java.lang.Math", "abs", [-7]) == 7
    # Math.max(double, double) (use doubles to disambiguate)
    assert bridge.call_static("java.lang.Math", "max", [3.5, 2.5]) == 3.5


def test_unknown_class_raises(bridge):
    with pytest.raises(BridgeError) as exc:
        bridge.create("no.such.Class", [])
    assert "ClassNotFoundException" in str(exc.value) or "no.such.Class" in str(exc.value)


def test_release_drops_ref(bridge):
    sb = bridge.create("java.lang.StringBuilder", [])
    ref = sb._ref
    bridge.release(ref)
    with pytest.raises(BridgeError):
        bridge.call(ref, "length", [])


def test_chain_returns_same_ref(bridge):
    """Identity interning: the JVM bridge should return the same ref when a
    method returns ``this`` (e.g. StringBuilder.append)."""
    sb = bridge.create("java.lang.StringBuilder", [])
    chained = sb._call("append", "x")
    assert chained._ref == sb._ref
