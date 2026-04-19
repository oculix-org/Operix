# CDC — Operix (Python)
## Python wrapper for OculiX via Py4J

**Auteur :** Julien Mer — JMer Consulting
**Date :** 19 avril 2026
**Statut :** A implementer
**Repo cible :** oculix-org/operix-python
**PyPI :** oculix
**Dependance :** `io.github.oculix-org:oculixapi:3.0.2` (Maven Central)
**Prerequis :** Java 11+ (Eclipse Temurin / Azul Zulu recommandes)

---

## 1. Probleme

OculiX est un JAR Java. Pour l'utiliser, il faut soit :
- Un projet Java/Maven (devs Java uniquement)
- L'IDE OculiX avec Jython (limite, Python 2.7)
- La ligne de commande `-r script.py` (pas integrable dans un workflow Python)

Les devs Python, les data engineers, les QA qui utilisent pytest/Robot Framework
ne peuvent pas utiliser OculiX nativement.

## 2. Solution

Un package PyPI `oculix` qui expose toute l'API OculiX en Python natif via Py4J.

```bash
pip install oculix
```

```python
from oculix import Screen, App, Pattern

screen = Screen()
screen.click("button.png")
app = App.open("notepad")
```

## 3. Architecture

```
+---------------------+         +---------------------+
|   Python process    |  Py4J   |   JVM process       |
|                     | socket  |                     |
|   from oculix ...   |<------->|   oculixapi.jar     |
|   screen.click()    |         |   + py4j JAR        |
|                     |         |   (py4j.GatewayServer)|
+---------------------+         +---------------------+
```

Aucun code Java custom cote Operix : on s'appuie sur `py4j.GatewayServer`
(classe `main` fournie par py4j) pour exposer la JVM, et le repo Oculix
est utilise tel quel sans modification.

1. Au premier `import oculix`, le package :
   - Verifie si Java est installe (`java -version`)
   - Verifie si `oculixapi-3.0.2.jar` est present dans `~/.oculix/lib/`
   - Si absent, le telecharge depuis Maven Central
   - Localise le `py4jX.Y.jar` embarque par le package Python `py4j`
   - Demarre une JVM avec les deux JAR sur le classpath et lance
     `py4j.GatewayServer` (utilise `py4j.launch_gateway()`)
   - Connecte le client Python au GatewayServer (port choisi par py4j)
   - Charge OpenCV (Apertix) via `org.sikuli.support.Commons.loadOpenCV()`

2. Les appels Python sont traduits en appels Java via Py4J (transparent)

3. A la fin du script (ou `atexit`), la JVM est arretee proprement

## 4. Structure du repo

```
oculix-org/operix-python/
|-- README.md
|-- LICENSE (MIT)
|-- pyproject.toml
|-- setup.cfg
|-- src/
|   +-- oculix/
|       |-- __init__.py          # import principal, auto-start JVM
|       |-- gateway.py           # gestion JVM lifecycle (start/stop/download)
|       |-- screen.py            # wrapper Screen
|       |-- region.py            # wrapper Region
|       |-- pattern.py           # wrapper Pattern
|       |-- app.py               # wrapper App
|       |-- vnc.py               # wrapper VNCScreen, SSHTunnel
|       |-- adb.py               # wrapper ADBScreen
|       |-- ocr.py               # wrapper OCR, PaddleOCREngine, PaddleOCRClient
|       |-- keys.py              # constantes Key, KeyModifier
|       +-- _version.py          # version du package
|-- tests/
|   |-- test_gateway.py
|   |-- test_screen.py
|   |-- test_pattern.py
|   +-- test_app.py
+-- scripts/
    +-- download_jar.py          # telecharge oculixapi depuis Maven Central
```

**Note :** pas de dossier `java/` car aucun code Java custom n'est necessaire.
On utilise `py4j.GatewayServer` (fourni par le package PyPI `py4j`) pour
exposer la JVM, et `oculixapi.jar` est utilise tel quel sans modification.

