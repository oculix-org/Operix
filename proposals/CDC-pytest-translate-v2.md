# CDC — pytest-translate v2
## Internationalisation complete de la sortie pytest, 100% offline

**Auteur :** Julien Mer — JMer Consulting
**Date :** 19 avril 2026
**Statut :** A implementer
**Repo cible :** julienmerconsulting/pytest-translate
**PyPI :** pytest-translate (version 2.0.0)

---

## 1. Probleme

La v1 (1.0.1) ne traduit que **9 chaines** (`PASSED`, `FAILED`, `SKIPPED`, et
la ligne de resume final) via un appel **Google Translate runtime**. Cela
pose 4 problemes :

| Probleme | Impact |
|---|---|
| Dependance reseau obligatoire | casse en CI offline, proxy d'entreprise, air-gapped |
| **Privacy** : noms de tests + textes envoyes a Google | interdit en contexte sensible (sante, defense, banque) |
| Latence 100-500 ms par chaine non cachee au 1er run | visible pour les tests courts |
| Surface de traduction minuscule | l'utilisateur voit quand meme 95% de la sortie en anglais |

Resultat : le plugin est une curiosite, pas un outil professionnel.

## 2. Solution v2

Un plugin **100% offline** qui intercepte **toute la sortie pytest** via
monkey-patching de `_pytest._io.terminalwriter.TerminalWriter` et applique
une table de traductions livree dans le package.

```bash
pip install pytest-translate      # aucune dep externe au-dela de pytest + gettext (stdlib)
PYTEST_LANG=fr_FR pytest          # ou detection auto via locale
```

Zero appel reseau a l'execution, 134 langues maintenues via PR communautaires.

## 3. Architecture

```
+--------------------------+
|   pytest runtime         |
|   (7.x / 8.x / 9.x)      |
+-----------+--------------+
            |
            v
+--------------------------+
|  TerminalWriter          |
|  (write / sep / line)    |
+-----------+--------------+
            |
            | monkey-patched par pytest_translate
            v
+--------------------------+     +---------------------------+
|  _translate_chunk(msg)   |<----|  locale/{lang}/           |
|    EXACT dict lookup     |     |    LC_MESSAGES/           |
|    REGEX patterns        |     |      pytest_translate.mo  |
+-----------+--------------+     +---------------------------+
            |
            v
+--------------------------+
|  terminal (utf-8)        |
+--------------------------+
```

## 4. Structure du repo

```
pytest-translate/
|-- README.md
|-- LICENSE (MIT)
|-- pyproject.toml
|-- src/
|   +-- pytest_translate/
|       |-- __init__.py
|       |-- plugin.py              # hooks pytest + monkey-patch TerminalWriter
|       |-- patterns.py            # table EXACT + REGEX des strings pytest
|       |-- resolver.py            # resolution de la langue (CLI > env > locale)
|       |-- _version.py
|       +-- locale/
|           |-- fr/LC_MESSAGES/pytest_translate.{po,mo}
|           |-- de/LC_MESSAGES/pytest_translate.{po,mo}
|           |-- es/LC_MESSAGES/pytest_translate.{po,mo}
|           |-- zh_CN/LC_MESSAGES/pytest_translate.{po,mo}
|           |-- ja/LC_MESSAGES/pytest_translate.{po,mo}
|           +-- ... (134 langues)
|-- tests/
|   |-- test_plugin.py             # scenarii pytest
|   |-- test_patterns.py           # unit tests sur les substitutions
|   |-- test_resolver.py
|   +-- fixtures/                  # tests mock pour les sorties pytest
|-- scripts/
|   |-- bootstrap_translations.py  # MT Google one-shot pour generer les .po
|   |-- compile_mo.py              # msgfmt pour generer les .mo a partir des .po
|   +-- extract_strings.py         # extrait les strings a traduire depuis patterns.py
+-- CONTRIBUTING.md                # guide pour PR de correction de traduction
```

## 5. Composants detailles

### 5.1 patterns.py — la table des traductions

