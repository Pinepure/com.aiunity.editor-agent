import fs from 'node:fs/promises';
import path from 'node:path';
import { spawn } from 'node:child_process';

function buildUrl(baseUrl, route) {
  return new URL(route, baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`);
}

async function fetchJson(baseUrl, route, { method = 'GET' } = {}) {
  const response = await fetch(buildUrl(baseUrl, route), { method });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`Chrome endpoint ${route} failed with ${response.status}: ${text.trim()}`);
  }
  return text.trim() ? JSON.parse(text) : {};
}

async function fetchText(baseUrl, route, { method = 'GET' } = {}) {
  const response = await fetch(buildUrl(baseUrl, route), { method });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`Chrome endpoint ${route} failed with ${response.status}: ${text.trim()}`);
  }
  return text;
}

export async function getChromeVersion(config) {
  return fetchJson(config.chromeBaseUrl, '/json/version');
}

export async function listTargets(config) {
  return fetchJson(config.chromeBaseUrl, '/json/list');
}

export async function createTarget(config, url) {
  return fetchJson(
    config.chromeBaseUrl,
    `/json/new?${encodeURIComponent(url)}`,
    { method: 'PUT' },
  );
}

export async function activateTarget(config, targetId) {
  return fetchText(config.chromeBaseUrl, `/json/activate/${encodeURIComponent(targetId)}`);
}

export async function closeTarget(config, targetId) {
  return fetchText(config.chromeBaseUrl, `/json/close/${encodeURIComponent(targetId)}`);
}

export async function resolveChromeExecutable(explicitPath) {
  const candidates = [];
  if (explicitPath) {
    candidates.push(explicitPath);
  }
  if (process.env.CHROME_PATH) {
    candidates.push(process.env.CHROME_PATH);
  }
  candidates.push(
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/Applications/Chromium.app/Contents/MacOS/Chromium',
    'google-chrome',
    'chromium',
    'chromium-browser',
    'chrome',
  );

  for (const candidate of candidates) {
    if (!candidate) {
      continue;
    }
    if (!candidate.includes(path.sep)) {
      return candidate;
    }
    try {
      await fs.access(candidate);
      return candidate;
    } catch {
      // continue
    }
  }
  throw new Error('Chrome executable not found. Pass --chrome-executable or set CHROME_PATH.');
}

export async function launchChrome(config, options = {}) {
  const executable = await resolveChromeExecutable(options.chromeExecutable || config.chromeExecutable);
  const userDataDir = path.resolve(
    options.userDataDir || path.join(config.stateDirectoryPath, 'chrome-profile'),
  );
  await fs.mkdir(userDataDir, { recursive: true });
  const args = [
    `--remote-debugging-port=${config.chromePort}`,
    `--user-data-dir=${userDataDir}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-background-networking',
  ];
  if (options.headless) {
    args.push('--headless=new');
  }
  for (const extraArg of options.extraArgs || []) {
    args.push(String(extraArg));
  }
  if (options.url) {
    args.push(String(options.url));
  }

  const child = spawn(executable, args, {
    detached: true,
    stdio: 'ignore',
  });
  child.unref();
  const version = await waitForChromeReady(config, { timeoutMs: Number(options.timeoutMs) || 15000 });
  return {
    executable,
    pid: child.pid,
    userDataDir,
    args,
    version,
  };
}

export async function waitForChromeReady(config, { timeoutMs = 15000 } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastError = null;
  while (Date.now() < deadline) {
    try {
      return await getChromeVersion(config);
    } catch (error) {
      lastError = error;
      await sleep(300);
    }
  }
  throw new Error(`Chrome did not become ready in time: ${lastError?.message ?? 'unknown error'}`);
}

export class ChromeSession {
  constructor(webSocketUrl) {
    this.webSocketUrl = webSocketUrl;
    this.socket = null;
    this.nextId = 1;
    this.pending = new Map();
    this.eventQueues = new Map();
    this.eventWaiters = new Map();
  }

