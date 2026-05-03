# CDC — Operix-NET (C# / .NET)
## C# wrapper for OculiX via the shared JVM bridge

**Auteur :** Julien Mer — JMer Consulting
**Date :** 19 avril 2026 (re-redige : 3 mai 2026)
**Statut :** Implemente (V1, scaffold)
**Repo :** oculix-org/Operix (monorepo, dossier `dotnet/`)
**NuGet :** OculiX
**Dependance runtime :** `io.github.oculix-org:oculixapi:3.0.3` (Maven Central, via le JAR du bridge)
**Prerequis utilisateur :** .NET 8+, Java 11+ sur le PATH

---

## 1. Probleme

Le monde .NET/C# est enorme en entreprise (banque, assurance, retail, sante).
Les QA .NET utilisent NUnit, xUnit, SpecFlow, MSTest. Pour le visual testing,
ils n'ont que des outils payants (Ranorex, TestComplete, UiPath) ou des
solutions browser-only (Playwright .NET).

Aucun outil open source ne fait du visual testing desktop/VNC/Android en C#.

## 2. Solution

Un package NuGet `OculiX` qui expose toute l'API OculiX en C# idiomatique
(PascalCase, async/await), en deleguant tous les appels a un process JVM
qui execute `oculixapi`.

```bash
dotnet add package OculiX
```

```csharp
using OculiX;

var screen = await Screen.CreateAsync();
await screen.Click("button.png");
var app = await App.Open("notepad");
```

## 3. Architecture — Process bridge JSON-RPC

**IKVM a ete evalue puis abandonne** (voir §3.1). L'architecture retenue est
identique a celle du wrapper Node.js : une JVM enfant tourne en sous-process,
on lui parle en JSON ligne par ligne sur stdin/stdout. Le code Java du
serveur RPC vit dans le meme monorepo (`jvm-bridge/`) et est mutualise entre
les wrappers Node.js et .NET.

```
+---------------------+          +---------------------------+
|   .NET process      |  spawn   |   JVM process             |
|                     | stdin/   |                           |
|   var s = await     |<-------->|   operix-jvm-bridge.jar   |
|   Screen.Create();  | stdout   |   (oculixapi + OpenCV +   |
|   await s.Click(x)  | JSON-RPC |    org.operix.rpc.Server) |
+---------------------+          +---------------------------+
```

**Ce qu'on gagne :**
- Fonctionne **aujourd'hui** avec OculiX 3.x (Java 17 bytecode)
- Code Java mutualise avec `operix-js` (meme `jvm-bridge/` sert Node et .NET)
- Aucune modification d'OculiX
- Isolation : si OculiX crashe, le process .NET survit (on relance la JVM)
- Pas de probleme de natifs : OpenCV Apertix reste dans la JVM, P/Invoke
  jamais necessaire cote .NET
- Le meme JAR sert les wrappers Python, Node.js et .NET (un seul artefact a
  publier sur GitHub Releases par version du bridge)

**Ce qu'on paie :**
- Une JVM a lancer (~200 MB RAM en steady state)
- Serialisation JSON sur chaque appel (~1-5 ms de latence). Negligeable
  pour du visual testing ou les actions sont de l'ordre de la seconde

### 3.1 Pourquoi pas IKVM

Spike effectue le 19 avril 2026 (Linux x64, IKVM 8.15.0 stable dec 2025) :
**IKVM 8.x ne sait pas convertir le bytecode Java 17** (class file major=61)
qui est la cible de build d'oculixapi 3.x. Resultat concret : 0 type .NET
expose, 1085 warnings `class format error "61.0"` lors de `ikvmc`.

A re-evaluer si une version d'IKVM avec support Java 17+ devient stable.
Le code metier des wrappers (`Wrappers.cs`) ne dependant que du contrat
`Bridge.CallAsync(method, args)`, basculer sur IKVM en V2 demanderait
seulement de remplacer `Bridge.cs` par une couche IKVM — `Wrappers.cs`
reste tel quel.

### 3.2 Pourquoi pas Javonet