```python
"""
Toutes les strings que pytest emet, regroupees en 2 tables :
- EXACT : substitution directe (si la string apparait telle quelle)
- REGEX : patterns avec groupes de capture pour les formats dynamiques
"""

# Strings exactes (headers, labels, status)
EXACT_STRINGS = [
    "test session starts",
    "ERRORS",
    "FAILURES",
    "PASSES",
    "WARNINGS",
    "short test summary info",
    "warnings summary",
    "slowest durations",
    "no tests ran",
    "PASSED", "FAILED", "SKIPPED", "ERROR", "XFAIL", "XPASS",
    "passed", "failed", "error", "skipped", "xfailed", "xpassed",
    "no tests collected",
    "fixtures used in",
]

# Patterns regex avec capture de valeurs dynamiques
REGEX_PATTERNS = [
    # nombre d'items collectes
    (r"\bcollected (\d+) items?\b",                 "items_collected"),
    (r"\bcollected (\d+) items? / (\d+) errors?\b", "items_errors"),
    # informations session
    (r"^rootdir: (.+)$",                             "rootdir"),
    (r"^plugins: (.+)$",                             "plugins"),
    (r"^configfile: (.+)$",                          "configfile"),
    (r"^testpaths: (.+)$",                           "testpaths"),
    # resume
    (r"\bin ([\d.]+)s\b",                            "in_duration"),
    (r"(\d+) (passed|failed|error|skipped|warning|xfailed|xpassed)",
                                                     "count_status"),
    # marqueurs
    (r"\[\s*(\d+)%\s*\]",                            "percent"),
]
```

### 5.2 plugin.py — hooks et monkey-patch

```python
import re
import gettext
from pathlib import Path
from _pytest._io.terminalwriter import TerminalWriter

from .patterns import EXACT_STRINGS, REGEX_PATTERNS
from .resolver import resolve_lang

_translator = None
_compiled_patterns = []


def pytest_addoption(parser):
    parser.addoption(
        "--translate-lang",
        action="store",
        default=None,
        metavar="LANG",
        help="Langue cible (ex: fr_FR, zh_CN, ja). auto = locale OS. off = desactive.",
    )


def pytest_configure(config):
    global _translator, _compiled_patterns

    lang = resolve_lang(config)
    if lang is None:
        return

    # Charge la table de traduction via gettext
    locale_dir = Path(__file__).parent / "locale"
    try:
        _translator = gettext.translation(
            "pytest_translate", locale_dir, languages=[lang], fallback=False,
        )
    except FileNotFoundError:
        # Langue non livree dans le package — fallback silencieux
        return

    _compiled_patterns = [
        (re.compile(pattern), template_id)
        for pattern, template_id in REGEX_PATTERNS
    ]

    _install_terminal_patch()


def _t(text):
    return _translator.gettext(text) if _translator else text


def _translate_chunk(msg):
    if not isinstance(msg, str) or not _translator:
        return msg

    # 1. Substitutions exactes
    for s in EXACT_STRINGS:
        if s in msg:
            msg = msg.replace(s, _t(s))

    # 2. Patterns regex (format strings avec groupes)
    for pattern, template_id in _compiled_patterns:
        template = _t(template_id)         # ex: "items_collected" -> "{0} elements collectes"
        msg = pattern.sub(lambda m: template.format(*m.groups()), msg)

    return msg


def _install_terminal_patch():
    original_write = TerminalWriter.write
    original_sep = TerminalWriter.sep
    original_line = TerminalWriter.line

    def translated_write(self, msg, **kw):
        return original_write(self, _translate_chunk(msg), **kw)

    def translated_sep(self, sepchar, title=None, **kw):
        if title:
            title = _translate_chunk(title)
        return original_sep(self, sepchar, title, **kw)

    def translated_line(self, msg="", **kw):
        return original_line(self, _translate_chunk(msg), **kw)

    TerminalWriter.write = translated_write
    TerminalWriter.sep = translated_sep
    TerminalWriter.line = translated_line
```

### 5.3 resolver.py — priorites de resolution langue

