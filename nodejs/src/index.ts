/**
 * OculiX — visual automation for the real world (Node.js wrapper).
 *
 *     import { Screen } from 'oculix';
 *     await (await Screen.create()).click('button.png');
 */

import { Bridge, BridgeError, RemoteObject, defaultBridge } from './bridge';

export { Bridge, BridgeError, RemoteObject, defaultBridge };

// --- base class -------------------------------------------------------------

abstract class OculixClass {
  protected remote!: RemoteObject;
  protected static readonly JAVA_CLASS: string;

  protected async ensureRemote(args: unknown[]): Promise<void> {
    if (!this.remote) {
      const ctor = (this.constructor as typeof OculixClass).JAVA_CLASS;
      this.remote = await defaultBridge().create(ctor, args);
    }
  }

  protected async call(method: string, ...args: unknown[]): Promise<unknown> {
    return this.remote.call(method, ...args);
  }
}

// --- Region: geometry + mouse + keyboard + search --------------------------

export class Region extends OculixClass {
  static readonly JAVA_CLASS: string = 'org.sikuli.script.Region';

  /** Build a Region from pixel coordinates. */
  static async fromRect(x: number, y: number, w: number, h: number): Promise<Region> {
    const r = new Region();
    await r.ensureRemote([x, y, w, h]);
    return r;
  }

  // mouse
  async click(target?: string)       { return target ? this.call('click', target) : this.call('click'); }
  async doubleClick(target?: string) { return target ? this.call('doubleClick', target) : this.call('doubleClick'); }
  async rightClick(target?: string)  { return target ? this.call('rightClick', target) : this.call('rightClick'); }
  async hover(target?: string)       { return target ? this.call('hover', target) : this.call('hover'); }
  async dragDrop(src: string, dst: string) { return this.call('dragDrop', src, dst); }
  async mouseMove(target?: string)   { return target ? this.call('mouseMove', target) : this.call('mouseMove'); }
  async mouseDown(button = 1)        { return this.call('mouseDown', button); }
  async mouseUp(button = 0)          { return this.call('mouseUp', button); }

  // keyboard
  async type(text: string)    { return this.call('type', text); }
  async paste(text: string)   { return this.call('paste', text); }
  async write(text: string)   { return this.call('write', text); }
  async keyDown(keys: string) { return this.call('keyDown', keys); }
  async keyUp(keys?: string)  { return keys !== undefined ? this.call('keyUp', keys) : this.call('keyUp'); }

  // search
  async find(target: string)    { return this.call('find', target); }
  async findAll(target: string) { return this.call('findAll', target); }
  async wait(target: string, timeout = 10) { return this.call('wait', target, timeout); }
  async waitVanish(target: string, timeout = 10) { return this.call('waitVanish', target, timeout); }
  async exists(target: string, timeout = 3): Promise<boolean> {
    return (await this.call('exists', target, timeout)) !== null;
  }
  async getLastMatch() { return this.call('getLastMatch'); }

  // OCR
  async text(): Promise<string>      { return (await this.call('text')) as string; }
  async textLines(): Promise<unknown> { return this.call('textLines'); }
  async textWords(): Promise<unknown> { return this.call('textWords'); }

  // geometry — getters
  async getX(): Promise<number> { return (await this.call('getX')) as number; }
  async getY(): Promise<number> { return (await this.call('getY')) as number; }
  async getW(): Promise<number> { return (await this.call('getW')) as number; }
  async getH(): Promise<number> { return (await this.call('getH')) as number; }

  // geometry — setters / movement
  async setX(x: number) { return this.call('setX', x); }
  async setY(y: number) { return this.call('setY', y); }
  async setW(w: number) { return this.call('setW', w); }
  async setH(h: number) { return this.call('setH', h); }
  async moveTo(x: number, y: number) { return this.call('moveTo', x, y); }
  async setROI(x: number, y: number, w: number, h: number) {
    return this.call('setROI', x, y, w, h);
  }

