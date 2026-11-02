import { num, parseJson } from "./api";

export interface EventRow {
  id: string; source: string; type: string; name?: string; ts: string;
  userId?: string; anonymousId?: string; sessionId?: string;
  url?: string; path?: string; referrer?: string; title?: string;
  route?: string; method?: string; statusCode?: number;
  ip?: string; browser?: string; os?: string; device?: string; screen?: string; language?: string;
  properties?: string;
}

export const typeLabels: Record<string, string> = {
  pageview: "بازدید صفحه",
  pageleave: "خروج از صفحه",
  click: "کلیک",
  submit: "ارسال فرم",
  change: "تغییر فیلد",
  api: "درخواست API",
  custom: "رویداد",
  identify: "ورود / شناسایی",
};

export const typeIcons: Record<string, string> = {
  pageview: "◉", pageleave: "↩", click: "☝", submit: "✉", change: "✎", api: "⇄", custom: "★", identify: "☺",
};

/** One readable Persian sentence per event. Values are rendered as text, never HTML. */
export function describe(e: EventRow): string {
  const p = parseJson(e.properties) ?? {};
  switch (e.type) {
    case "pageview": return `صفحه ${e.path ?? ""} را دید${e.title ? ` (${e.title})` : ""}`;
    case "pageleave": {
      const d = Number(p.duration ?? 0);
      return `از ${p.leftPath ?? e.path ?? ""} خارج شد بعد از ${d >= 60 ? `${num(Math.floor(d / 60))} دقیقه و ` : ""}${num(d % 60)} ثانیه، ${num(p.scroll ?? 0)}٪ اسکرول`;
    }
    case "click": return `روی «${e.name ?? p.text ?? p.tag ?? "?"}» کلیک کرد${p.href ? ` ← ${p.href}` : ""}`;
    case "submit": return `فرم ${e.name ? `«${e.name}» ` : ""}را ارسال کرد`;
    case "change": return `فیلد «${e.name ?? "?"}» را تغییر داد`;
    case "api": return e.name ? `${e.name}` : `${e.method ?? ""} ${e.route ?? e.path ?? ""}`;
    case "identify": return "وارد حساب شد / مشخصات کاربر ثبت شد";
    default: return `رویداد «${e.name ?? e.type}»`;
  }
}

export function statusClass(code?: number) {
  if (!code) return "";
  if (code >= 500) return "bad";
  if (code >= 400) return "warn";
  return "ok";
}