In-process, supporte Java 17, mais **$69/instance/mois** et licence
commerciale non-redistribuable en MIT. Incompatible avec un NuGet OSS.

---

## 4. Structure du repo (monorepo Operix)

```
oculix-org/Operix/
|-- jvm-bridge/                          # serveur Java JSON-RPC (mutualise Node + .NET)
|   +-- src/main/java/org/operix/rpc/
|       |-- Server.java                  # boucle JSON-RPC sur stdio
|       |-- Dispatcher.java              # reflection + overload resolution
|       +-- ObjectRegistry.java          # __ref handles
|-- dotnet/
|   |-- OculiX.sln
|   |-- src/OculiX/
|   |   |-- OculiX.csproj
|   |   |-- Bridge.cs                    # JSON-RPC client + spawn JVM + JAR autodownload
|   |   +-- Wrappers.cs                  # Screen, Region, Match, Pattern, App, ...
|   +-- tests/OculiX.Tests/
|       +-- BridgeTests.cs               # tests d'integration (JAR requis)
+-- .github/workflows/
    |-- release-jvm-bridge.yml           # build + publish du fat JAR sur GitHub Releases
    +-- release-dotnet.yml               # dotnet pack + push NuGet
```

## 5. Composants implementes

### 5.1 Bridge.cs

JSON-RPC client en async/await pur (pas de blocking, pas de Thread.Sleep).

- `Bridge.Default` : singleton lazy
- `StartAsync()` : `Process.Start("java", "-jar", jar)` + auto-flush stdin
- `CreateAsync(className, args)`, `CallAsync(ref, method, args)`,
  `CallStaticAsync(className, method, args)`, `ReleaseAsync(ref)`
- Boucle de lecture asynchrone (`ReadLineAsync`) qui demultiplexe les
  reponses par `id` vers les `TaskCompletionSource` en attente
- `EnsureJarAsync()` : telecharge le fat JAR depuis GitHub Releases sur le
  premier appel et le cache dans `~/.oculix/lib/`
- Encodage UTF-8 sans BOM cote stdin (le bridge JVM rejette les BOM)

### 5.2 Wrappers.cs

Hierarchie miroir de Sikuli/OculiX :

| Classe C# | Classe Java | Notes |
|---|---|---|
| `OculixClass` | (abstrait) | base, contient `RemoteObject Remote` |
| `Region` | `org.sikuli.script.Region` | factory `FromRect(x,y,w,h)` |
| `Screen` | `org.sikuli.script.Screen` | herite de Region, factory `CreateAsync(int)` |
| `Match` | `org.sikuli.script.Match` | herite de Region, ajoute `GetScore/GetTarget/GetIndex` |
| `Pattern` | `org.sikuli.script.Pattern` | factory `FromImage(string)` |
| `App` | `org.sikuli.script.App` | factories `Create(name)` et `Open(path)` |
| `VNCScreen` | `org.sikuli.vnc.VNCScreen` | factory `Start(...)` |
| `ADBScreen` | `org.sikuli.android.ADBScreen` | factory `Start(...)` (lives in `org.sikuli.android` !) |
| `SSHTunnel` | `com.sikulix.util.SSHTunnel` | factory `Create(...)` (`com.sikulix.util` !) |
| `PaddleOCREngine` | `com.sikulix.ocr.PaddleOCREngine` | factory `GetInstance()` (`com.sikulix.ocr` !) |
| `OCR` | `org.sikuli.script.OCR` | classe statique (Tesseract) |
| `Key`, `Settings` | `org.sikuli.script.Key`, `org.sikuli.basics.Settings` | `Get(name)` async via `Class.getField` |

Surface des methodes (voir `Wrappers.cs` pour la liste complete) :
- Mouse : `Click`, `DoubleClick`, `RightClick`, `Hover`, `DragDrop`,
  `MouseMove`, `MouseDown`, `MouseUp`
