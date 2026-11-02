/* Rahgir browser tracker. No dependencies, ~4 KB gzipped, never throws into the host app. */

export interface RahgirConfig {
  /** Project key registered in the Collector. */
  projectKey: string;
  /** Collector base URL, e.g. https://track.mydomain.ir */
  collectorUrl: string;
  /** Record clicks, form submits and input changes automatically. Default true. */
  autocapture?: boolean;
  /** Record page views on load and on client-side navigation. Default true. */
  pageviews?: boolean;
  /** Origins of your API. Requests from fetch/XHR to these get session headers so the
   *  dashboard can link API calls to the visitor. Your API's CORS must allow
   *  X-Rahgir-Session and X-Rahgir-Anon. Default: [] (same-origin requests only). */
  linkApiOrigins?: string[];
  /** Respect the browser's Do-Not-Track setting. Default true. */
  respectDoNotTrack?: boolean;
  /** Minutes of inactivity before a new session starts. Default 30. */
  sessionTimeoutMinutes?: number;
  /** Send when this many events are queued. Default 20. */
  batchSize?: number;
  /** Send at least this often (ms). Default 5000. */
  flushIntervalMs?: number;
  /** Extra property names that must never be sent. */
  sensitiveKeys?: string[];
  /** Log to the console. Default false. */
  debug?: boolean;
}

export type Props = Record<string, unknown>;

interface RemoteConfig { enabled?: boolean; sampleRate?: number; autocapture?: boolean | null }

interface WireEvent {
  id: string;
  type: string;
  name?: string;
  ts: string;
  userId?: string;
  anonId: string;
  sessionId: string;
  url?: string;
  path?: string;
  referrer?: string;
  title?: string;
  screen?: string;
  lang?: string;
  props?: Props;
}

const STORAGE = { anon: "rahgir_anon", user: "rahgir_user", session: "rahgir_session", config: "rahgir_config" };
const DEFAULT_SENSITIVE = ["password", "passwd", "pwd", "pass", "secret", "token", "authorization", "otp",
  "cvv", "cvv2", "card", "cardnumber", "pan", "nationalcode", "codemelli", "iban", "sheba", "pin"];
const IGNORE_ATTR = "data-rahgir-ignore";
const NAME_ATTR = "data-rahgir";

