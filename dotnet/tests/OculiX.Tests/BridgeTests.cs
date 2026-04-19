using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace OculiX.Tests;

/// <summary>
/// Integration tests for the .NET wrapper. Spawns the actual JVM bridge JAR
/// and exchanges JSON-RPC over stdio. The JAR is built via <c>mvn package</c>
/// in <c>../jvm-bridge/</c>.
/// </summary>
public sealed class BridgeTests : IDisposable
{
    private static readonly string LocalJar = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..")),
        "jvm-bridge", "target", "operix-jvm-bridge.jar");

    private readonly Bridge _bridge;
    private readonly bool _skip;

    public BridgeTests()
    {
        _skip = !File.Exists(LocalJar) || !JavaAvailable();
        _bridge = new Bridge(jarPath: LocalJar);
    }

    public void Dispose() => _bridge.Dispose();

    private static bool JavaAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("java", "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            })!;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task ConstructStringBuilder_AndChainCalls()
    {
        if (_skip) return;
        var sb = await _bridge.CreateAsync("java.lang.StringBuilder");
        var chained = (RemoteObject)(await sb.CallAsync("append", "hello "))!;
        Assert.Equal(sb.Ref, chained.Ref); // identity interning
        await sb.CallAsync("append", "operix");
        Assert.Equal(12, (int)(await sb.CallAsync("length"))!);
        Assert.Equal("hello operix", (string)(await sb.CallAsync("toString"))!);
    }

    [Fact]
    public async Task StaticMethodCall_ReturnsString()
    {
        if (_skip) return;
        var result = await _bridge.CallStaticAsync("java.lang.Integer", "toBinaryString", 42);
        Assert.Equal("101010", result);
    }

    [Fact]
    public async Task OverloadResolution_ByArgumentType()
    {
        if (_skip) return;
        Assert.Equal(7, (int)(await _bridge.CallStaticAsync("java.lang.Math", "abs", -7))!);
        Assert.Equal(3.5, (double)(await _bridge.CallStaticAsync("java.lang.Math", "max", 3.5, 2.5))!);
    }

    [Fact]
    public async Task UnknownClass_ThrowsBridgeException()
    {
        if (_skip) return;
        await Assert.ThrowsAsync<BridgeException>(
            async () => await _bridge.CreateAsync("no.such.Class"));
    }

    [Fact]
    public async Task Release_DropsTheRef()
    {
        if (_skip) return;
        var sb = await _bridge.CreateAsync("java.lang.StringBuilder");
        var refId = sb.Ref;
        await _bridge.ReleaseAsync(refId);
        await Assert.ThrowsAsync<BridgeException>(
            async () => await _bridge.CallAsync(refId, "length"));
    }
}
