using System.Threading.Tasks;

namespace OculiX;

/// <summary>Base class for all OculiX wrappers — owns the bridge handle.</summary>
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

// --- Region: geometry + mouse + keyboard + search --------------------------

public class Region : OculixClass
{
    public const string JavaClass = "org.sikuli.script.Region";

    public static Task<Region> FromRect(int x, int y, int w, int h)
        => CreateAsync<Region>(JavaClass, x, y, w, h);

    // mouse
    public Task Click(string? target = null)       => target is null ? Call("click") : Call("click", target);
    public Task DoubleClick(string? target = null) => target is null ? Call("doubleClick") : Call("doubleClick", target);
    public Task RightClick(string? target = null)  => target is null ? Call("rightClick") : Call("rightClick", target);
    public Task Hover(string? target = null)       => target is null ? Call("hover") : Call("hover", target);
    public Task DragDrop(string src, string dst)   => Call("dragDrop", src, dst);
    public Task MouseMove(string? target = null)   => target is null ? Call("mouseMove") : Call("mouseMove", target);
    public Task MouseDown(int button = 1)          => Call("mouseDown", button);
    public Task MouseUp(int button = 0)            => Call("mouseUp", button);

    // keyboard
    public Task Type(string text)        => Call("type", text);
    public Task Paste(string text)       => Call("paste", text);
    public Task Write(string text)       => Call("write", text);
    public Task KeyDown(string keys)     => Call("keyDown", keys);
    public Task KeyUp(string? keys = null) => keys is null ? Call("keyUp") : Call("keyUp", keys);

    // search — all return a Java object (Match / Iterator<Match>) wrapped as RemoteObject
    public Task<object?> Find(string target)     => Call("find", target);
    public Task<object?> FindAll(string target)  => Call("findAll", target);
    public Task<object?> Wait(string target, double timeout = 10) => Call("wait", target, timeout);
    public Task<object?> WaitVanish(string target, double timeout = 10) => Call("waitVanish", target, timeout);
    public async Task<bool> Exists(string target, double timeout = 3)
        => (await Call("exists", target, timeout)) != null;
    public Task<object?> GetLastMatch() => Call("getLastMatch");

    // OCR
    public async Task<string?> Text() => (await Call("text")) as string;
    public Task<object?> TextLines()  => Call("textLines");
    public Task<object?> TextWords()  => Call("textWords");

    // geometry — getters
    public async Task<int> GetX() => (int)(await Call("getX"))!;
    public async Task<int> GetY() => (int)(await Call("getY"))!;
    public async Task<int> GetW() => (int)(await Call("getW"))!;
    public async Task<int> GetH() => (int)(await Call("getH"))!;

    // geometry — setters / movement
    public Task SetX(int x) => Call("setX", x);
    public Task SetY(int y) => Call("setY", y);
    public Task SetW(int w) => Call("setW", w);
    public Task SetH(int h) => Call("setH", h);
    public Task MoveTo(int x, int y) => Call("moveTo", x, y);
    public Task SetROI(int x, int y, int w, int h) => Call("setROI", x, y, w, h);

    // spatial
    public Task<object?> Nearby(int rangePx = 50) => Call("nearby", rangePx);
    public Task<object?> Above(int rangePx = 0)   => rangePx == 0 ? Call("above") : Call("above", rangePx);
    public Task<object?> Below(int rangePx = 0)   => rangePx == 0 ? Call("below") : Call("below", rangePx);
    public Task<object?> Left(int rangePx = 0)    => rangePx == 0 ? Call("left")  : Call("left", rangePx);
    public Task<object?> Right(int rangePx = 0)   => rangePx == 0 ? Call("right") : Call("right", rangePx);

    // misc
    public Task Highlight(double secs = 2)      => Call("highlight", secs);
    public Task<object?> Contains(object other) => Call("contains", other);
    public Task<object?> Capture()              => Call("capture");
}

// --- Screen ----------------------------------------------------------------

public sealed class Screen : Region
{
    public new const string JavaClass = "org.sikuli.script.Screen";

    public static Task<Screen> CreateAsync(int screenId = 0)
        => CreateAsync<Screen>(JavaClass, screenId);

