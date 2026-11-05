const nf = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 1 });
const dtFull = new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short" });
const dtDay = new Intl.DateTimeFormat("fa-IR", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
const dtTime = new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
const dtHm = new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit" });
const dtShort = new Intl.DateTimeFormat("fa-IR", { month: "short", day: "numeric" });
const dtShortTime = new Intl.DateTimeFormat("fa-IR", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });

export const num = (v: number | string | null | undefined) => (v == null || v === "" || isNaN(Number(v)) ? "—" : nf.format(Number(v)));
export const pct = (v: number | null | undefined) => (v == null || isNaN(v) ? "—" : nf.format(Math.round(v * 10) / 10) + "٪");
export const ms = (v: number | null | undefined) => {
  if (v == null || isNaN(v)) return "—";
  return v >= 1000 ? nf.format(Math.round(v / 100) / 10) + " ثانیه" : nf.format(Math.round(v)) + " ms";
};
export const kbs = (v: number) => (v >= 1024 ? nf.format(Math.round(v / 102.4) / 10) + " MB/s" : nf.format(Math.round(v)) + " KB/s");

/** API times are UTC; some arrive without a zone suffix (SQL Server). */
export const toDate = (s: string | Date) => (s instanceof Date ? s : new Date(/Z$|[+-]\d\d:?\d\d$/.test(s) ? s : s + "Z"));
export const fullDate = (s?: string | null) => (s ? dtFull.format(toDate(s)) : "—");
export const dayLabel = (s: string) => dtDay.format(toDate(s));
export const timeLabel = (s: string) => dtTime.format(toDate(s));
export const hm = (s: string | Date) => dtHm.format(toDate(s));
export const shortDay = (s: string) => dtShort.format(new Date(s.length === 10 ? s + "T00:00:00" : s));
export const shortDateTime = (s: string | Date) => dtShortTime.format(toDate(s));

export function ago(s?: string | null) {
  if (!s) return "—";
  const sec = Math.round((Date.now() - toDate(s).getTime()) / 1000);
  if (sec < 0) return "به‌زودی";
  if (sec < 60) return "همین الان";
  if (sec < 3600) return `${num(Math.floor(sec / 60))} دقیقه پیش`;
  if (sec < 86400) return `${num(Math.floor(sec / 3600))} ساعت پیش`;
  return `${num(Math.floor(sec / 86400))} روز پیش`;
}

export function parseJson<T = Record<string, unknown>>(s: string | null | undefined): T | null {
  if (!s) return null;
  try {
    return JSON.parse(s) as T;
  } catch {
    return null;
  }
}

/** Short, readable device name from a user-agent string. */
export function deviceName(ua?: string | null) {
  if (!ua) return "نامشخص";
  const os = /Windows/i.test(ua) ? "Windows" : /Android/i.test(ua) ? "Android" : /iPhone|iPad/i.test(ua) ? "iOS" : /Mac OS/i.test(ua) ? "macOS" : /Linux/i.test(ua) ? "Linux" : "";
  const br = /Edg\//.test(ua) ? "Edge" : /OPR\//.test(ua) ? "Opera" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : "مرورگر";
  return os ? `${br} روی ${os}` : br;
}
