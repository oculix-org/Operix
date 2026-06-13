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
# Each class is a one-liner that delegates everything to the bridge. Method
# names track the Java side (camelCase) so users can cross-reference the
# Sikuli/OculiX docs without translation.

class _OculixClass:
    """Base for explicit class wrappers — gives autocomplete and type hints."""

    JAVA_CLASS: str = ""

    def __init__(self, *args):
        self._remote = default_bridge().create(self.JAVA_CLASS, list(args))

    def _call(self, method, *args):
        return self._remote._call(method, *args)

    @classmethod
    def _wrap(cls, remote: RemoteObject) -> "_OculixClass":
        """Build an instance directly from an existing RemoteObject (no JVM ctor call)."""
        instance = cls.__new__(cls)
        instance._remote = remote
        return instance


# --- Region: geometry + mouse + keyboard + search ---------------------------

class Region(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.Region"

    def __init__(self, x: int, y: int, w: int, h: int):
        super().__init__(int(x), int(y), int(w), int(h))

    # mouse
    def click(self, target=None):       return self._call("click", target) if target else self._call("click")
    def doubleClick(self, target=None): return self._call("doubleClick", target) if target else self._call("doubleClick")
    def rightClick(self, target=None):  return self._call("rightClick", target) if target else self._call("rightClick")
    def hover(self, target=None):       return self._call("hover", target) if target else self._call("hover")
    def dragDrop(self, src, dst):       return self._call("dragDrop", src, dst)
    def mouseMove(self, target=None):   return self._call("mouseMove", target) if target else self._call("mouseMove")
    def mouseDown(self, button: int = 1): return self._call("mouseDown", int(button))
    def mouseUp(self, button: int = 0):   return self._call("mouseUp", int(button))

    # keyboard
    def type(self, text):    return self._call("type", text)
    def paste(self, text):   return self._call("paste", text)
    def write(self, text):   return self._call("write", text)
    def keyDown(self, keys): return self._call("keyDown", keys)
    def keyUp(self, keys=None): return self._call("keyUp", keys) if keys is not None else self._call("keyUp")

    # search
    def find(self, target):  return self._call("find", target)
    def findAll(self, target): return self._call("findAll", target)
    def wait(self, target, timeout: float = 10.0): return self._call("wait", target, float(timeout))
    def waitVanish(self, target, timeout: float = 10.0): return self._call("waitVanish", target, float(timeout))
    def exists(self, target, timeout: float = 3.0):
        return self._call("exists", target, float(timeout)) is not None
    def getLastMatch(self): return self._call("getLastMatch")

    # OCR
    def text(self):      return self._call("text")
    def textLines(self): return self._call("textLines")
    def textWords(self): return self._call("textWords")

    # geometry — getters
    def getX(self): return self._call("getX")
    def getY(self): return self._call("getY")
    def getW(self): return self._call("getW")
    def getH(self): return self._call("getH")

    # geometry — setters / movement
    def setX(self, x: int): return self._call("setX", int(x))
    def setY(self, y: int): return self._call("setY", int(y))
    def setW(self, w: int): return self._call("setW", int(w))
    def setH(self, h: int): return self._call("setH", int(h))
    def moveTo(self, x: int, y: int): return self._call("moveTo", int(x), int(y))
    def setROI(self, x: int, y: int, w: int, h: int):
        return self._call("setROI", int(x), int(y), int(w), int(h))

    # spatial — return new regions
    def nearby(self, range_px: int = 50): return self._call("nearby", int(range_px))
    def above(self, range_px: int = 0):   return self._call("above", int(range_px)) if range_px else self._call("above")
    def below(self, range_px: int = 0):   return self._call("below", int(range_px)) if range_px else self._call("below")
    def left(self, range_px: int = 0):    return self._call("left", int(range_px)) if range_px else self._call("left")
    def right(self, range_px: int = 0):   return self._call("right", int(range_px)) if range_px else self._call("right")

    # misc
    def highlight(self, secs: float = 2.0): return self._call("highlight", float(secs))
    def contains(self, other):              return self._call("contains", other)
    def capture(self):                      return self._call("capture")


# --- Screen: a Region tied to a physical display ----------------------------

class Screen(Region):
    """Screen extends Region; every Region method is available."""
    JAVA_CLASS = "org.sikuli.script.Screen"

    def __init__(self, screen_id: int = 0):
        # Skip Region.__init__ — Screen's ctor is (int) or ().
        _OculixClass.__init__(self, int(screen_id))

    @staticmethod
    def getNumberScreens() -> int:
        return default_bridge().call_static(Screen.JAVA_CLASS, "getNumberScreens", [])

    @staticmethod
    def getBounds(screen_id: int = 0):
        return default_bridge().call_static(Screen.JAVA_CLASS, "getBounds", [int(screen_id)])


# --- Pattern ----------------------------------------------------------------

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


# --- Match: a Region augmented with a similarity score ----------------------

class Match(Region):
    JAVA_CLASS = "org.sikuli.script.Match"

    def __init__(self):
        # Match objects are returned by find/wait/exists — never built directly.
        raise TypeError("Match cannot be instantiated; obtain one via find/wait/exists.")

    def getScore(self):  return self._call("getScore")
    def getTarget(self): return self._call("getTarget")
    def getIndex(self):  return self._call("getIndex")


# --- App --------------------------------------------------------------------

class App(_OculixClass):
    JAVA_CLASS = "org.sikuli.script.App"

    def __init__(self, name: str):
        super().__init__(name)

    @staticmethod
    def open(path: str) -> "App":
        result = default_bridge().call_static(App.JAVA_CLASS, "open", [path])
        return App._wrap(result)

    def focus(self):     return self._call("focus")
    def close(self):     return self._call("close")
    def window(self):    return self._call("window")
    def isRunning(self): return self._call("isRunning")
    def hasWindow(self): return self._call("hasWindow")
    def getName(self):   return self._call("getName")
    def getPID(self):    return self._call("getPID")


# --- VNC, ADB, SSH ----------------------------------------------------------

class VNCScreen(_OculixClass):
    JAVA_CLASS = "org.sikuli.vnc.VNCScreen"

    @staticmethod
    def start(host: str, port: int, password: str, width: int, height: int) -> "VNCScreen":
        result = default_bridge().call_static(
            VNCScreen.JAVA_CLASS, "start",
            [host, int(port), password, int(width), int(height)],
        )
        return VNCScreen._wrap(result)

    def click(self, target): return self._call("click", target)
    def type(self, text):    return self._call("type", text)
    def stop(self):          return self._call("stop")


class ADBScreen(_OculixClass):
    """ADBScreen lives in ``org.sikuli.android`` in OculiX 3.x."""
    JAVA_CLASS = "org.sikuli.android.ADBScreen"

    @staticmethod
    def start(adb_path: str = "") -> "ADBScreen":
        args = [adb_path] if adb_path else []
        result = default_bridge().call_static(ADBScreen.JAVA_CLASS, "start", args)
        return ADBScreen._wrap(result)

    def click(self, target): return self._call("click", target)
    def type(self, text):    return self._call("type", text)
    def tap(self, x: int, y: int): return self._call("tap", int(x), int(y))
    def swipe(self, x1: int, y1: int, x2: int, y2: int):
        return self._call("swipe", int(x1), int(y1), int(x2), int(y2))
    def wakeUp(self, secs: int = 1): return self._call("wakeUp", int(secs))


class SSHTunnel(_OculixClass):
    """``com.sikulix.util.SSHTunnel`` — embedded jcraft/jsch."""
    JAVA_CLASS = "com.sikulix.util.SSHTunnel"

    def __init__(self, user: str, host: str, port: int, password: str):
        super().__init__(user, host, int(port), password)

    def open(self, local_port: int, remote_host: str, remote_port: int):
        return self._call("open", int(local_port), remote_host, int(remote_port))

    def close(self): return self._call("close")


# --- OCR engines ------------------------------------------------------------

class PaddleOCREngine(_OculixClass):
    """OculiX's neural OCR engine — ``com.sikulix.ocr.PaddleOCREngine``."""
    JAVA_CLASS = "com.sikulix.ocr.PaddleOCREngine"

    @staticmethod
    def getInstance() -> "PaddleOCREngine":
        result = default_bridge().call_static(PaddleOCREngine.JAVA_CLASS, "getInstance", [])
        return PaddleOCREngine._wrap(result)


class OCR:
    """Tesseract-based OCR (``org.sikuli.script.OCR``) — fully static."""
    JAVA_CLASS = "org.sikuli.script.OCR"

    @staticmethod
    def readText(target):  return default_bridge().call_static(OCR.JAVA_CLASS, "readText", [target])
    @staticmethod
    def readLine(target):  return default_bridge().call_static(OCR.JAVA_CLASS, "readLine", [target])
    @staticmethod
    def readWord(target):  return default_bridge().call_static(OCR.JAVA_CLASS, "readWord", [target])
    @staticmethod
    def readLines(target): return default_bridge().call_static(OCR.JAVA_CLASS, "readLines", [target])
    @staticmethod
    def readWords(target): return default_bridge().call_static(OCR.JAVA_CLASS, "readWords", [target])


# --- static-fields proxy (Key, Settings) ------------------------------------

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

    def __setattr__(self, name: str, value):
        # Internal attrs (_java_class, _cache) stay Python-side.
        if name.startswith("_"):
            object.__setattr__(self, name, value)
            return
        # Public name -> WRITE the Java static field via reflection.
        # e.g. Settings.MoveMouseDelay = 0.0 actually pokes org.sikuli.basics.Settings.
        bridge = default_bridge()
        cls_obj = bridge.call_static("java.lang.Class", "forName", [self._java_class])
        field = bridge.call(cls_obj._ref, "getField", [name])
        bridge.call(field._ref, "set", [None, value])
        self._cache.pop(name, None)   # drop any stale cached read


Key = _StaticConstants("org.sikuli.script.Key")
Settings = _StaticConstants("org.sikuli.basics.Settings")
