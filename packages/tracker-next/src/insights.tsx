"use client";
/*
 * Show a project's own Tracker data inside the project (e.g. its admin panel).
 * Data comes from your backend's MapTrackerInsights(...) route, never directly from the Collector,
 * so the read key stays on the server and your own auth protects it.
 *
 *   <TrackerUserActivity baseUrl="/api/admin/tracker" userId={user.id} />
 *   <TrackerOverview baseUrl="/api/admin/tracker" days={30} />
 */
import { useEffect, useState, type CSSProperties } from "react";

export interface InsightsSource {
  /** URL of the backend route mapped with MapTrackerInsights, e.g. "/api/admin/tracker" or "https://api.verti.ir/api/admin/tracker". */
  baseUrl: string;
  /** Extra headers, e.g. { Authorization: `Bearer ${token}` } when your API uses JWT. */
  headers?: Record<string, string>;
  /** "include" when the API is on another origin and uses cookies. Default "same-origin". */
  credentials?: RequestCredentials;
}

export interface InsightsEvent {
  id: string; source: string; type: string; name?: string; ts: string;
  userId?: string; anonymousId?: string; sessionId?: string; path?: string; title?: string;
  route?: string; method?: string; statusCode?: number; ip?: string; browser?: string; os?: string; device?: string;
  properties?: string;
}

