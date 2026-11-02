"use client";

export class ApiError extends Error {
  constructor(public status: number) { super(`HTTP ${status}`); }
}

export async function api<T = any>(path: string, params?: Record<string, string | number | undefined | null>, method: "GET" | "POST" = "GET", body?: unknown): Promise<T> {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
  const url = `/api/dash/${path}${qs.size ? `?${qs}` : ""}`;
  const res = await fetch(url, {
    method,
    credentials: "same-origin",
    headers: method === "POST" ? { "Content-Type": "application/json", "X-Rahgir-Csrf": "1" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    cache: "no-store",
  });
  if (res.status === 401 && !location.pathname.startsWith("/login")) {
    location.href = "/login/";
    throw new ApiError(401);
  }
  if (!res.ok) throw new ApiError(res.status);
  const text = await res.text();
  return (text ? JSON.parse(text) : null) as T;
}

const fa = new Intl.NumberFormat("fa-IR");
export const num = (n: number | null | undefined) => fa.format(Number(n ?? 0));

const dtFull = new Intl.DateTimeFormat("fa-IR", { dateStyle: "medium", timeStyle: "short" });
const dtDay = new Intl.DateTimeFormat("fa-IR", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
const dtTime = new Intl.DateTimeFormat("fa-IR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
const dtShort = new Intl.DateTimeFormat("fa-IR", { month: "short", day: "numeric" });

/** The API returns UTC without a zone suffix. */
export const toDate = (s: string) => new Date(/Z|[+-]\d\d:?\d\d$/.test(s) ? s : s + "Z");
export const fullDate = (s: string) => dtFull.format(toDate(s));
export const dayLabel = (s: string) => dtDay.format(toDate(s));
export const timeLabel = (s: string) => dtTime.format(toDate(s));
export const shortDay = (d: string) => dtShort.format(new Date(d + (d.length === 10 ? "T00:00:00" : "")));

export function ago(s: string) {
  const sec = Math.round((Date.now() - toDate(s).getTime()) / 1000);
  if (sec < 60) return "همین الان";
  if (sec < 3600) return `${num(Math.floor(sec / 60))} دقیقه پیش`;
  if (sec < 86400) return `${num(Math.floor(sec / 3600))} ساعت پیش`;
  return `${num(Math.floor(sec / 86400))} روز پیش`;
}

export function parseJson(s: string | null | undefined): Record<string, any> | null {
  if (!s) return null;
  try { return JSON.parse(s); } catch { return null; }
}