```python
import os
import locale as _locale


PRIORITY = [
    "cli",          # --translate-lang=fr_FR
    "env_pytest",   # PYTEST_LANG=fr_FR
    "env_lc_all",   # LC_ALL=fr_FR.UTF-8
    "env_lang",     # LANG=fr_FR.UTF-8
    "locale",       # locale.getlocale()
]


def resolve_lang(config):
    """Retourne le code ISO (ex: 'fr') ou None pour desactiver."""
    val = None

    try:
        val = config.getoption("--translate-lang", default=None, skip=True)
    except Exception:
        pass

    if val == "off":
        return None
    if val is None:
        val = os.getenv("PYTEST_LANG")
    if val is None or val == "auto":
        val = os.getenv("LC_ALL") or os.getenv("LANG")
    if val is None or val == "auto":
        try:
            val = _locale.getlocale()[0]
        except Exception:
            val = None

    if not val or val.startswith("en") or val.lower() == "c":
        return None

    # Normalise (fr_FR.UTF-8 -> fr_FR, fr-fr -> fr_FR)
    code = val.split(".")[0].replace("-", "_")
    return code
```

### 5.4 scripts/bootstrap_translations.py — MT Google one-shot

```python
"""
Script de bootstrap a lancer UNE SEULE FOIS chez le mainteneur.
Genere les .po pour les 134 langues supportees via Google Translate.
Necessite: pip install deep-translator polib

Ensuite : python scripts/compile_mo.py  # genere les .mo binaires
"""

from deep_translator import GoogleTranslator
from pathlib import Path
import polib

from pytest_translate.patterns import EXACT_STRINGS, REGEX_PATTERNS

# Les 134 langues supportees par Google Translate
LANGS = ["fr", "de", "es", "zh-CN", "zh-TW", "ja", "ko", "ar", "hi",
         "pt", "ru", "it", "nl", "pl", "tr", "sv", "da", "fi", "no",
         ...]  # liste complete dans le repo

# Templates pour les patterns (a traduire avec placeholders {0}, {1})
TEMPLATES = {
    "items_collected": "{0} items collected",
    "items_errors":    "{0} items collected / {1} errors",
    "rootdir":         "rootdir: {0}",
    "plugins":         "plugins: {0}",
    "in_duration":     "in {0}s",
    "count_status":    "{0} {1}",
    # ...
}


def bootstrap(lang_code):
    tr = GoogleTranslator(source="en", target=lang_code)
    po = polib.POFile()
    po.metadata = {
        "Content-Type": "text/plain; charset=UTF-8",
        "Language": lang_code,
    }

    for s in EXACT_STRINGS + list(TEMPLATES):
        source = TEMPLATES.get(s, s)
        try:
            translated = tr.translate(source)
        except Exception as e:
            print(f"[{lang_code}] failed for {s!r}: {e}")
            translated = source
        entry = polib.POEntry(msgid=s, msgstr=translated)
        po.append(entry)

    out = Path(f"src/pytest_translate/locale/{lang_code}/LC_MESSAGES/pytest_translate.po")
    out.parent.mkdir(parents=True, exist_ok=True)
    po.save(out)
    print(f"[{lang_code}] wrote {out}")


if __name__ == "__main__":
    for lang in LANGS:
        bootstrap(lang)
```

### 5.5 CONTRIBUTING.md — modele de PR communautaire

Guide court expliquant comment corriger une traduction :

1. Ouvrir `src/pytest_translate/locale/<lang>/LC_MESSAGES/pytest_translate.po`
2. Modifier les `msgstr` voulus
3. Regenerer le `.mo` : `python scripts/compile_mo.py`
4. PR avec le diff

## 6. Exemples d'usage

### 6.1 Auto-detection locale

```bash
$ LANG=fr_FR.UTF-8 pytest -v
========== la session de tests demarre ==========
platform linux ...
racine: /home/jmer/projet
extensions: translate-2.0.0
5 elements collectes

tests/test_login.py::test_auth PASSE        [ 20%]
tests/test_login.py::test_lock ECHOUE       [ 40%]
...
========== 4 reussi, 1 echoue en 1.24s ==========
```

### 6.2 Force une langue via CLI

```bash
pytest --translate-lang=zh_CN   # chinois simplifie
pytest --translate-lang=ja      # japonais
pytest --translate-lang=off     # desactive
```

### 6.3 Desactivation via env var

```bash
PYTEST_LANG=off pytest          # utile en CI pour des logs standards
```

## 7. Tests de regression

### 7.1 test_patterns.py

