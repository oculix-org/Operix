# CDC — Operix-JS (Node.js)
## Node.js wrapper for OculiX via JSON-RPC bridge

**Author:** Julien Mer — JMer Consulting
**Date:** 25 June 2026
**Status:** Scaffolded — looking for a JS/TS maintainer
**Target repo:** oculix-org/Operix (subfolder `nodejs/`)
**npm package:** `oculix`
**Dependency:** `io.github.oculix-org:oculixapi:3.0.4` (Maven Central)
**Prerequisites:** Java 17+ (Eclipse Temurin / Azul Zulu recommended), Node.js 20+

---

## 1. Problem

Node.js is the dominant ecosystem for web testing (Playwright, Cypress, Jest, Mocha). But none of these tools handle visual testing on desktop, VNC, Android, kiosks, or POS systems. Playwright is browser-only. Cypress is browser-only. To test a native Windows app, a VNC terminal, or a mainframe — Node developers have nothing.

## 2. Solution

An npm package `oculix` that exposes the full OculiX API in JavaScript/TypeScript via a Java bridge. The user runs `npm install oculix` and combines native visual testing with their existing Node tools.

```bash
npm install oculix
```

```javascript
const { Screen, App, Pattern } = require('oculix');

const screen = new Screen();
await screen.click("button.png");
const app = await App.open("notepad");
```

## 3. Architecture

```
+---------------------+              +---------------------------+
|   Node.js process   |   spawn      |   JVM process             |
|                     |   stdin/out  |                           |
|   const screen =    |<------------>|   oculixapi-3.0.4.jar     |
|   new Screen()      |   JSON-RPC   |   + operix-rpc-server.jar |
|                     |              |   (org.operix.rpc.Server) |
+---------------------+              +---------------------------+
```

**Java bridge for Node.js:** a small custom JSON-RPC server (`org.operix.rpc.Server`) written in THIS repo (`Operix/jvm-bridge`), compiled into `operix-rpc-server.jar` and bundled inside the npm package. The JVM is launched as a child process with `oculixapi.jar + operix-rpc-server.jar` on the classpath and communicates line-by-line in JSON-RPC over stdin/stdout. No modification to the OculiX core repo required.

**Why not an existing library?**
- `java-caller` spawns the JVM per call → unacceptable latency (3s per click).
- `node-java-bridge` (JNI) → faster, but cross-platform builds are painful, and the OpenCV natives from Apertix already shipped in the JAR make a JNI mix risky.
- `py4j` has no official Node client.

So V1 = custom JSON-RPC (~150 lines of Java). Migration to `node-java-bridge` is possible in V2 if the demand justifies it.

## 4. Repo structure

```
oculix-org/Operix/nodejs/
|-- README.md
|-- LICENSE (MIT)
|-- package.json
|-- tsconfig.json
|-- src/
|   |-- index.ts              # main exports
|   |-- gateway.ts            # JVM lifecycle (start/stop/download JAR)
|   |-- screen.ts             # Screen wrapper
|   |-- region.ts             # Region wrapper
|   |-- pattern.ts            # Pattern wrapper
|   |-- app.ts                # App wrapper
|   |-- vnc.ts                # VNCScreen, SSHTunnel wrappers
|   |-- adb.ts                # ADBScreen wrapper
|   |-- ocr.ts                # OCR wrapper
|   +-- keys.ts               # Key, KeyModifier constants
|-- tests/
|   |-- gateway.test.ts
|   |-- screen.test.ts
|   +-- app.test.ts
+-- examples/
    |-- basic.ts
    |-- vnc-remote.ts
    +-- with-playwright.ts
```

The JSON-RPC Java server lives next door in `oculix-org/Operix/jvm-bridge/` and is built into the bundled JAR at npm publish time.

## 5. Detailed components

### 5.1 gateway.ts — JVM lifecycle

