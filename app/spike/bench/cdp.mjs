// A minimal CDP client over WebSocket. Unlike Playwright it enables no domains, so DevTools keeps nothing alive in the
// page (Playwright's Network domain, for one, retains every response body in the renderer).
export async function connectCdp(port) {
  let version;
  for (let i = 0; i < 200 && !version; i++) {
    try {
      version = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json();
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
  }
  const ws = new WebSocket(version.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    ws.onopen = resolve;
    ws.onerror = reject;
  });
  let nextId = 0;
  const pending = new Map();
  const listeners = new Map();
  ws.onmessage = (message) => {
    const data = JSON.parse(message.data);
    if (data.id && pending.has(data.id)) {
      const { resolve, reject } = pending.get(data.id);
      pending.delete(data.id);
      if (data.error) reject(new Error(`${data.error.message} (${data.error.code})`));
      else resolve(data.result);
    } else if (data.method) {
      for (const listener of listeners.get(data.method) ?? []) listener(data.params, data.sessionId);
    }
  };
  const send = (method, params = {}, sessionId) =>
    new Promise((resolve, reject) => {
      const id = ++nextId;
      pending.set(id, { resolve, reject });
      ws.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
    });
  const on = (method, listener) => {
    if (!listeners.has(method)) listeners.set(method, new Set());
    listeners.get(method).add(listener);
    return () => listeners.get(method).delete(listener);
  };
  return { send, on, close: () => ws.close() };
}

/** Attaches to the first page target (flat session) without enabling any domain. */
export async function attachPage(cdp) {
  for (let i = 0; i < 100; i++) {
    const { targetInfos } = await cdp.send('Target.getTargets');
    const page = targetInfos.find((t) => t.type === 'page');
    if (page) return (await cdp.send('Target.attachToTarget', { targetId: page.targetId, flatten: true })).sessionId;
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  throw new Error('no page target');
}

export async function evaluate(cdp, sessionId, expression) {
  const { result } = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true }, sessionId);
  return result.value;
}

/** One detailed memory-infra dump of every process of this browser. */
export async function memoryDump(cdp) {
  const events = [];
  const stopData = cdp.on('Tracing.dataCollected', (params) => events.push(...params.value));
  const complete = new Promise((resolve) => {
    const stop = cdp.on('Tracing.tracingComplete', () => {
      stop();
      resolve();
    });
  });
  await cdp.send('Tracing.start', {
    traceConfig: { includedCategories: ['disabled-by-default-memory-infra'], memoryDumpConfig: { triggers: [] } },
    transferMode: 'ReportEvents',
  });
  const result = await cdp.send('Tracing.requestMemoryDump', { deterministic: false, levelOfDetail: 'detailed' });
  await cdp.send('Tracing.end');
  await complete;
  stopData();
  return { success: result.success, events };
}
