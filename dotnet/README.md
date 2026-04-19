# OculiX (.NET)

.NET wrapper for [OculiX](https://github.com/oculix-org/Oculix) — visual automation for the real world.

## Install

```bash
dotnet add package OculiX
```

Requirements: .NET 8+, Java 11+ on `PATH` (Eclipse Temurin or Azul Zulu).

The OculiX engine (~160 MB fat JAR) is downloaded on first use into `~/.oculix/lib/`.

## Quickstart

```csharp
using OculiX;

var screen = await Screen.CreateAsync();
await screen.Click("login.png");
await screen.Type("admin");
await screen.Click("submit.png");
await screen.Wait("dashboard.png", 10);

var calc = await App.Open("calc");
await screen.Click("button_7.png");

var vnc = await VNCScreen.Start("10.0.0.42", 5900, "", 1920, 1080);
await vnc.Click("logo.png");
await vnc.Stop();
```

## With NUnit

```csharp
using NUnit.Framework;
using OculiX;

[TestFixture]
public class CalculatorTests
{
    private App _calc = null!;
    private Screen _screen = null!;

    [SetUp]
    public async Task Setup()
    {
        _calc = await App.Open("calc");
        _screen = await Screen.CreateAsync();
    }

    [TearDown]
    public async Task TearDown() => await _calc.Close();

    [Test]
    public async Task Addition()
    {
        await _screen.Click("button_7.png");
        await _screen.Click("button_plus.png");
        await _screen.Click("button_3.png");
        await _screen.Click("button_equals.png");
        Assert.That(await _screen.Exists("result_10.png"), Is.True);
    }
}
```

## Why no IKVM?

IKVM 8.x only supports Java 8 bytecode. OculiX 3.x is built with Java 17.
A spike on 19 Apr 2026 (IKVM 8.15.0, latest stable) confirmed `0` types
exported from the converted `oculixapi.dll` — see
[`CDC-Operix-DotNet.md`](../CDC-Operix-DotNet.md) §3 for details.

The wrapper uses a process-bridge JSON-RPC architecture instead, sharing
the same Java backend (`jvm-bridge/`) as the Node.js wrapper.

## License

MIT