```typescript
import { spawn, ChildProcess } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';
import * as https from 'https';

const JAR_DIR = path.join(require('os').homedir(), '.oculix', 'lib');
const OCULIX_JAR_NAME = 'oculixapi-3.0.4.jar';
const OCULIX_JAR_URL = 'https://repo1.maven.org/maven2/io/github/oculix-org/oculixapi/3.0.4/oculixapi-3.0.4.jar';
// shipped inside the npm package (compiled at npm publish time)
const RPC_JAR_PATH = path.join(__dirname, '..', 'java-bin', 'operix-rpc-server.jar');

let jvmProcess: ChildProcess | null = null;
let requestId = 0;
let pendingRequests = new Map<number, { resolve: Function, reject: Function }>();

async function ensureOculixJar(): Promise<string> {
  const jarPath = path.join(JAR_DIR, OCULIX_JAR_NAME);
  if (!fs.existsSync(jarPath)) {
    fs.mkdirSync(JAR_DIR, { recursive: true });
    console.log(`[OculiX] Downloading ${OCULIX_JAR_NAME}...`);
    await downloadFile(OCULIX_JAR_URL, jarPath);
    console.log(`[OculiX] Saved to ${jarPath}`);
  }
  return jarPath;
}

export async function start(): Promise<void> {
  if (jvmProcess) return;
  const oculixJar = await ensureOculixJar();
  const sep = process.platform === 'win32' ? ';' : ':';
  const classpath = `${oculixJar}${sep}${RPC_JAR_PATH}`;

  jvmProcess = spawn('java', ['-cp', classpath, 'org.operix.rpc.Server'], {
    stdio: ['pipe', 'pipe', 'pipe']
  });

  jvmProcess.stdout!.on('data', (data: Buffer) => {
    const lines = data.toString().split('\n').filter(l => l.trim());
    for (const line of lines) {
      try {
        const response = JSON.parse(line);
        const pending = pendingRequests.get(response.id);
        if (pending) {
          pendingRequests.delete(response.id);
          if (response.error) pending.reject(new Error(response.error));
          else pending.resolve(response.result);
        }
      } catch {}
    }
  });

  process.on('exit', stop);
  await new Promise(resolve => setTimeout(resolve, 3000));
}

export async function call(className: string, method: string, ...args: any[]): Promise<any> {
  if (!jvmProcess) await start();
  const id = ++requestId;
  const request = JSON.stringify({ id, class: className, method, args }) + '\n';

  return new Promise((resolve, reject) => {
    pendingRequests.set(id, { resolve, reject });
    jvmProcess!.stdin!.write(request);
    setTimeout(() => {
      if (pendingRequests.has(id)) {
        pendingRequests.delete(id);
        reject(new Error(`Timeout calling ${className}.${method}`));
      }
    }, 30000);
  });
}

export function stop(): void {
  if (jvmProcess) {
    jvmProcess.kill();
    jvmProcess = null;
  }
}
```

### 5.2 index.ts — Public API

```typescript
import { call, start, stop } from './gateway';

export class Screen {
  async click(target: string): Promise<void> {
    await call('Screen', 'click', target);
  }

  async doubleClick(target: string): Promise<void> {
    await call('Screen', 'doubleClick', target);
  }

  async rightClick(target: string): Promise<void> {
    await call('Screen', 'rightClick', target);
  }

  async type(text: string): Promise<void> {
    await call('Screen', 'type', text);
  }

  async wait(target: string, timeout: number = 10): Promise<void> {
    await call('Screen', 'wait', target, timeout);
  }

  async exists(target: string, timeout: number = 3): Promise<boolean> {
    return await call('Screen', 'exists', target, timeout);
  }

  async find(target: string): Promise<any> {
    return await call('Screen', 'find', target);
  }

  async text(): Promise<string> {
    return await call('Screen', 'text');
  }

  async capture(): Promise<string> {
    return await call('Screen', 'capture');
  }
}

export class App {
  private name: string;
  constructor(name: string) { this.name = name; }

  static async open(path: string): Promise<App> {
    await call('App', 'open', path);
    return new App(path);
  }

  async focus(): Promise<void> {
    await call('App', 'focus', this.name);
  }

  async close(): Promise<void> {
    await call('App', 'close', this.name);
  }

  async window(): Promise<any> {
    return await call('App', 'window', this.name);
  }
}

export class Pattern {
  private path: string;
  private similarity: number = 0.7;

  constructor(path: string) { this.path = path; }

  similar(value: number): Pattern {
    this.similarity = value;
    return this;
  }

  exact(): Pattern {
    this.similarity = 0.99;
    return this;
  }
}

export class VNCScreen {
  // Backed by org.sikuli.vnc.VNCScreen
  static async start(host: string, port: number, password: string,
                     width: number, height: number): Promise<VNCScreen> {
    await call('org.sikuli.vnc.VNCScreen', 'start', host, port, password, width, height);
    return new VNCScreen();
  }

  async click(target: string): Promise<void> {
    await call('org.sikuli.vnc.VNCScreen', 'click', target);
  }

  async type(text: string): Promise<void> {
    await call('org.sikuli.vnc.VNCScreen', 'type', text);
  }

  async stop(): Promise<void> {
    await call('org.sikuli.vnc.VNCScreen', 'stop');
  }
}

export class ADBScreen {
  // Backed by org.sikuli.android.ADBScreen (NOT org.sikuli.script)
  static async start(adbPath: string): Promise<ADBScreen> {
    await call('org.sikuli.android.ADBScreen', 'start', adbPath);
    return new ADBScreen();
  }

  async click(target: string): Promise<void> {
    await call('org.sikuli.android.ADBScreen', 'click', target);
  }
}

export class PaddleOCR {
  // Backed by com.sikulix.ocr.PaddleOCREngine — OculiX's neural OCR engine
  static async getInstance(): Promise<PaddleOCR> {
    await call('com.sikulix.ocr.PaddleOCREngine', 'getInstance');
    return new PaddleOCR();
  }
}

export { start, stop } from './gateway';
```