  // spatial
  async nearby(rangePx = 50)     { return this.call('nearby', rangePx); }
  async above(rangePx = 0)       { return rangePx ? this.call('above', rangePx) : this.call('above'); }
  async below(rangePx = 0)       { return rangePx ? this.call('below', rangePx) : this.call('below'); }
  async left(rangePx = 0)        { return rangePx ? this.call('left', rangePx) : this.call('left'); }
  async right(rangePx = 0)       { return rangePx ? this.call('right', rangePx) : this.call('right'); }

  // misc
  async highlight(secs = 2)      { return this.call('highlight', secs); }
  async contains(other: unknown) { return this.call('contains', other); }
  async capture()                { return this.call('capture'); }
}

// --- Screen ----------------------------------------------------------------

export class Screen extends Region {
  static readonly JAVA_CLASS: string = 'org.sikuli.script.Screen';

  static async create(screenId = 0): Promise<Screen> {
    const s = new Screen();
    await s.ensureRemote([screenId]);
    return s;
  }

  static async getNumberScreens(): Promise<number> {
    return (await defaultBridge().callStatic(Screen.JAVA_CLASS, 'getNumberScreens', [])) as number;
  }

  static async getBounds(screenId = 0): Promise<unknown> {
    return defaultBridge().callStatic(Screen.JAVA_CLASS, 'getBounds', [screenId]);
  }
}

// --- Pattern ---------------------------------------------------------------

export class Pattern extends OculixClass {
  static readonly JAVA_CLASS: string = 'org.sikuli.script.Pattern';

  static async fromImage(imagePath: string): Promise<Pattern> {
    const p = new Pattern();
    await p.ensureRemote([imagePath]);
    return p;
  }

  async similar(value: number): Promise<this>      { await this.call('similar', value); return this; }
  async exact(): Promise<this>                     { await this.call('exact'); return this; }
  async targetOffset(x: number, y: number): Promise<this> {
    await this.call('targetOffset', x, y); return this;
  }
}

// --- Match -----------------------------------------------------------------

/**
 * Match extends Region with a similarity score. Match objects are returned
 * by Region.find / Region.wait / Region.exists — typically you don't build
 * one yourself.
 */
export class Match extends Region {
  static readonly JAVA_CLASS: string = 'org.sikuli.script.Match';

  async getScore(): Promise<number>   { return (await this.call('getScore')) as number; }
  async getTarget(): Promise<unknown> { return this.call('getTarget'); }
  async getIndex(): Promise<number>   { return (await this.call('getIndex')) as number; }
}

// --- App -------------------------------------------------------------------

export class App extends OculixClass {
  static readonly JAVA_CLASS: string = 'org.sikuli.script.App';

  static async create(name: string): Promise<App> {
    const a = new App();
    await a.ensureRemote([name]);
    return a;
  }

  static async open(path: string): Promise<App> {
    const result = await defaultBridge().callStatic(App.JAVA_CLASS, 'open', [path]);
    const app = new App();
    app.remote = result as RemoteObject;
    return app;
  }

  async focus()      { return this.call('focus'); }
  async close()      { return this.call('close'); }
  async window()     { return this.call('window'); }
  async isRunning(): Promise<boolean> { return (await this.call('isRunning')) as boolean; }
  async hasWindow(): Promise<boolean> { return (await this.call('hasWindow')) as boolean; }
  async getName(): Promise<string>    { return (await this.call('getName')) as string; }
  async getPID(): Promise<number>     { return (await this.call('getPID')) as number; }
}

// --- VNC -------------------------------------------------------------------

export class VNCScreen extends OculixClass {
  static readonly JAVA_CLASS: string = 'org.sikuli.vnc.VNCScreen';

  static async start(host: string, port: number, password: string,
                     width: number, height: number): Promise<VNCScreen> {
    const result = await defaultBridge().callStatic(
      VNCScreen.JAVA_CLASS, 'start',
      [host, port, password, width, height],
    );
    const v = new VNCScreen();
    v.remote = result as RemoteObject;
    return v;
  }

  async click(target: string) { return this.call('click', target); }
  async type(text: string)    { return this.call('type', text); }
  async stop()                { return this.call('stop'); }
}

