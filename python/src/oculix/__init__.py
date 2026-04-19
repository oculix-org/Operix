"""OculiX — visual automation for the real world (Python wrapper).

    >>> from oculix import Screen
    >>> Screen().click("button.png")
"""

from oculix._bridge import Bridge, BridgeError, RemoteObject, default_bridge

__version__ = "0.1.0"
__all__ = [
    "Bridge", "BridgeError", "RemoteObject", "default_bridge",
    "Screen", "Region", "Pattern", "Match", "App",
    "VNCScreen", "ADBScreen", "SSHTunnel",
    "PaddleOCREngine", "OCR", "Key", "Settings",
]


# --- thin Pythonic wrappers around the most-used Oculix classes -------------
#
# Each class is a one-liner that delegates everything to the bridge. We keep
# the names PEP-8 friendly (Click() not click()) on the Java side.

class _OculixClass:
    """Base for explicit class wrappers — gives autocomplete and type hints."""

    JAVA_CLASS: str = ""

    def __init__(self, *args):
        self._remote = default_bridge().create(self.JAVA_CLASS, list(args))

    def _call(self, method, *args):
        return self._remote._call(method, *args)


class Screen(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.Screen"

    def click(self, target):       return self._call("click", target)
    def doubleClick(self, target): return self._call("doubleClick", target)
    def rightClick(self, target):  return self._call("rightClick", target)
    def hover(self, target):       return self._call("hover", target)
    def type(self, text):          return self._call("type", text)
    def paste(self, text):         return self._call("paste", text)
    def wait(self, target, timeout=10.0): return self._call("wait", target, float(timeout))
    def find(self, target):        return self._call("find", target)
    def exists(self, target, timeout=3.0):
        result = self._call("exists", target, float(timeout))
        return result is not None
    def text(self):                return self._call("text")
    def capture(self):             return self._call("capture")


class Region(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.Region"

    def __init__(self, x: int, y: int, w: int, h: int):
        super().__init__(int(x), int(y), int(w), int(h))

    def click(self, target):  return self._call("click", target)
    def find(self, target):   return self._call("find", target)
    def text(self):           return self._call("text")


class Pattern(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.Pattern"

    def __init__(self, image_path: str):
        super().__init__(image_path)

    def similar(self, value: float):
        self._call("similar", float(value)); return self
    def exact(self):
        self._call("exact"); return self
    def targetOffset(self, x: int, y: int):
        self._call("targetOffset", int(x), int(y)); return self


class Match(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.Match"


class App(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.App"

    def __init__(self, name: str):
        super().__init__(name)

    @staticmethod
    def open(path: str) -> "App":
        result = default_bridge().call_static(App.JAVA_CLASS, "open", [path])
        wrapped = App.__new__(App)
        wrapped._remote = result
        return wrapped

    def focus(self): return self._call("focus")
    def close(self): return self._call("close")
    def window(self): return self._call("window")


class VNCScreen(_OculixClass):
    JAVA_CLASS = "org.sikuli.vnc.VNCScreen"

    @staticmethod
    def start(host: str, port: int, password: str, width: int, height: int):
        result = default_bridge().call_static(
            VNCScreen.JAVA_CLASS, "start",
            [host, int(port), password, int(width), int(height)],
        )
        wrapped = VNCScreen.__new__(VNCScreen)
        wrapped._remote = result
        return wrapped

    def click(self, target): return self._call("click", target)
    def type(self, text):    return self._call("type", text)
    def stop(self):          return self._call("stop")


class ADBScreen(_OculixClass):
    """ADBScreen lives in ``org.sikuli.android`` in OculiX 3.x."""
    JAVA_CLASS = "org.sikuli.android.ADBScreen"

    @staticmethod
    def start(adb_path: str = ""):
        args = [adb_path] if adb_path else []
        result = default_bridge().call_static(ADBScreen.JAVA_CLASS, "start", args)
        wrapped = ADBScreen.__new__(ADBScreen)
        wrapped._remote = result
        return wrapped

    def click(self, target): return self._call("click", target)
    def type(self, text):    return self._call("type", text)


class SSHTunnel(_OculixClass):
    """``com.sikulix.util.SSHTunnel`` — embedded jcraft/jsch."""
    JAVA_CLASS = "com.sikulix.util.SSHTunnel"

    def __init__(self, user: str, host: str, port: int, password: str):
        super().__init__(user, host, int(port), password)

    def open(self, local_port: int, remote_host: str, remote_port: int):
        return self._call("open", int(local_port), remote_host, int(remote_port))

    def close(self): return self._call("close")


class PaddleOCREngine(_OculixClass):
    """OculiX's neural OCR engine — ``com.sikulix.ocr.PaddleOCREngine``."""
    JAVA_CLASS = "com.sikulix.ocr.PaddleOCREngine"

    @staticmethod
    def getInstance():
        result = default_bridge().call_static(PaddleOCREngine.JAVA_CLASS, "getInstance", [])
        wrapped = PaddleOCREngine.__new__(PaddleOCREngine)
        wrapped._remote = result
        return wrapped


class OCR(_OculixClass):
    """Tesseract-based OCR (``org.sikuli.script.OCR``)."""
    JAVA_CLASS = "org.sikuli.script.OCR"


class _StaticConstants:
    """Lazy proxy that forwards attribute lookups to a Java class's static fields."""
    def __init__(self, java_class: str):
        self._java_class = java_class
        self._cache = {}

    def __getattr__(self, name: str):
        if name in self._cache:
            return self._cache[name]
        bridge = default_bridge()
        # Static fields aren't exposed by our minimal RPC yet — fall back to
        # invoking the JVM's reflection via a Class.getField roundtrip.
        cls_obj = bridge.call_static("java.lang.Class", "forName", [self._java_class])
        field = bridge.call(cls_obj._ref, "getField", [name])
        value = bridge.call(field._ref, "get", [None])
        self._cache[name] = value
        return value


Key = _StaticConstants("org.sikuli.script.Key")
Settings = _StaticConstants("org.sikuli.basics.Settings")
