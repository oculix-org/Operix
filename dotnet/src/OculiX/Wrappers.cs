using System.Threading.Tasks;

namespace OculiX;

/// <summary>Base for Pythonic class wrappers around the most-used Oculix classes.</summary>
public abstract class OculixClass
{
    protected RemoteObject Remote { get; private set; } = null!;

    protected static async Task<T> CreateAsync<T>(string javaClass, params object?[] args)
        where T : OculixClass, new()
    {
        var instance = new T { Remote = await Bridge.Default.CreateAsync(javaClass, args) };
        return instance;
    }

    protected static async Task<T> WrapStaticAsync<T>(string javaClass, string method, params object?[] args)
        where T : OculixClass, new()
    {
        var result = await Bridge.Default.CallStaticAsync(javaClass, method, args);
        return new T { Remote = (RemoteObject)result! };
    }

    internal void SetRemote(RemoteObject remote) => Remote = remote;

    protected Task<object?> Call(string method, params object?[] args) => Remote.CallAsync(method, args);
}

// --- Screen ----------------------------------------------------------------

public sealed class Screen : OculixClass
{
    public const string JavaClass = "org.sikuli.script.Screen";

    public static Task<Screen> CreateAsync() => CreateAsync<Screen>(JavaClass);

    public Task Click(string target)       => Call("click", target);
    public Task DoubleClick(string target) => Call("doubleClick", target);
    public Task RightClick(string target)  => Call("rightClick", target);
    public Task Hover(string target)       => Call("hover", target);
    public Task Type(string text)          => Call("type", text);
    public Task Paste(string text)         => Call("paste", text);
    public Task Wait(string target, double timeout = 10) => Call("wait", target, timeout);
    public Task Find(string target) => Call("find", target);
    public async Task<bool> Exists(string target, double timeout = 3)
        => (await Call("exists", target, timeout)) != null;
    public async Task<string?> Text() => (await Call("text")) as string;
    public Task Capture() => Call("capture");
}

// --- Pattern ---------------------------------------------------------------

public sealed class Pattern : OculixClass
{
    public const string JavaClass = "org.sikuli.script.Pattern";

    public static Task<Pattern> FromImage(string imagePath) => CreateAsync<Pattern>(JavaClass, imagePath);

    public async Task<Pattern> Similar(double value)        { await Call("similar", value);        return this; }
    public async Task<Pattern> Exact()                      { await Call("exact");                 return this; }
    public async Task<Pattern> TargetOffset(int x, int y)   { await Call("targetOffset", x, y);    return this; }
}

// --- App -------------------------------------------------------------------

public sealed class App : OculixClass
{
    public const string JavaClass = "org.sikuli.script.App";

    public static Task<App> Open(string path) => WrapStaticAsync<App>(JavaClass, "open", path);

    public Task Focus()  => Call("focus");
    public Task Close()  => Call("close");
    public Task Window() => Call("window");
}

// --- VNCScreen -------------------------------------------------------------

public sealed class VNCScreen : OculixClass
{
    public const string JavaClass = "org.sikuli.vnc.VNCScreen";

    public static Task<VNCScreen> Start(string host, int port, string password, int width, int height)
        => WrapStaticAsync<VNCScreen>(JavaClass, "start", host, port, password, width, height);

    public Task Click(string target) => Call("click", target);
    public Task Type(string text)    => Call("type", text);
    public Task Stop()               => Call("stop");
}

// --- ADBScreen (org.sikuli.android, not org.sikuli.script) -----------------

public sealed class ADBScreen : OculixClass
{
    public const string JavaClass = "org.sikuli.android.ADBScreen";

    public static Task<ADBScreen> Start(string? adbPath = null)
        => adbPath is null
            ? WrapStaticAsync<ADBScreen>(JavaClass, "start")
            : WrapStaticAsync<ADBScreen>(JavaClass, "start", adbPath);

    public Task Click(string target) => Call("click", target);
    public Task Type(string text)    => Call("type", text);
}

// --- SSHTunnel -------------------------------------------------------------

public sealed class SSHTunnel : OculixClass
{
    public const string JavaClass = "com.sikulix.util.SSHTunnel";

    public static Task<SSHTunnel> Create(string user, string host, int port, string password)
        => CreateAsync<SSHTunnel>(JavaClass, user, host, port, password);

    public Task Open(int localPort, string remoteHost, int remotePort)
        => Call("open", localPort, remoteHost, remotePort);

    public Task Close() => Call("close");
}

// --- PaddleOCR (com.sikulix.ocr — Oculix's neural OCR engine) --------------

public sealed class PaddleOCREngine : OculixClass
{
    public const string JavaClass = "com.sikulix.ocr.PaddleOCREngine";

    public static Task<PaddleOCREngine> GetInstance()
        => WrapStaticAsync<PaddleOCREngine>(JavaClass, "getInstance");
}