## 6. Usage examples

### 6.1 Basic script

```javascript
const { Screen } = require('oculix');

async function main() {
  const screen = new Screen();
  await screen.click("login_button.png");
  await screen.type("admin");
  await screen.type("password123");
  await screen.click("submit.png");
  await screen.wait("dashboard.png", 10);
}

main().catch(console.error);
```

### 6.2 With Playwright (hybrid browser + desktop)

```javascript
const { chromium } = require('playwright');
const { Screen, App } = require('oculix');

async function test() {
  // Browser part — Playwright
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.goto('https://myapp.com/export');
  await page.click('#export-pdf');

  // Desktop part — OculiX
  const screen = new Screen();
  await screen.wait("save_dialog.png", 10);
  await screen.type("report.pdf");
  await screen.click("save_button.png");

  // Verify file was saved
  const app = await App.open("explorer.exe");
  await screen.wait("report_pdf_icon.png", 5);

  await browser.close();
}

test();
```

### 6.3 With Jest

```javascript
const { Screen, App } = require('oculix');

describe('Calculator', () => {
  let app;

  beforeAll(async () => {
    app = await App.open('calc');
  });

  afterAll(async () => {
    await app.close();
  });

  test('addition', async () => {
    const screen = new Screen();
    await screen.click("button_7.png");
    await screen.click("button_plus.png");
    await screen.click("button_3.png");
    await screen.click("button_equals.png");
    expect(await screen.exists("result_10.png")).toBeTruthy();
  });
});
```

### 6.4 VNC remote

```javascript
const { VNCScreen } = require('oculix');

async function testPOS() {
  const vnc = await VNCScreen.start("10.184.10.147", 5900, "", 1920, 1080);
  await vnc.click("login_button.png");
  await vnc.type("1234");
  await vnc.stop();
}

testPOS();
```

## 7. package.json

```json
{
  "name": "oculix",
  "version": "0.1.0",
  "description": "Visual automation for the real world — Node.js wrapper for OculiX",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "test": "jest",
    "prepublishOnly": "npm run build"
  },
  "keywords": ["visual-testing", "automation", "ocr", "sikuli", "oculix", "gui-testing", "desktop-testing", "vnc", "adb"],
  "author": "Julien Mer <julien.mer38@gmail.com>",
  "license": "MIT",
  "dependencies": {},
  "devDependencies": {
    "typescript": "^5.6.0",
    "@types/node": "^22.0.0",
    "jest": "^30.0.0",
    "ts-jest": "^29.2.0"
  },
  "engines": { "node": ">=20.0.0" },
  "repository": {
    "type": "git",
    "url": "https://github.com/oculix-org/Operix",
    "directory": "nodejs"
  }
}
```

## 8. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Java 17+ not installed | Clear message on first call + download link to Eclipse Temurin |
| JSON-RPC latency | Acceptable for visual testing (actions = seconds). PaddleOCR returns bulky JSON: needs benchmarking. Migrate to `node-java-bridge` if necessary |
| JSON-RPC JAR build | Maven build in `jvm-bridge/` triggered by `npm prepublish`, JAR shipped in `java-bin/` of the npm package |
| Apertix OpenCV natives | Bundled in oculixapi.jar, loaded on the JVM side. To be tested on Win/Mac M1/Linux in CI |
| async/await everywhere | Normal in Node, not a problem |
| TypeScript mandatory | No, the package compiles to JS, usable without TS |
| Concurrency with Playwright | Complementary, not competing (browser vs desktop) |

## 9. Roadmap

| Phase | Content | Duration |
|---|---|---|
| Phase 1 | Mini JSON-RPC Java server (`org.operix.rpc.Server`) + Maven build | 2 days |
| Phase 2 | TS gateway + Screen + Pattern + App | 2 days |
| Phase 3 | VNCScreen + ADBScreen (`org.sikuli.android`) + PaddleOCR (`com.sikulix.ocr`) | 1 day |
| Phase 4 | TypeScript types + Jest examples | 1 day |
| Phase 5 | Playwright hybrid example | 1 day |
| Phase 6 | npm publication + README | 1 day |

**Total: ~1.5 weeks** (the mini JSON-RPC server adds 2 days)

---

*"npm install oculix. Desktop visual testing meets the Node.js ecosystem."*