## 5. Composants detailles

### 5.1 gateway.py — JVM lifecycle

```python
import atexit
import os
from py4j.java_gateway import JavaGateway, GatewayParameters, launch_gateway

_gateway = None
_port = None

JAR_DIR = os.path.expanduser("~/.oculix/lib")
JAR_NAME = "oculixapi-3.0.2.jar"
JAR_URL = "https://repo1.maven.org/maven2/io/github/oculix-org/oculixapi/3.0.2/oculixapi-3.0.2.jar"

def _ensure_jar():
    jar_path = os.path.join(JAR_DIR, JAR_NAME)
    if not os.path.exists(jar_path):
        os.makedirs(JAR_DIR, exist_ok=True)
        import urllib.request
        print(f"[OculiX] Downloading {JAR_NAME}...")
        urllib.request.urlretrieve(JAR_URL, jar_path)
        print(f"[OculiX] Saved to {jar_path}")
    return jar_path

def start():
    global _gateway, _port
    if _gateway is not None:
        return _gateway

    jar_path = _ensure_jar()
    # py4j launches a JVM running py4j.GatewayServer with the given classpath
    # and returns the dynamically chosen port (avoids the fixed-25333 conflict).
    _port = launch_gateway(classpath=jar_path, die_on_exit=True)
    _gateway = JavaGateway(gateway_parameters=GatewayParameters(port=_port))

    # Apertix OpenCV bundled in oculixapi must be loaded explicitly
    _gateway.jvm.org.sikuli.support.Commons.loadOpenCV()

    atexit.register(stop)
    return _gateway

def stop():
    global _gateway
    if _gateway:
        _gateway.shutdown()
        _gateway = None

def jvm():
    if _gateway is None:
        start()
    return _gateway.jvm
```

### 5.2 __init__.py — API publique

```python
from oculix.gateway import jvm, start, stop

def __getattr__(name):
    _jvm = jvm()
    _classes = {
        # org.sikuli.script
        "Screen": _jvm.org.sikuli.script.Screen,
        "Region": _jvm.org.sikuli.script.Region,
        "Pattern": _jvm.org.sikuli.script.Pattern,
        "Match": _jvm.org.sikuli.script.Match,
        "App": _jvm.org.sikuli.script.App,
        "Key": _jvm.org.sikuli.script.Key,
        "KeyModifier": _jvm.org.sikuli.script.KeyModifier,
        "OCR": _jvm.org.sikuli.script.OCR,
        "FindFailed": _jvm.org.sikuli.script.FindFailed,
        "Image": _jvm.org.sikuli.script.Image,
        "Location": _jvm.org.sikuli.script.Location,
        # org.sikuli.vnc
        "VNCScreen": _jvm.org.sikuli.vnc.VNCScreen,
        # org.sikuli.android (NOT org.sikuli.script)
        "ADBScreen": _jvm.org.sikuli.android.ADBScreen,
        # org.sikuli.basics
        "Settings": _jvm.org.sikuli.basics.Settings,
        # com.sikulix.util
        "SSHTunnel": _jvm.com.sikulix.util.SSHTunnel,
        # com.sikulix.ocr (Oculix's PaddleOCR engine)
        "PaddleOCREngine": _jvm.com.sikulix.ocr.PaddleOCREngine,
        "PaddleOCRClient": _jvm.com.sikulix.ocr.PaddleOCRClient,
        "TesseractEngine": _jvm.com.sikulix.ocr.TesseractEngine,
    }
    if name in _classes:
        return _classes[name]
    raise AttributeError(f"module 'oculix' has no attribute '{name}'")
```

### 5.3 Pas de classe Java custom

L'architecture s'appuie uniquement sur :
- `py4j.GatewayServer` (livre par le package PyPI `py4j`, lance via
  `py4j.java_gateway.launch_gateway()`)
- `oculixapi.jar` 3.0.2 utilise tel quel depuis Maven Central

