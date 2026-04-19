using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace OculiX;

/// <summary>
/// JSON-RPC client to the OculiX JVM bridge over stdin/stdout.
///
/// The bridge is a fat JAR (~160 MB) downloaded once from GitHub Releases
/// into <c>~/.oculix/lib/</c> on first use.
/// </summary>
public sealed class Bridge : IDisposable
{
    public const string BridgeVersion = "0.1.0";

    private static readonly string BridgeJarName = $"operix-jvm-bridge-{BridgeVersion}.jar";
    private static readonly string BridgeJarUrl =
        $"https://github.com/oculix-org/Operix/releases/download/" +
        $"jvm-bridge-{BridgeVersion}/{BridgeJarName}";
    private static readonly string JarDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".oculix", "lib");

    private readonly string _javaBin;
    private readonly string? _jarPathOverride;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private long _nextId;
    private Process? _proc;

    public Bridge(string? jarPath = null, string javaBin = "java")
    {
        _jarPathOverride = jarPath;
        _javaBin = javaBin;
    }

    public async Task StartAsync()
    {
        if (_proc != null) return;
        var jar = _jarPathOverride ?? await EnsureJarAsync();

        // BOM-less UTF-8 — the JVM bridge parses each line as JSON and
        // chokes on a BOM at the very first byte of stdin.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var psi = new ProcessStartInfo(_javaBin, $"-jar \"{jar}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = utf8NoBom,
            StandardInputEncoding = utf8NoBom,
        };

        _proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start JVM");

        // Auto-flush stdin so each request line reaches the JVM immediately.
        _proc.StandardInput.AutoFlush = true;

        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(DrainStderrAsync);
    }

    public void Dispose()
    {
        try { _proc?.StandardInput.Close(); } catch { /* ignore */ }
        try { _proc?.Kill(true); } catch { /* ignore */ }
        _proc = null;
    }

    // --- public RPC operations ---------------------------------------------

    public async Task<RemoteObject> CreateAsync(string className, params object?[] args)
    {
        var node = await SendAsync(new JsonObject
        {
            ["class"] = className,
            ["args"] = EncodeArgs(args),
        });
        return (RemoteObject)Decode(node)!;
    }

    public async Task<object?> CallAsync(string @ref, string method, params object?[] args)
    {
        var node = await SendAsync(new JsonObject
        {
            ["ref"] = @ref,
            ["method"] = method,
            ["args"] = EncodeArgs(args),
        });
        return Decode(node);
    }

    public async Task<object?> CallStaticAsync(string className, string method, params object?[] args)
    {
        var node = await SendAsync(new JsonObject
        {
            ["class"] = className,
            ["method"] = method,
            ["static"] = true,
            ["args"] = EncodeArgs(args),
        });
        return Decode(node);
    }

    public async Task ReleaseAsync(string @ref)
    {
        try
        {
            await SendAsync(new JsonObject { ["ref"] = @ref, ["release"] = true });
        }
        catch { /* best-effort */ }
    }

    // --- internals ---------------------------------------------------------

    private async Task<JsonNode?> SendAsync(JsonObject payload)
    {
        if (_proc == null) await StartAsync();

        var id = Interlocked.Increment(ref _nextId);
        payload["id"] = id;
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var line = payload.ToJsonString() + "\n";
        lock (_lock)
        {
            _proc!.StandardInput.Write(line);
            _proc.StandardInput.Flush();
        }
        return await tcs.Task;
    }

    private async Task ReadLoopAsync()
    {
        var reader = _proc!.StandardOutput;
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;
                JsonNode? response;
                try { response = JsonNode.Parse(line); }
                catch { continue; /* JVM banner — not for us */ }
                if (response is null) continue;

                var idNode = response["id"];
                if (idNode == null) continue;
                var id = idNode.GetValue<long>();
                if (!_pending.TryRemove(id, out var tcs)) continue;

                var error = response["error"];
                if (error != null)
                {
                    tcs.SetException(new BridgeException(error.ToString()));
                }
                else
                {
                    tcs.SetResult(response["result"]);
                }
            }
        }
        catch (Exception e)
        {
            FailAllPending(new BridgeException("JVM bridge died: " + e.Message));
        }
    }

    private async Task DrainStderrAsync()
    {
        try
        {
            await _proc!.StandardError.ReadToEndAsync();
        }
        catch { /* ignore */ }
    }

    private void FailAllPending(Exception e)
    {
        foreach (var kv in _pending) kv.Value.TrySetException(e);
        _pending.Clear();
    }

    // --- value codec --------------------------------------------------------

    private static JsonArray EncodeArgs(object?[] args)
    {
        var arr = new JsonArray();
        foreach (var a in args) arr.Add(Encode(a));
        return arr;
    }

    private static JsonNode? Encode(object? v)
    {
        if (v == null) return null;
        if (v is RemoteObject ro) return new JsonObject { ["__ref"] = ro.Ref };
        if (v is string s) return s;
        if (v is bool b) return b;
        if (v is int i) return i;
        if (v is long l) return l;
        if (v is double d) return d;
        if (v is float f) return f;
        return JsonValue.Create(v);
    }

    private object? Decode(JsonNode? v)
    {
        if (v is null) return null;
        if (v is JsonObject obj && obj.ContainsKey("__ref"))
        {
            var refId = obj["__ref"]!.GetValue<string>();
            var klass = obj.ContainsKey("__class") ? obj["__class"]!.GetValue<string>() : "?";
            return new RemoteObject(this, refId, klass);
        }
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var bv)) return bv;
            if (jv.TryGetValue<int>(out var iv)) return iv;
            if (jv.TryGetValue<long>(out var lv)) return lv;
            if (jv.TryGetValue<double>(out var dv)) return dv;
            if (jv.TryGetValue<string>(out var sv)) return sv;
        }
        return v;
    }

    // --- JAR distribution ---------------------------------------------------

    private static async Task<string> EnsureJarAsync()
    {
        var jarPath = Path.Combine(JarDir, BridgeJarName);
        if (File.Exists(jarPath)) return jarPath;
        Directory.CreateDirectory(JarDir);
        Console.WriteLine($"[OculiX] Downloading {BridgeJarName} (~160 MB)…");
        using var http = new HttpClient();
        using var s = await http.GetStreamAsync(BridgeJarUrl);
        using var f = File.Create(jarPath);
        await s.CopyToAsync(f);
        Console.WriteLine($"[OculiX] Saved to {jarPath}");
        return jarPath;
    }

    // --- module-wide singleton ---------------------------------------------

    private static Bridge? _default;
    private static readonly object _defaultLock = new();

    public static Bridge Default
    {
        get
        {
            lock (_defaultLock)
            {
                _default ??= new Bridge();
                return _default;
            }
        }
    }
}

public sealed class BridgeException : Exception
{
    public BridgeException(string message) : base(message) { }
}

/// <summary>Opaque handle to a Java object held by the JVM bridge.</summary>
public sealed class RemoteObject
{
    public Bridge Bridge { get; }
    public string Ref { get; }
    public string JavaClass { get; }

    internal RemoteObject(Bridge bridge, string @ref, string javaClass)
    {
        Bridge = bridge;
        Ref = @ref;
        JavaClass = javaClass;
    }

    public Task<object?> CallAsync(string method, params object?[] args)
        => Bridge.CallAsync(Ref, method, args);

    public Task ReleaseAsync() => Bridge.ReleaseAsync(Ref);

    public override string ToString() => $"<RemoteObject {JavaClass}#{Ref}>";
}
