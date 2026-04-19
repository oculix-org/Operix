/**
 * OculiX — visual automation for the real world (Node.js wrapper).
 *
 *     import { Screen } from 'oculix';
 *     await new Screen().click('button.png');
 */

import { Bridge, BridgeError, RemoteObject, defaultBridge } from './bridge';

export { Bridge, BridgeError, RemoteObject, defaultBridge };

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

// --- Screen -----------------------------------------------------------------

export class Screen extends OculixClass {
  static readonly JAVA_CLASS = 'org.sikuli.script.Screen';

  static async create(): Promise<Screen> {
    const s = new Screen();
    await s.ensureRemote([]);
    return s;
  }

  async click(target: string)       { return this.call('click', target); }
  async doubleClick(target: string) { return this.call('doubleClick', target); }
  async rightClick(target: string)  { return this.call('rightClick', target); }
  async hover(target: string)       { return this.call('hover', target); }
  async type(text: string)          { return this.call('type', text); }
  async paste(text: string)         { return this.call('paste', text); }
  async wait(target: string, timeout = 10) {
    return this.call('wait', target, timeout);
  }
  async find(target: string)        { return this.call('find', target); }
  async exists(target: string, timeout = 3): Promise<boolean> {
    const result = await this.call('exists', target, timeout);
    return result !== null;
  }
  async text(): Promise<string>     { return (await this.call('text')) as string; }
  async capture()                   { return this.call('capture'); }
}

// --- Pattern ----------------------------------------------------------------

export class Pattern extends OculixClass {
  static readonly JAVA_CLASS = 'org.sikuli.script.Pattern';

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

// --- App --------------------------------------------------------------------

export class App extends OculixClass {
  static readonly JAVA_CLASS = 'org.sikuli.script.App';

  static async open(path: string): Promise<App> {
    const result = await defaultBridge().callStatic(App.JAVA_CLASS, 'open', [path]);
    const app = new App();
    app.remote = result as RemoteObject;
    return app;
  }

  async focus()  { return this.call('focus'); }
  async close()  { return this.call('close'); }
  async window() { return this.call('window'); }
}

// --- VNC --------------------------------------------------------------------

export class VNCScreen extends OculixClass {
  static readonly JAVA_CLASS = 'org.sikuli.vnc.VNCScreen';

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

// --- ADB (org.sikuli.android, not org.sikuli.script) ------------------------

export class ADBScreen extends OculixClass {
  static readonly JAVA_CLASS = 'org.sikuli.android.ADBScreen';

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
}

// --- SSH tunnel -------------------------------------------------------------

export class SSHTunnel extends OculixClass {
  static readonly JAVA_CLASS = 'com.sikulix.util.SSHTunnel';

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

// --- PaddleOCR (Oculix's neural OCR engine) ---------------------------------

export class PaddleOCREngine extends OculixClass {
  static readonly JAVA_CLASS = 'com.sikulix.ocr.PaddleOCREngine';

  static async getInstance(): Promise<PaddleOCREngine> {
    const result = await defaultBridge().callStatic(
      PaddleOCREngine.JAVA_CLASS, 'getInstance', [],
    );
    const o = new PaddleOCREngine();
    o.remote = result as RemoteObject;
    return o;
  }
}