```python
from pytest_translate.plugin import _translate_chunk

def test_exact_substitution_passed():
    # Avec traducteur actif en francais
    _activate("fr")
    assert "PASSE" in _translate_chunk("test_foo PASSED [10%]")

def test_regex_preserves_numbers():
    _activate("fr")
    out = _translate_chunk("collected 42 items")
    assert "42" in out
    assert "collectes" in out  # traduction francaise

def test_fallback_when_lang_unsupported():
    _activate("xx_XX")  # langue inexistante
    assert _translate_chunk("PASSED") == "PASSED"  # passe-plat

def test_no_translation_in_test_names():
    # PASSED dans un nom de test ne doit pas etre traduit (word boundary)
    _activate("fr")
    out = _translate_chunk("test_password PASSED")
    assert "test_password" in out       # intouche
    assert "test_mot_de_passe" not in out
```

### 7.2 test_plugin.py (integration)

```python
# Utilise pytester (le framework de test pour pytest lui-meme)
def test_full_session_translated(pytester):
    pytester.makepyfile("""
        def test_one(): assert True
        def test_two(): assert False
    """)
    result = pytester.runpytest("--translate-lang=fr_FR", "-v")
    assert "PASSE" in result.stdout.str()
    assert "ECHOUE" in result.stdout.str()
    assert "reussi" in result.stdout.str()
    assert "echoue" in result.stdout.str()
```

## 8. Risques et mitigations

| Risque | Mitigation |
|---|---|
| API `TerminalWriter` change entre pytest 7/8/9 | Tests CI sur les 3 majeures (matrix `pytest==7.4`, `8.3`, `9.0`). Tolerer les signatures differentes via `**kw` |
| Replacement casse un nom de test (ex: `test_password`) | Utiliser `\b` word boundaries dans les regex. Ne JAMAIS substituer dans les lignes qui contiennent `::` (notation test) |
| `msgfmt` pas installe chez le contributeur | Fournir `compile_mo.py` pur Python via `babel.messages.mofile.write_mo` |
| Traduction Google bootstrap de mauvaise qualite | Accepter — c'est bootstrap, la communaute corrige |
| Conflit avec autres plugins qui patchent TerminalWriter (ex: pytest-html) | Tester avec les top-20 plugins. Chaine ordonne (nos patches en dernier) |
| Caracteres non-UTF8 dans anciens terminaux Windows | Force `sys.stdout.reconfigure(encoding='utf-8')` au configure |
| Performance : regex sur chaque chunk | Cache LRU sur les chunks deja traduits. Skip si langue = None |

## 9. Roadmap

| Phase | Contenu | Duree |
|---|---|---|
| Phase 1 | `resolver.py` + `plugin.py` minimal (hooks + patch TerminalWriter) | 1 jour |
| Phase 2 | `patterns.py` — table exhaustive des 50 strings pytest communs | 1 jour |
| Phase 3 | Bootstrap Google pour les 10 langues majeures (fr, de, es, zh, ja, ko, ar, pt, ru, it) | 0.5 jour |
| Phase 4 | Bootstrap Google pour les 124 restantes | 0.5 jour |
| Phase 5 | Tests regression sur pytest 7/8/9 + top-10 plugins | 1 jour |
| Phase 6 | CI GitHub Actions matrix + publish PyPI via OIDC Trusted Publisher | 0.5 jour |
| Phase 7 | README + CONTRIBUTING + migration guide 1.x -> 2.x | 0.5 jour |

**Total : ~5 jours**

## 10. Compatibilite ascendante

La v2 **remplace** la v1 mais garde :
- La meme CLI option `--translate-lang`
- La meme env var `PYTEST_LANG`
- Les memes 9 strings traduites (et beaucoup plus)

Les utilisateurs v1 qui mettent a jour voient **plus** de choses traduites,
pas moins. Pas de breaking change.

## 11. Dependances

| Runtime | Stdlib uniquement (`gettext`, `re`, `locale`) + `pytest>=7.0` |
|---|---|
| Bootstrap (dev only) | `deep-translator`, `polib`, `babel` (pas livres aux users) |

Zero dep tierce au runtime = installation ultra-rapide, surface d'attaque nulle.

---

*"Ecrire vos scripts de test en francais, lire vos rapports en francais.*
*Zero cloud, zero latence, zero fuite de nom de test."*
