/**
 * Integration tests for the Node.js wrapper.
 *
 * Spawns the actual JVM bridge JAR and exchanges JSON-RPC over stdio.
 * The JAR is built via `mvn package` in `../jvm-bridge/`.
 */

import * as fs from 'fs';
import * as path from 'path';
import { execSync } from 'child_process';
import { Bridge, BridgeError, RemoteObject } from '../src/bridge';

const REPO_ROOT = path.resolve(__dirname, '..', '..');
const LOCAL_JAR = path.join(REPO_ROOT, 'jvm-bridge', 'target', 'operix-jvm-bridge.jar');

function javaAvailable(): boolean {
  try {
    execSync('java -version', { stdio: 'pipe' });
    return true;
  } catch {
    return false;
  }
}

const skipReason =
  !fs.existsSync(LOCAL_JAR)
    ? `Bridge JAR not built: ${LOCAL_JAR}`
    : !javaAvailable()
    ? 'Java not on PATH'
    : null;

const maybeDescribe = skipReason ? describe.skip : describe;

maybeDescribe('Bridge integration', () => {
  let bridge: Bridge;

  beforeAll(async () => {
    bridge = new Bridge({ jarPath: LOCAL_JAR });
    await bridge.start();
  });

  afterAll(async () => {
    await bridge.stop();
  });

  test('construct StringBuilder and chain calls', async () => {
    const sb = await bridge.create('java.lang.StringBuilder', []);
    expect(sb).toBeInstanceOf(RemoteObject);

    const chained = await sb.call('append', 'hello ');
    expect((chained as RemoteObject).ref).toBe(sb.ref); // identity interning

    await sb.call('append', 'operix');
    expect(await sb.call('length')).toBe(12);
    expect(await sb.call('toString')).toBe('hello operix');
  });

  test('static method call returns string', async () => {
    expect(await bridge.callStatic('java.lang.Integer', 'toBinaryString', [42]))
      .toBe('101010');
  });

  test('overload resolution by argument type', async () => {
    expect(await bridge.callStatic('java.lang.Math', 'abs', [-7])).toBe(7);
    expect(await bridge.callStatic('java.lang.Math', 'max', [3.5, 2.5])).toBe(3.5);
  });

  test('unknown class raises BridgeError', async () => {
    await expect(bridge.create('no.such.Class', [])).rejects.toThrow(BridgeError);
  });

  test('release drops the ref', async () => {
    const sb = await bridge.create('java.lang.StringBuilder', []);
    const ref = sb.ref;
    await bridge.release(ref);
    await expect(bridge.call(ref, 'length', [])).rejects.toThrow(BridgeError);
  });
});
