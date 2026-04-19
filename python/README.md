# oculix (Python)

Python wrapper for [OculiX](https://github.com/oculix-org/Oculix) — visual automation for the real world.

## Install

```bash
pip install oculix
```

Requirements: Python 3.8+, Java 11+ on `PATH` (Eclipse Temurin or Azul Zulu).

The OculiX engine (~160 MB fat JAR) is downloaded on first use into `~/.oculix/lib/`.

## Quickstart

```python
from oculix import Screen, App, VNCScreen

screen = Screen()
screen.click("login.png")
screen.type("admin")
screen.click("submit.png")
screen.wait("dashboard.png", 10)

# Open a desktop app
calc = App.open("calc")
screen.click("button_7.png")

# Drive a remote VNC display
vnc = VNCScreen.start("10.0.0.42", 5900, "", 1920, 1080)
vnc.click("logo.png")
vnc.stop()
```

## With pytest

```python
import pytest
from oculix import Screen, App

@pytest.fixture
def calc():
    a = App.open("calculator")
    yield a
    a.close()

def test_addition(calc):
    s = Screen()
    s.click("button_7.png")
    s.click("button_plus.png")
    s.click("button_3.png")
    s.click("button_equals.png")
    assert s.exists("result_10.png")
```

## License

MIT