- Keyboard : `Type`, `Paste`, `Write`, `KeyDown`, `KeyUp`
- Search : `Find`, `FindAll`, `Wait`, `WaitVanish`, `Exists`, `GetLastMatch`
- OCR : `Text`, `TextLines`, `TextWords`
- Geometry : `GetX/Y/W/H`, `SetX/Y/W/H`, `MoveTo`, `SetROI`, `Nearby`,
  `Above`, `Below`, `Left`, `Right`, `Highlight`, `Contains`, `Capture`

Pour toute classe ou methode OculiX **non explicitement enveloppee**,
l'utilisateur peut toujours descendre d'un cran via le bridge generique :

```csharp
var any = await Bridge.Default.CreateAsync("com.example.NotInWrappers");
var result = await any.CallAsync("someMethod", arg1, arg2);
```

## 6. Exemples d'usage

### 6.1 Script basique

```csharp
using OculiX;

var screen = await Screen.CreateAsync();
await screen.Click("login_button.png");
await screen.Type("admin");
await screen.Type((string)(await Key.Get("TAB"))!);
await screen.Type("password123");
await screen.Click("submit.png");
await screen.Wait("dashboard.png", 10);
```

### 6.2 Avec NUnit

```csharp
using NUnit.Framework;
using OculiX;

[TestFixture]
public class CalculatorTests
{
    private App _app = null!;
    private Screen _screen = null!;

    [SetUp]
    public async Task Setup()
    {
        _app = await App.Open("calc");
        _screen = await Screen.CreateAsync();
    }

    [TearDown]
    public async Task TearDown() => await _app.Close();

    [Test]
    public async Task Addition_7Plus3_Returns10()
    {
        await _screen.Click("button_7.png");
        await _screen.Click("button_plus.png");
        await _screen.Click("button_3.png");
        await _screen.Click("button_equals.png");
        Assert.IsTrue(await _screen.Exists("result_10.png"));
    }
}
```

### 6.3 Avec SpecFlow (BDD)

```gherkin
Feature: POS Login
  Scenario: Cashier logs in successfully
    Given the POS application is open
    When I enter cashier code "1234"
    And I click the login button
    Then I should see the main menu
```

```csharp
[Binding]
public class POSSteps
{
    private Screen _screen = null!;

    [Given("the POS application is open")]
    public async Task GivenPOSOpen()
    {
        var vnc = await VNCScreen.Start("10.184.10.147", 5900, "", 1920, 1080);
        _screen = await Screen.CreateAsync();
    }

    [When("I enter cashier code {string}")]
    public Task WhenEnterCode(string code) => _screen.Type(code);

    [When("I click the login button")]
    public Task WhenClickLogin() => _screen.Click("login_button.png");

    [Then("I should see the main menu")]
    public async Task ThenSeeMainMenu()
        => Assert.IsTrue(await _screen.Exists("main_menu.png", 10));
}
```

### 6.4 VNC remote via tunnel SSH

```csharp
using OculiX;

var tunnel = await SSHTunnel.Create("root", "10.184.10.147", 22, "password");
await tunnel.Open(5900, "localhost", 5900);

var vnc = await VNCScreen.Start("localhost", 5900, "", 1920, 1080);
await vnc.Click("auchan_logo.png");
await vnc.Type("1234");
await vnc.Stop();
```

## 7. OculiX.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <PackageId>OculiX</PackageId>
    <Version>0.1.0</Version>
    <Authors>Julien Mer</Authors>
    <Description>Visual automation for the real world - .NET wrapper for OculiX</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/oculix-org/Operix</Project>
    <RepositoryUrl>https://github.com/oculix-org/Operix</RepositoryUrl>
    <PackageTags>visual-testing;automation;ocr;sikuli;oculix;gui-testing;desktop-testing</PackageTags>
  </PropertyGroup>
  <!-- Pas de PackageReference IKVM, pas de MavenReference -->
  <!-- Le runtime telecharge le JAR du bridge a la demande dans ~/.oculix/lib/ -->