`Commons.loadOpenCV()` est appele cote Python apres connexion au gateway,
donc aucune modification d'Oculix n'est necessaire.

## 6. Exemples d'usage

### 6.1 Script basique

```python
from oculix import Screen, Key

screen = Screen()
screen.click("login_button.png")
screen.type("admin")
screen.type(Key.TAB)
screen.type("password123")
screen.type(Key.ENTER)
screen.wait("dashboard.png", 10)
```

### 6.2 VNC remote

```python
from oculix import VNCScreen, SSHTunnel

tunnel = SSHTunnel("root", "10.184.10.147", 22, "password")
tunnel.open(5900, "localhost", 5900)

vnc = VNCScreen.start("localhost", 5900, "", 1920, 1080)
vnc.click("auchan_logo.png")
vnc.type("1234")
vnc.stop()
```

### 6.3 Android ADB

```python
from oculix import ADBScreen, Pattern

# ADBScreen lives in org.sikuli.android in Oculix 3.x
adb = ADBScreen.start("/usr/local/bin/adb")
adb.click(Pattern("accept_button.png").similar(0.7))
```

### 6.5 PaddleOCR (text find/click)

```python
from oculix import Screen, PaddleOCREngine

screen = Screen()
# Use the neural OCR engine bundled with Oculix instead of Tesseract
ocr = PaddleOCREngine.getInstance()
match = screen.findText("Submit", ocr)
match.click()
```

### 6.4 Avec pytest

```python
import pytest
from oculix import Screen, App

@pytest.fixture
def app():
    a = App.open("calculator")
    yield a
    a.close()

def test_addition(app):
    screen = Screen()
    screen.click("button_7.png")
    screen.click("button_plus.png")
    screen.click("button_3.png")
    screen.click("button_equals.png")
    assert screen.exists("result_10.png")
```

## 7. pyproject.toml

```toml
[build-system]
requires = ["setuptools>=68.0", "wheel"]
build-backend = "setuptools.build_meta"

[project]
name = "oculix"
version = "0.1.0"
description = "Visual automation for the real world - Python wrapper for OculiX"
readme = "README.md"
license = {text = "MIT"}
requires-python = ">=3.8"
authors = [{name = "Julien Mer", email = "julien.mer38@gmail.com"}]
keywords = ["visual-testing", "automation", "ocr", "sikuli", "gui-testing"]
dependencies = ["py4j>=0.10.9"]

[project.urls]
Homepage = "https://github.com/oculix-org/operix"
Repository = "https://github.com/oculix-org/operix"
```

## 8. Risques et mitigations

| Risque | Mitigation |
|---|---|
| Java 11+ pas installe | Message clair + lien download Eclipse Temurin au premier import |
| JAR download echoue | Cache local, retry, message d'erreur avec URL manuelle |
| Port py4j deja pris | `launch_gateway()` choisit un port libre dynamiquement |
| Py4J latence | Negligeable pour visual testing (actions = secondes). PaddleOCR retourne des JSON volumineux : a benchmarker |
| Apertix OpenCV natifs | OpenCV 4.10.0 bundle dans le JAR, charge via `Commons.loadOpenCV()`. A tester sur Win/Mac M1/Linux |
| JVM qui crash | atexit cleanup + `die_on_exit=True` (kill JVM si Python meurt) |

## 9. Roadmap

| Phase | Contenu | Duree |
|---|---|---|
| Phase 1 | Gateway + Screen + Pattern + App + Key | 2 jours |
| Phase 2 | VNCScreen + ADBScreen + SSHTunnel | 1 jour |
| Phase 3 | OCR + Settings + pytest examples | 1 jour |
| Phase 4 | Robot Framework library wrapper | 1 jour |
| Phase 5 | PyPI publication + README + docs | 1 jour |
| Phase 6 | CI (GitHub Actions: test on Win/Mac/Linux) | 1 jour |

**Total : ~1 semaine**

---

*"pip install oculix. That's it. Visual automation in Python, powered by 15 years of SikuliX."*
