/**
 * JSON-RPC client to the OculiX JVM bridge over stdin/stdout.
 *
 * The bridge is a fat JAR (~160 MB) downloaded once from GitHub Releases
 * into ~/.oculix/lib/ on first use.
 */

import { ChildProcess, spawn } from 'child_process';
import * as fs from 'fs';
import * as https from 'https';
import * as os from 'os';
import * as path from 'path';

export const BRIDGE_VERSION = '0.1.0';
const BRIDGE_JAR_NAME = `operix-jvm-bridge-${BRIDGE_VERSION}.jar`;
const BRIDGE_JAR_URL =
  `https://github.com/oculix-org/Operix/releases/download/` +
  `jvm-bridge-${BRIDGE_VERSION}/${BRIDGE_JAR_NAME}`;
const JAR_DIR = path.join(os.homedir(), '.oculix', 'lib');

export class BridgeError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'BridgeError';
  }
}

export interface RemoteRef {
  __ref: string;
  __class?: string;
}

export class RemoteObject {
  constructor(
    public readonly bridge: Bridge,
    public readonly ref: string,
    public readonly javaClass: string,
  ) {}

  async call(method: string, ...args: unknown[]): Promise<unknown> {
    return this.bridge.call(this.ref, method, args);
  }

  async release(): Promise<void> {
    return this.bridge.release(this.ref);
  }
}

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (err: Error) => void;
}

export interface BridgeOptions {
  /** Override the JAR path (skip auto-download). */
  jarPath?: string;
  /** Java executable, defaults to 'java' on PATH. */
  javaBin?: string;
}

export class Bridge {
  private proc: ChildProcess | null = null;
  private nextId = 0;
  private pending = new Map<number, PendingRequest>();
  private buffer = '';
  private readonly javaBin: string;
  private readonly jarPathOverride?: string;

  constructor(options: BridgeOptions = {}) {
    this.javaBin = options.javaBin ?? 'java';
    this.jarPathOverride = options.jarPath;
  }

  async start(): Promise<void> {
    if (this.proc) return;
    const jar = this.jarPathOverride ?? (await ensureJar());
    this.proc = spawn(this.javaBin, ['-jar', jar], {
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    this.proc.stdout!.setEncoding('utf-8');
    this.proc.stdout!.on('data', (chunk: string) => this.onStdout(chunk));
    this.proc.on('exit', (code) => this.onExit(code));
    this.proc.stderr!.setEncoding('utf-8');
    // Drain stderr so the JVM doesn't block on a full pipe; surface for debug
    this.proc.stderr!.on('data', () => {});
  }

  async stop(): Promise<void> {
    const proc = this.proc;
    if (!proc) return;
    this.proc = null;
    try {
      proc.stdin?.end();
    } catch {
      /* ignore */
    }
    proc.kill();
  }

  // --- public RPC operations ------------------------------------------------

  async create(className: string, args: unknown[]): Promise<RemoteObject> {
    const result = await this.send({ class: className, args: encodeArgs(args) });
    return decode(this, result) as RemoteObject;
  }

  async call(ref: string, method: string, args: unknown[]): Promise<unknown> {
    const result = await this.send({ ref, method, args: encodeArgs(args) });
    return decode(this, result);
  }

  async callStatic(className: string, method: string, args: unknown[]): Promise<unknown> {
    const result = await this.send({
      class: className,
      method,
      static: true,
      args: encodeArgs(args),
    });
    return decode(this, result);
  }

  async release(ref: string): Promise<void> {
    try {
      await this.send({ ref, release: true });
    } catch {
      /* best-effort */
    }
  }

  // --- internals ------------------------------------------------------------

  private async send(payload: Record<string, unknown>): Promise<unknown> {
    if (!this.proc) await this.start();
    const id = ++this.nextId;
    payload.id = id;
    return new Promise<unknown>((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.proc!.stdin!.write(JSON.stringify(payload) + '\n');
    });
  }

  private onStdout(chunk: string): void {
    this.buffer += chunk;
    let newlineIdx;
    while ((newlineIdx = this.buffer.indexOf('\n')) >= 0) {
      const line = this.buffer.slice(0, newlineIdx).trim();
      this.buffer = this.buffer.slice(newlineIdx + 1);
      if (!line) continue;
      this.handleLine(line);
    }
  }

  private handleLine(line: string): void {
    let response: { id?: number; result?: unknown; error?: string };
    try {
      response = JSON.parse(line);
    } catch (e) {
      // Not our JSON — ignore (might be JVM banner)
      return;
    }
    if (typeof response.id !== 'number') return;
    const pending = this.pending.get(response.id);
    if (!pending) return;
    this.pending.delete(response.id);
    if (response.error) {
      pending.reject(new BridgeError(response.error));
    } else {
      pending.resolve(response.result);
    }
  }

  private onExit(code: number | null): void {
    this.proc = null;
    const err = new BridgeError(`JVM bridge exited (code=${code})`);
    for (const p of this.pending.values()) p.reject(err);
    this.pending.clear();
  }
}

// --- value codec -------------------------------------------------------------

function encodeArgs(args: unknown[]): unknown[] {
  return args.map(encode);
}

function encode(v: unknown): unknown {
  if (v instanceof RemoteObject) return { __ref: v.ref };
  return v;
}

function decode(bridge: Bridge, v: unknown): unknown {
  if (v && typeof v === 'object' && '__ref' in (v as object)) {
    const r = v as RemoteRef;
    return new RemoteObject(bridge, r.__ref, r.__class ?? '?');
  }
  return v;
}

// --- JAR distribution --------------------------------------------------------

async function ensureJar(): Promise<string> {
  const jarPath = path.join(JAR_DIR, BRIDGE_JAR_NAME);
  if (fs.existsSync(jarPath)) return jarPath;
  fs.mkdirSync(JAR_DIR, { recursive: true });
  console.log(`[OculiX] Downloading ${BRIDGE_JAR_NAME} (~160 MB)…`);
  await downloadFile(BRIDGE_JAR_URL, jarPath);
  console.log(`[OculiX] Saved to ${jarPath}`);
  return jarPath;
}

function downloadFile(url: string, dest: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);
    const onError = (err: Error) => {
      file.close();
      fs.unlink(dest, () => reject(err));
    };
    https
      .get(url, (response) => {
        if (response.statusCode === 302 || response.statusCode === 301) {
          // GitHub Releases redirects to S3 — follow once.
          file.close();
          return downloadFile(response.headers.location!, dest).then(resolve, reject);
        }
        if (response.statusCode !== 200) {
          return onError(new Error(`HTTP ${response.statusCode} for ${url}`));
        }
        response.pipe(file);
        file.on('finish', () => file.close(() => resolve()));
      })
      .on('error', onError);
  });
}

// --- module-wide singleton ---------------------------------------------------

let _defaultBridge: Bridge | null = null;

export function defaultBridge(): Bridge {
  if (!_defaultBridge) _defaultBridge = new Bridge();
  return _defaultBridge;
}