</Project>
```

## 8. Distribution du JAR du bridge

Le NuGet `OculiX` ne contient **pas** le JAR (~160 MB). A la premiere utilisation,
`Bridge.EnsureJarAsync()` :

1. Verifie si `~/.oculix/lib/operix-jvm-bridge-{BridgeVersion}.jar` existe
2. Sinon, telecharge depuis
   `https://github.com/oculix-org/Operix/releases/download/jvm-bridge-{BridgeVersion}/operix-jvm-bridge-{BridgeVersion}.jar`
3. Cache localement, suit les redirects 301/302 (GitHub Releases redirige vers S3)

Le JAR est genere par le workflow `release-jvm-bridge.yml` : `mvn package`
produit un fat JAR shade qui inclut `oculixapi-3.0.3` + Apertix OpenCV
(natifs Win/Mac/Linux x86_64 embarques par Apertix) + `org.json` + le
serveur RPC. Le workflow joint ce JAR a une GitHub Release.

**Couplage de versions :** `BridgeVersion` dans `Bridge.cs` doit matcher la
version du tag de release JVM bridge. La doc de release (`release-jvm-bridge.yml`)
le rappelle au mainteneur.

## 9. Risques et mitigations

| Risque | Mitigation |
|---|---|
| Java absent du PATH chez l'utilisateur | Erreur claire au demarrage de `Bridge`. Documenter Adoptium/Temurin dans le README NuGet |
| JAR du bridge introuvable (404 sur Release) | Les tags du workflow doivent matcher l'URL construite par `Bridge.cs` (`jvm-bridge-X.Y.Z`, sans prefixe `v`) |
| Latence JSON-RPC | ~1-5 ms par appel ; insignifiant face aux secondes d'attente d'une UI |
| Crash de la JVM | `Process.Exited` propage l'erreur a tous les `TaskCompletionSource` en attente. L'app .NET survit |
| Encodage stdin (BOM) | `UTF8Encoding(emitBOM: false)` est explicitement positionne sur `StandardInputEncoding` |
| Concurrence des requetes | Chaque requete recoit un `id` unique (`Interlocked.Increment`), demultiplex par `id` cote `ReadLoopAsync` |
| API naming Java vs C# | Les wrappers traduisent (`type` -> `Type`, `getX` -> `GetX`). Les noms cote bridge restent en Java |
| .NET 8 minimum | Acceptable en 2026, .NET 6 est en fin de vie |

## 10. Roadmap

| Phase | Contenu | Statut |
|---|---|---|
| Phase 0 | Spike IKVM + decision archi | ✅ fait (19 avril 2026, IKVM rejete) |
| Phase 1 | `Bridge.cs` + `Wrappers.cs` (Screen, Region, Pattern, App, Match) | ✅ scaffold |
| Phase 2 | VNCScreen + ADBScreen + SSHTunnel | ✅ scaffold |
| Phase 3 | OCR (Tesseract static) + PaddleOCREngine + Key/Settings proxy | ✅ scaffold |
| Phase 4 | Tests d'integration end-to-end avec un vrai `oculixapi:3.0.3` | 🚧 a faire |
| Phase 5 | NUnit + SpecFlow examples (sous `dotnet/examples/`) | 🚧 a faire |
| Phase 6 | Premiere release NuGet 0.1.0 | 🚧 a faire |
| Phase 7 | CI multi-OS (test on Win/Mac/Linux dans `ci.yml`) | 🚧 partiel |

## 11. Tradeoffs : ce qu'on a choisi vs ce qu'on a laisse

| Critere | Process Bridge (retenu) | IKVM (rejete) | Javonet (rejete) |
|---|---|---|---|
| JVM requise cote utilisateur | Oui | Non | Non |
| Performance | JSON serialisation, ~1-5 ms/appel | Native .NET | Native .NET |
| Distribution | NuGet (petit) + JAR a la demande | DLL gros (~150 MB) | DLL + licence commerciale |
| Compatibilite Java 17+ | OK aujourd'hui | KO aujourd'hui | OK |
| Code Java mutualise avec Node.js | Oui | Non | Non |
| Licence | MIT pur | MIT | Commercial $69/mois |
| Debug | Deux process | Un seul | Un seul |

---

*"dotnet add package OculiX. Visual automation meets the .NET enterprise."*