    public static async Task<int> GetNumberScreens()
        => (int)(await Bridge.Default.CallStaticAsync(JavaClass, "getNumberScreens"))!;

    public static Task<object?> GetBounds(int screenId = 0)
        => Bridge.Default.CallStaticAsync(JavaClass, "getBounds", screenId);
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

// --- Match: a Region with a similarity score ------------------------------

/// <summary>
/// Match extends Region with a similarity score. Match objects are returned
/// by Region.Find / Region.Wait / Region.Exists — typically you don't build
/// one yourself.
/// </summary>
public sealed class Match : Region
{
    public new const string JavaClass = "org.sikuli.script.Match";

    public async Task<double> GetScore() => (double)(await Call("getScore"))!;
    public Task<object?> GetTarget()     => Call("getTarget");
    public async Task<int> GetIndex()    => (int)(await Call("getIndex"))!;
}

// --- App -------------------------------------------------------------------

public sealed class App : OculixClass
{
    public const string JavaClass = "org.sikuli.script.App";

    public static Task<App> Create(string name) => CreateAsync<App>(JavaClass, name);

    public static Task<App> Open(string path) => WrapStaticAsync<App>(JavaClass, "open", path);

    public Task Focus()  => Call("focus");
    public Task Close()  => Call("close");
    public Task Window() => Call("window");
    public async Task<bool> IsRunning() => (bool)(await Call("isRunning"))!;
    public async Task<bool> HasWindow() => (bool)(await Call("hasWindow"))!;
    public async Task<string?> GetName() => (await Call("getName")) as string;
    public async Task<int> GetPID() => (int)(await Call("getPID"))!;
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
    public Task Tap(int x, int y)    => Call("tap", x, y);
    public Task Swipe(int x1, int y1, int x2, int y2) => Call("swipe", x1, y1, x2, y2);
    public Task WakeUp(int secs = 1) => Call("wakeUp", secs);
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

// --- OCR engines ----------------------------------------------------------

public sealed class PaddleOCREngine : OculixClass
{
    public const string JavaClass = "com.sikulix.ocr.PaddleOCREngine";

    public static Task<PaddleOCREngine> GetInstance()
        => WrapStaticAsync<PaddleOCREngine>(JavaClass, "getInstance");
}

/// <summary>Tesseract-based OCR (org.sikuli.script.OCR) — fully static.</summary>
public static class OCR
{
    public const string JavaClass = "org.sikuli.script.OCR";

    public static Task<object?> ReadText(object target)  => Bridge.Default.CallStaticAsync(JavaClass, "readText", target);
    public static Task<object?> ReadLine(object target)  => Bridge.Default.CallStaticAsync(JavaClass, "readLine", target);
    public static Task<object?> ReadWord(object target)  => Bridge.Default.CallStaticAsync(JavaClass, "readWord", target);
    public static Task<object?> ReadLines(object target) => Bridge.Default.CallStaticAsync(JavaClass, "readLines", target);
    public static Task<object?> ReadWords(object target) => Bridge.Default.CallStaticAsync(JavaClass, "readWords", target);
}

// --- static-fields proxy (Key, Settings) ----------------------------------

/// <summary>
/// Reads static fields off a Java class via a Class.getField roundtrip.
/// Used for OculiX's <c>Key</c> and <c>Settings</c> classes, which expose
/// most of their public surface as static constants.
/// </summary>
public sealed class StaticConstants
{
    private readonly string _javaClass;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object?> _cache = new();

    public StaticConstants(string javaClass) => _javaClass = javaClass;

    public async Task<object?> Get(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;
        var bridge = Bridge.Default;
        var cls   = (RemoteObject)(await bridge.CallStaticAsync("java.lang.Class", "forName", _javaClass))!;
        var field = (RemoteObject)(await bridge.CallAsync(cls.Ref, "getField", name))!;
        var value = await bridge.CallAsync(field.Ref, "get", new object?[] { null });
        _cache[name] = value;
        return value;
    }
}

public static class Key
{
    public static readonly StaticConstants Constants = new("org.sikuli.script.Key");
    public static Task<object?> Get(string name) => Constants.Get(name);
}

public static class Settings
{
    public static readonly StaticConstants Constants = new("org.sikuli.basics.Settings");
    public static Task<object?> Get(string name) => Constants.Get(name);
}