/** Low-level hook for building your own UI. */
export function useTrackerInsights<T = any>(src: InsightsSource, endpoint: "overview" | "events" | "users" | "user" | "session",
  params?: Record<string, string | number | undefined>) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const key = JSON.stringify([src.baseUrl, endpoint, params]);
  useEffect(() => {
    let alive = true;
    const qs = new URLSearchParams();
    for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined && v !== "") qs.set(k, String(v));
    setData(null); setError(null);
    fetch(`${src.baseUrl.replace(/\/+$/, "")}/${endpoint}${qs.toString() ? `?${qs}` : ""}`,
      { headers: src.headers, credentials: src.credentials ?? "same-origin", cache: "no-store" })
      .then(async (r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
      .then((d) => alive && setData(d))
      .catch((e) => alive && setError(String(e?.message ?? e)));
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  return { data, error, loading: !data && !error };
}

const fa = (n: unknown) => new Intl.NumberFormat("fa-IR").format(Number(n ?? 0));
const toDate = (s: string) => new Date(/Z|[+-]\d\d:?\d\d$/.test(s) ? s : s + "Z");
const time = (s: string) => new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short" }).format(toDate(s));
const json = (s?: string) => { try { return s ? JSON.parse(s) : {}; } catch { return {}; } };

const LABELS: Record<string, string> = {
  pageview: "بازدید صفحه", pageleave: "خروج از صفحه", click: "کلیک", submit: "ارسال فرم",
  change: "تغییر فیلد", api: "درخواست API", custom: "رویداد", identify: "ورود",
};

/** One readable Persian sentence per event (rendered as text, never as HTML). */
export function describeEvent(e: InsightsEvent): string {
  const p = json(e.properties);
  switch (e.type) {
    case "pageview": return `صفحه ${e.path ?? ""} را دید${e.title ? ` (${e.title})` : ""}`;
    case "pageleave": return `از ${p.leftPath ?? e.path ?? ""} خارج شد بعد از ${fa(p.duration)} ثانیه`;
    case "click": return `روی «${e.name ?? p.text ?? "?"}» کلیک کرد`;
    case "submit": return `فرم ${e.name ? `«${e.name}» ` : ""}را ارسال کرد`;
    case "change": return `فیلد «${e.name ?? "?"}» را تغییر داد`;
    case "api": return `${e.name ?? `${e.method ?? ""} ${e.route ?? e.path ?? ""}`}${e.statusCode ? ` (${e.statusCode})` : ""}`;
    case "identify": return "وارد حساب شد";
    default: return `رویداد «${e.name ?? e.type}»`;
  }
}

const box: CSSProperties = { direction: "rtl", fontFamily: "inherit", fontSize: 14, lineHeight: 1.6 };
const muted: CSSProperties = { color: "#667085", fontSize: 12.5 };
const badge: CSSProperties = { display: "inline-block", fontSize: 11.5, padding: "0 8px", borderRadius: 99, background: "rgba(37,99,235,.1)", color: "#2563eb", marginInlineEnd: 6 };

function State({ error, loading }: { error: string | null; loading: boolean }) {
  if (error) return <div style={{ ...box, ...muted }}>دریافت اطلاعات رهگیری ممکن نشد ({error}).</div>;
  if (loading) return <div style={{ ...box, ...muted }}>در حال بارگذاری…</div>;
  return null;
}

/** Everything one user did: pages, clicks, forms and API calls, newest first. */
export function TrackerUserActivity({ userId, limit = 100, ...src }: InsightsSource & { userId: string; limit?: number }) {
  const { data, error, loading } = useTrackerInsights<{ events: InsightsEvent[]; stats?: any }>(src, "user", { id: userId });
  if (!data) return <State error={error} loading={loading} />;
  const events = data.events.slice(0, limit);
  return (
    <div style={box}>
      {data.stats && (
        <div style={{ ...muted, marginBottom: 8 }}>
          {fa(data.stats.sessions)} نشست · {fa(data.stats.pageViews)} بازدید صفحه · {fa(data.stats.apiCalls)} درخواست API
        </div>
      )}
      {!events.length && <div style={muted}>فعالیتی ثبت نشده است.</div>}
      {events.map((e) => (
        <div key={e.id} style={{ padding: "6px 0", borderBottom: "1px dashed rgba(127,127,127,.25)" }}>
          <span style={badge}>{LABELS[e.type] ?? e.type}</span>
          {describeEvent(e)}
          <div style={muted}>{time(e.ts)}{e.device ? ` · ${e.device} · ${e.os ?? ""} · ${e.browser ?? ""}` : ""}</div>
        </div>
      ))}
    </div>
  );
}

/** Headline numbers and top pages/actions for the last N days. */
export function TrackerOverview({ days = 30, ...src }: InsightsSource & { days?: number }) {
  const [range] = useState(() => { const to = new Date(); return { to: to.toISOString(), from: new Date(to.getTime() - days * 864e5).toISOString() }; });
  const { data, error, loading } = useTrackerInsights<any>(src, "overview", { ...range, tz: -new Date().getTimezoneOffset() });
  if (!data) return <State error={error} loading={loading} />;
  const t = data.totals ?? {};
  const kpis: [string, unknown][] = [["بازدیدکننده", t.visitors], ["کاربر", t.users], ["نشست", t.sessions], ["بازدید صفحه", t.pageViews], ["درخواست API", t.apiCalls]];
  const list = (title: string, rows: any[]) => (
    <div style={{ flex: "1 1 260px" }}>
      <div style={{ fontWeight: 600, margin: "12px 0 6px" }}>{title}</div>
      {rows.length ? rows.map((r, i) => (
        <div key={i} style={{ display: "flex", justifyContent: "space-between", gap: 12, padding: "3px 0" }}>
          <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{r.label}</span>
          <span style={muted}>{fa(r.count)}</span>
        </div>
      )) : <div style={muted}>داده‌ای نیست</div>}
    </div>
  );
  return (
    <div style={box}>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {kpis.map(([l, v]) => (
          <div key={l} style={{ flex: "1 1 120px", border: "1px solid rgba(127,127,127,.25)", borderRadius: 10, padding: "10px 12px" }}>
            <div style={{ fontSize: 20, fontWeight: 700 }}>{fa(v)}</div><div style={muted}>{l}</div>
          </div>
        ))}
      </div>
      <div style={{ display: "flex", gap: 24, flexWrap: "wrap" }}>
        {list("صفحات پربازدید", data.topPages ?? [])}
        {list("اکشن‌های پرکاربرد", data.topActions ?? [])}
        {list("رویدادها", data.topEvents ?? [])}
      </div>
    </div>
  );
}

/** Latest events across all users. */
export function TrackerRecentEvents({ take = 50, type, ...src }: InsightsSource & { take?: number; type?: string }) {
  const { data, error, loading } = useTrackerInsights<InsightsEvent[]>(src, "events", { take, type });
  if (!data) return <State error={error} loading={loading} />;
  return (
    <div style={box}>
      {data.map((e) => (
        <div key={e.id} style={{ padding: "6px 0", borderBottom: "1px dashed rgba(127,127,127,.25)" }}>
          <span style={badge}>{LABELS[e.type] ?? e.type}</span>{describeEvent(e)}
          <div style={muted}>{time(e.ts)}{e.userId ? ` · کاربر ${e.userId}` : " · ناشناس"}</div>
        </div>
      ))}
    </div>
  );
}