// --- ADB (org.sikuli.android, not org.sikuli.script) -----------------------

export class ADBScreen extends OculixClass {
  static readonly JAVA_CLASS: string = 'org.sikuli.android.ADBScreen';

  static async start(adbPath?: string): Promise<ADBScreen> {
    const result = await defaultBridge().callStatic(
      ADBScreen.JAVA_CLASS, 'start',
      adbPath ? [adbPath] : [],
    );
    const a = new ADBScreen();
    a.remote = result as RemoteObject;
    return a;
  }

  async click(target: string) { return this.call('click', target); }
  async type(text: string)    { return this.call('type', text); }
  async tap(x: number, y: number) { return this.call('tap', x, y); }
  async swipe(x1: number, y1: number, x2: number, y2: number) {
    return this.call('swipe', x1, y1, x2, y2);
  }
  async wakeUp(secs = 1) { return this.call('wakeUp', secs); }
}

// --- SSH tunnel ------------------------------------------------------------

export class SSHTunnel extends OculixClass {
  static readonly JAVA_CLASS: string = 'com.sikulix.util.SSHTunnel';

  static async create(user: string, host: string, port: number, password: string): Promise<SSHTunnel> {
    const t = new SSHTunnel();
    await t.ensureRemote([user, host, port, password]);
    return t;
  }

  async open(localPort: number, remoteHost: string, remotePort: number) {
    return this.call('open', localPort, remoteHost, remotePort);
  }
  async close() { return this.call('close'); }
}

// --- OCR engines -----------------------------------------------------------

export class PaddleOCREngine extends OculixClass {
  static readonly JAVA_CLASS: string = 'com.sikulix.ocr.PaddleOCREngine';

  static async getInstance(): Promise<PaddleOCREngine> {
    const result = await defaultBridge().callStatic(
      PaddleOCREngine.JAVA_CLASS, 'getInstance', [],
    );
    const o = new PaddleOCREngine();
    o.remote = result as RemoteObject;
    return o;
  }
}

/** Tesseract-based OCR (org.sikuli.script.OCR) — fully static. */
export class OCR {
  static readonly JAVA_CLASS: string = 'org.sikuli.script.OCR';

  static readText(target: unknown)  { return defaultBridge().callStatic(OCR.JAVA_CLASS, 'readText', [target]); }
  static readLine(target: unknown)  { return defaultBridge().callStatic(OCR.JAVA_CLASS, 'readLine', [target]); }
  static readWord(target: unknown)  { return defaultBridge().callStatic(OCR.JAVA_CLASS, 'readWord', [target]); }
  static readLines(target: unknown) { return defaultBridge().callStatic(OCR.JAVA_CLASS, 'readLines', [target]); }
  static readWords(target: unknown) { return defaultBridge().callStatic(OCR.JAVA_CLASS, 'readWords', [target]); }
}

// --- static-fields proxy (Key, Settings) -----------------------------------

/**
 * Lazy proxy that forwards property access to a Java class's static fields
 * (e.g. Key.ENTER, Settings.MinSimilarity). Resolves to a Promise<unknown>
 * because field reads roundtrip to the JVM.
 */
function staticConstants(javaClass: string): Record<string, Promise<unknown>> {
  const cache = new Map<string, Promise<unknown>>();
  return new Proxy({} as Record<string, Promise<unknown>>, {
    get(_t, prop: string): Promise<unknown> {
      if (typeof prop !== 'string') return Promise.resolve(undefined);
      const cached = cache.get(prop);
      if (cached) return cached;
      const p = (async () => {
        const bridge = defaultBridge();
        const cls = await bridge.callStatic('java.lang.Class', 'forName', [javaClass]) as RemoteObject;
        const field = await bridge.call(cls.ref, 'getField', [prop]) as RemoteObject;
        return bridge.call(field.ref, 'get', [null]);
      })();
      cache.set(prop, p);
      return p;
    },
  });
}

export const Key = staticConstants('org.sikuli.script.Key');
export const Settings = staticConstants('org.sikuli.basics.Settings');