  async connect({ timeoutMs = 10000 } = {}) {
    await new Promise((resolve, reject) => {
      let settled = false;
      const timer = setTimeout(() => {
        if (!settled) {
          settled = true;
          reject(new Error(`Timed out connecting to ${this.webSocketUrl}`));
        }
      }, timeoutMs);
      const socket = new WebSocket(this.webSocketUrl);
      this.socket = socket;
      socket.addEventListener('open', () => {
        if (settled) {
          return;
        }
        settled = true;
        clearTimeout(timer);
        resolve();
      }, { once: true });
      socket.addEventListener('error', (event) => {
        if (settled) {
          return;
        }
        settled = true;
        clearTimeout(timer);
        reject(new Error(`WebSocket connect failed for ${this.webSocketUrl}: ${String(event.type)}`));
      }, { once: true });
      socket.addEventListener('message', (event) => {
        this.#handleMessage(event.data);
      });
      socket.addEventListener('close', () => {
        this.#rejectAllPending(new Error(`WebSocket closed for ${this.webSocketUrl}`));
      });
    });
  }

  async close() {
    if (!this.socket) {
      return;
    }
    const socket = this.socket;
    if (socket.readyState === WebSocket.CLOSED) {
      return;
    }
    await new Promise((resolve) => {
      let settled = false;
      const finish = () => {
        if (settled) {
          return;
        }
        settled = true;
        resolve();
      };
      socket.addEventListener('close', finish, { once: true });
      setTimeout(finish, 500);
      socket.close();
    });
  }

  send(method, params = {}, { timeoutMs = 10000 } = {}) {
    if (!this.socket || this.socket.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket session is not connected.');
    }
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}`));
      }, timeoutMs);
      this.pending.set(id, { resolve, reject, timer, method });
      this.socket.send(JSON.stringify({ id, method, params }));
    });
  }

  waitForEvent(method, { timeoutMs = 10000 } = {}) {
    const queued = this.eventQueues.get(method);
    if (queued && queued.length > 0) {
      return Promise.resolve(queued.shift());
    }
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        const waiters = this.eventWaiters.get(method) ?? [];
        this.eventWaiters.set(
          method,
          waiters.filter((entry) => entry.resolve !== resolve),
        );
        reject(new Error(`Timed out waiting for event ${method}`));
      }, timeoutMs);
      const waiters = this.eventWaiters.get(method) ?? [];
      waiters.push({ resolve, reject, timer });
      this.eventWaiters.set(method, waiters);
    });
  }

  #handleMessage(rawData) {
    const payload = JSON.parse(String(rawData));
    if (Object.prototype.hasOwnProperty.call(payload, 'id')) {
      const pending = this.pending.get(payload.id);
      if (!pending) {
        return;
      }
      clearTimeout(pending.timer);
      this.pending.delete(payload.id);
      if (payload.error) {
        pending.reject(new Error(`${pending.method} failed: ${payload.error.message}`));
      } else {
        pending.resolve(payload.result ?? {});
      }
      return;
    }
    if (!payload.method) {
      return;
    }
    const waiters = this.eventWaiters.get(payload.method) ?? [];
    if (waiters.length > 0) {
      const waiter = waiters.shift();
      clearTimeout(waiter.timer);
      waiter.resolve(payload.params ?? {});
      this.eventWaiters.set(payload.method, waiters);
      return;
    }
    const queue = this.eventQueues.get(payload.method) ?? [];
    queue.push(payload.params ?? {});
    this.eventQueues.set(payload.method, queue);
  }

  #rejectAllPending(error) {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this.pending.clear();
    for (const waiters of this.eventWaiters.values()) {
      for (const waiter of waiters) {
        clearTimeout(waiter.timer);
        waiter.reject(error);
      }
    }
    this.eventWaiters.clear();
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