function uuid(): string {
  const c = (globalThis as any).crypto;
  if (c?.randomUUID) return c.randomUUID();
  const b = new Uint8Array(16);
  c?.getRandomValues ? c.getRandomValues(b) : b.forEach((_, i) => (b[i] = Math.floor(Math.random() * 256)));
  b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80;
  const h = Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

function store(kind: "local" | "session") {
  try { return kind === "local" ? window.localStorage : window.sessionStorage; } catch { return null; }
}

const idle: (cb: () => void) => void =
  typeof window !== "undefined" && "requestIdleCallback" in window
    ? (cb) => (window as any).requestIdleCallback(cb, { timeout: 2000 })
    : (cb) => setTimeout(cb, 1);

export class RahgirTracker {
  private cfg: Required<Omit<RahgirConfig, "sensitiveKeys">> & { sensitiveKeys: string[] };
  private queue: WireEvent[] = [];
  private timer: ReturnType<typeof setInterval> | null = null;
  private anonId = "";
  private userId: string | undefined;
  private session = { id: "", last: 0 };
  private disabled = false;
  private autocaptureOff = false;
  /** Hold the first send until central settings are known (max 3 s), so a switched-off project sends nothing. */
  private remoteReady = false;
  private currentPage: { path: string; start: number; maxScroll: number } | null = null;
  private teardown: Array<() => void> = [];

  constructor(config: RahgirConfig) {
    this.cfg = {
      autocapture: true, pageviews: true, linkApiOrigins: [], respectDoNotTrack: true,
      sessionTimeoutMinutes: 30, batchSize: 20, flushIntervalMs: 5000, debug: false,
      ...config,
      sensitiveKeys: [...DEFAULT_SENSITIVE, ...(config.sensitiveKeys ?? [])].map((k) => k.toLowerCase()),
    };
    this.cfg.collectorUrl = this.cfg.collectorUrl.replace(/\/+$/, "");
  }

  /** Start tracking. Safe to call more than once. */
  start(): this {
    if (typeof window === "undefined" || this.timer) return this;
    try {
      const nav = navigator as any;
      if (this.cfg.respectDoNotTrack && (nav.doNotTrack === "1" || (window as any).doNotTrack === "1" || nav.globalPrivacyControl)) {
        this.disabled = true;
        this.log("disabled by Do-Not-Track");
        return this;
      }
      const local = store("local");
      this.anonId = local?.getItem(STORAGE.anon) || uuid();
      local?.setItem(STORAGE.anon, this.anonId);
      this.userId = local?.getItem(STORAGE.user) || undefined;
      this.restoreSession();

      this.timer = setInterval(() => this.flush(), this.cfg.flushIntervalMs);
      const onHide = () => { if (document.visibilityState === "hidden") { this.leavePage(); this.flush(true); } };
      document.addEventListener("visibilitychange", onHide);
      window.addEventListener("pagehide", onHide);
      this.teardown.push(() => { document.removeEventListener("visibilitychange", onHide); window.removeEventListener("pagehide", onHide); });

      if (this.cfg.pageviews) this.watchNavigation();
      if (this.cfg.autocapture) this.watchDom();
      this.patchNetwork();
      this.watchScroll();
      this.applyRemote(this.cachedRemote());
      this.loadRemote();
    } catch (e) {
      this.log("start failed", e);
    }
    return this;
  }

  /** Stop tracking and remove all listeners. */
  stop() {
    this.flush(true);
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.teardown.forEach((f) => { try { f(); } catch { /* ignore */ } });
    this.teardown = [];
  }

  /** Record a custom event, e.g. track("AddToCart", { productId: 12 }). */
  track(name: string, props?: Props) {
    this.push("custom", name, props);
  }

  /** Call after login. Links this visitor's history to the user. */
  identify(userId: string, traits?: Props) {
    if (!userId) return;
    this.userId = String(userId).slice(0, 128);
    store("local")?.setItem(STORAGE.user, this.userId);
    this.push("identify", undefined, traits);
  }

  /** Call on logout. Starts a fresh anonymous visitor. */
  reset() {
    this.flush();
    this.userId = undefined;
    this.anonId = uuid();
    const local = store("local");
    local?.removeItem(STORAGE.user);
    local?.setItem(STORAGE.anon, this.anonId);
    this.newSession();
  }

  /** Manually record a page view (automatic by default). */
  page(props?: Props) {
    this.leavePage();
    this.currentPage = { path: location.pathname, start: Date.now(), maxScroll: 0 };
    this.push("pageview", undefined, props);
  }

  /** Headers to add to your own API calls (axios, ky, ...) so they link to this visitor. */
  headers(): Record<string, string> {
    if (this.disabled || !this.anonId) return {};
    this.touchSession();
    return { "X-Rahgir-Session": this.session.id, "X-Rahgir-Anon": this.anonId };
  }

  get visitorId() { return this.anonId; }
  get sessionId() { return this.session.id; }

  // ---------------------------------------------------------------------------

  private push(type: string, name?: string, props?: Props) {
    if (this.disabled || typeof window === "undefined" || !this.anonId) return;
    try {
      this.touchSession();
      const e: WireEvent = {
        id: uuid(), type, ts: new Date().toISOString(),
        anonId: this.anonId, sessionId: this.session.id,
        url: location.href, path: location.pathname,
        referrer: document.referrer || undefined, title: document.title?.slice(0, 256) || undefined,
        screen: `${screen.width}x${screen.height}`, lang: navigator.language,
      };
      if (name) e.name = name.slice(0, 200);
      if (this.userId) e.userId = this.userId;
      if (props) e.props = this.clean(props) as Props;
      this.queue.push(e);
      this.log(type, name ?? "", e);
      if (this.queue.length >= this.cfg.batchSize) idle(() => this.flush());
      if (this.queue.length > 1000) this.queue.splice(0, this.queue.length - 1000);
    } catch (err) {
      this.log("push failed", err);
    }
  }

  /** Remove sensitive keys and cap depth/size. */
  private clean(value: unknown, depth = 0): unknown {
    if (value == null || typeof value === "number" || typeof value === "boolean") return value;
    if (typeof value === "string") return value.slice(0, 1000);
    if (depth > 4) return "[…]";
    if (Array.isArray(value)) return value.slice(0, 50).map((v) => this.clean(v, depth + 1));
    if (typeof value === "object") {
      const out: Props = {};
      let n = 0;
      for (const [k, v] of Object.entries(value as Props)) {
        if (++n > 50) break;
        const key = k.toLowerCase().replace(/[-_ ]/g, "");
        out[k] = this.cfg.sensitiveKeys.some((s) => key === s || (s.length >= 5 && key.includes(s))) ? "***" : this.clean(v, depth + 1);
      }
      return out;
    }
    return undefined;
  }

  flush(unloading = false) {
    if (!this.queue.length || this.disabled) return;
    if (!this.remoteReady && !unloading) return;
    const events = this.queue.splice(0, 200);
    const body = JSON.stringify({ events, source: "web", sdk: "rahgir-next/1.0" });
    const url = `${this.cfg.collectorUrl}/v1/batch?k=${encodeURIComponent(this.cfg.projectKey)}`;
    try {
      // text/plain keeps it a "simple" request: no CORS preflight, nothing exposed to the page.
      const blob = new Blob([body], { type: "text/plain" });
      if (unloading && navigator.sendBeacon && navigator.sendBeacon(url, blob)) return;
      origFetch?.call(window, url, { method: "POST", body, keepalive: body.length < 60000, mode: "no-cors", credentials: "omit",
        headers: { "Content-Type": "text/plain" } }).catch(() => { /* dropped */ });
    } catch { /* never throw */ }
    if (this.queue.length) idle(() => this.flush(unloading));
  }

  // --- central settings (set in the Rahgir dashboard, applied to every install) ---

  private cachedRemote(): RemoteConfig | null {
    try {
      const raw = store("local")?.getItem(STORAGE.config);
      const c = raw ? JSON.parse(raw) : null;
      return c && Date.now() - c.at < 24 * 3600000 ? c.cfg : null;
    } catch { return null; }
  }

  private loadRemote() {
    const ready = () => { this.remoteReady = true; };
    if (this.cachedRemote()) ready();
    setTimeout(ready, 3000);
    const url = `${this.cfg.collectorUrl}/v1/config?k=${encodeURIComponent(this.cfg.projectKey)}`;
    origFetch?.call(window, url, { credentials: "omit", mode: "cors" })
      .then((r) => (r.ok ? r.json() : null))
      .then((cfg: RemoteConfig | null) => {
        if (!cfg) return;
        store("local")?.setItem(STORAGE.config, JSON.stringify({ at: Date.now(), cfg }));
        this.applyRemote(cfg);
      })
      .catch(() => { /* keep local settings */ })
      .finally(ready);
  }

  private applyRemote(cfg: RemoteConfig | null) {
    if (!cfg) return;
    this.autocaptureOff = cfg.autocapture === false;
    // Sampling is decided once per visitor so a visitor is either fully tracked or not at all.
    const bucket = parseInt(this.anonId.replace(/-/g, "").slice(0, 8), 16) / 0xffffffff;
    const off = cfg.enabled === false || (typeof cfg.sampleRate === "number" && !(bucket < cfg.sampleRate));
    if (off && !this.disabled) {
      this.disabled = true;
      this.queue = [];
      this.log("disabled by central settings");
    } else if (!off && this.disabled) {
      this.disabled = false;
    }
  }

  // --- sessions -------------------------------------------------------------

  private restoreSession() {
    try {
      const raw = store("local")?.getItem(STORAGE.session);
      if (raw) {
        const s = JSON.parse(raw);
        if (s?.id && Date.now() - s.last < this.cfg.sessionTimeoutMinutes * 60000) { this.session = s; return; }
      }
    } catch { /* ignore */ }
    this.newSession();
  }

  private newSession() {
    this.session = { id: uuid(), last: Date.now() };
    store("local")?.setItem(STORAGE.session, JSON.stringify(this.session));
  }

  private touchSession() {
    const now = Date.now();
    if (!this.session.id || now - this.session.last > this.cfg.sessionTimeoutMinutes * 60000) this.newSession();
    else if (now - this.session.last > 5000) {
      this.session.last = now;
      store("local")?.setItem(STORAGE.session, JSON.stringify(this.session));
    }
  }

  // --- page views (works for App Router and Pages Router) --------------------

  private watchNavigation() {
    let last = location.pathname + location.search;
    const check = () => {
      const now = location.pathname + location.search;
      if (now !== last) { last = now; idle(() => this.page()); }
    };
    const h = history as any;
    const wrap = (m: "pushState" | "replaceState") => {
      const orig = h[m];
      h[m] = function (this: History, ...args: any[]) { const r = orig.apply(this, args); setTimeout(check, 0); return r; };
      this.teardown.push(() => { h[m] = orig; });
    };
    wrap("pushState"); wrap("replaceState");
    window.addEventListener("popstate", check);
    this.teardown.push(() => window.removeEventListener("popstate", check));
    this.page();
  }

  private leavePage() {
    const p = this.currentPage;
    if (!p) return;
    this.currentPage = null;
    const seconds = Math.round((Date.now() - p.start) / 1000);
    if (seconds < 1) return;
    this.push("pageleave", undefined, { duration: seconds, scroll: p.maxScroll, leftPath: p.path });
  }

  private watchScroll() {
    let ticking = false;
    const onScroll = () => {
      if (ticking || !this.currentPage) return;
      ticking = true;
      requestAnimationFrame(() => {
        ticking = false;
        const d = document.documentElement;
        const max = d.scrollHeight - d.clientHeight;
        const pct = max > 0 ? Math.min(100, Math.round((d.scrollTop / max) * 100)) : 100;
        if (this.currentPage && pct > this.currentPage.maxScroll) this.currentPage.maxScroll = pct;
      });
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    this.teardown.push(() => window.removeEventListener("scroll", onScroll));
  }

  // --- autocapture ------------------------------------------------------------

  private watchDom() {
    const onClick = (ev: MouseEvent) => {
      if (this.autocaptureOff) return;
      const el = (ev.target as Element | null)?.closest?.(`a,button,[role=button],input[type=submit],input[type=button],[${NAME_ATTR}]`);
      if (!el || this.ignored(el)) return;
      const info = this.describe(el);
      idle(() => this.push("click", info.name, info.props));
    };
    const onSubmit = (ev: Event) => {
      if (this.autocaptureOff) return;
      const form = ev.target as HTMLFormElement;
      if (!form || this.ignored(form)) return;
      const name = form.getAttribute(NAME_ATTR) || form.getAttribute("name") || form.id || undefined;
      const fields = Array.from(form.elements).map((f: any) => f.name).filter(Boolean).slice(0, 30);
      idle(() => this.push("submit", name, { form: name, action: this.safePath(form.getAttribute("action")), fields }));
    };
    const onChange = (ev: Event) => {
      if (this.autocaptureOff) return;
      const el = ev.target as HTMLInputElement;
      if (!el || !el.tagName || this.ignored(el)) return;
      // Values are never sent: only which field changed.
      const name = el.getAttribute(NAME_ATTR) || el.name || el.id || undefined;
      idle(() => this.push("change", name, { tag: el.tagName.toLowerCase(), inputType: el.type, field: name }));
    };
    document.addEventListener("click", onClick, { capture: true, passive: true });
    document.addEventListener("submit", onSubmit, { capture: true });
    document.addEventListener("change", onChange, { capture: true, passive: true });
    this.teardown.push(() => {
      document.removeEventListener("click", onClick, { capture: true } as any);
      document.removeEventListener("submit", onSubmit, { capture: true } as any);
      document.removeEventListener("change", onChange, { capture: true } as any);
    });
  }

  private ignored(el: Element) {
    return !!el.closest(`[${IGNORE_ATTR}],.rahgir-ignore`) || (el as HTMLInputElement).type === "password";
  }

  private describe(el: Element) {
    const tag = el.tagName.toLowerCase();
    const explicit = el.getAttribute(NAME_ATTR);
    const text = (el as HTMLElement).innerText?.trim().replace(/\s+/g, " ").slice(0, 60)
      || el.getAttribute("aria-label") || (el as HTMLInputElement).value?.slice?.(0, 60) || undefined;
    const props: Props = { tag, text, id: el.id || undefined };
    if (tag === "a") props.href = this.safePath((el as HTMLAnchorElement).getAttribute("href"));
    const cls = (el.getAttribute("class") || "").split(/\s+/).filter(Boolean).slice(0, 5).join(" ");
    if (cls) props.classes = cls.slice(0, 200);
    return { name: explicit || text, props };
  }

  /** Strip query strings from links/actions: they often carry tokens. */
  private safePath(href: string | null) {
    if (!href) return undefined;
    try { const u = new URL(href, location.href); return u.origin === location.origin ? u.pathname : u.origin + u.pathname; }
    catch { return undefined; }
  }

  // --- link API calls to the visitor -----------------------------------------

  private patchNetwork() {
    const allowed = (url: string) => {
      try {
        const u = new URL(url, location.href);
        if (u.href.startsWith(this.cfg.collectorUrl)) return false;
        return u.origin === location.origin || this.cfg.linkApiOrigins.some((o) => u.origin === o.replace(/\/+$/, ""));
      } catch { return false; }
    };
    const self = this;
    if (origFetch) {
      const patched = function (input: RequestInfo | URL, init?: RequestInit) {
        try {
          const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
          if (allowed(url)) {
            const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
            for (const [k, v] of Object.entries(self.headers())) if (!headers.has(k)) headers.set(k, v);
            init = { ...init, headers };
          }
        } catch { /* fall through untouched */ }
        return origFetch!.call(window, input as any, init);
      };
      window.fetch = patched as typeof fetch;
      this.teardown.push(() => { window.fetch = origFetch!; });
    }
    const xhrOpen = XMLHttpRequest.prototype.open;
    const xhrSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (this: any, method: string, url: string | URL, ...rest: any[]) {
      this.__rahgir = allowed(String(url));
      return (xhrOpen as any).call(this, method, url, ...rest);
    } as any;
    XMLHttpRequest.prototype.send = function (this: any, body?: any) {
      try { if (this.__rahgir) for (const [k, v] of Object.entries(self.headers())) this.setRequestHeader(k, v); } catch { /* ignore */ }
      return xhrSend.call(this, body);
    };
    this.teardown.push(() => { XMLHttpRequest.prototype.open = xhrOpen; XMLHttpRequest.prototype.send = xhrSend; });
  }

  private log(...args: unknown[]) {
    if (this.cfg.debug) console.debug("[rahgir]", ...args);
  }
}

const origFetch: typeof fetch | undefined = typeof window !== "undefined" ? window.fetch : undefined;

let instance: RahgirTracker | null = null;

/** Create (once) and start the global tracker. */
export function init(config: RahgirConfig): RahgirTracker {
  if (!instance) instance = new RahgirTracker(config).start();
  return instance;
}

/** The global tracker, or a no-op stand-in during SSR / before init. */
export const rahgir = {
  track: (name: string, props?: Props) => instance?.track(name, props),
  identify: (userId: string, traits?: Props) => instance?.identify(userId, traits),
  reset: () => instance?.reset(),
  page: (props?: Props) => instance?.page(props),
  headers: (): Record<string, string> => instance?.headers() ?? {},
  flush: () => instance?.flush(),
  get instance() { return instance; },
};
