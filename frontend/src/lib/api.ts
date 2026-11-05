"use client";

export class ApiError extends Error {
  constructor(public status: number, message: string, public code?: string, public data?: unknown) {
    super(message);
  }
}

type Query = Record<string, string | number | boolean | undefined | null>;

/**
 * Calls the backend through the Next.js /api proxy. The session lives in an HttpOnly cookie, so there is no token here.
 * Every state-changing call carries X-Requested-With, which the API requires (CSRF guard).
 */
export async function api<T = unknown>(path: string, init: Omit<RequestInit, "body"> & { json?: unknown; query?: Query } = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("X-Requested-With", "fetch");
  let body: BodyInit | undefined;
  if (init.json !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(init.json);
  }
  const url = path + qs(init.query);
  const res = await fetch(url, { ...init, headers, body, credentials: "same-origin", cache: "no-store" });
  const text = await res.text();
  const data = text ? safeJson(text) : null;
  if (!res.ok) {
    const d = data as { message?: string; code?: string } | null;
    const err = new ApiError(res.status, d?.message ?? fallback(res.status), d?.code, data);
    if (typeof window !== "undefined") window.dispatchEvent(new CustomEvent("panel:api-error", { detail: err }));
    throw err;
  }
  return data as T;
}

export const post = <T = unknown>(path: string, json?: unknown, query?: Query) => api<T>(path, { method: "POST", json: json ?? {}, query });
export const put = <T = unknown>(path: string, json?: unknown) => api<T>(path, { method: "PUT", json: json ?? {} });
export const del = <T = unknown>(path: string) => api<T>(path, { method: "DELETE" });

export function qs(q?: Query) {
  if (!q) return "";
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(q)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  const s = p.toString();
  return s ? `?${s}` : "";
}

function fallback(status: number) {
  if (status === 401) return "نشست شما تمام شده است. دوباره وارد شوید.";
  if (status === 403) return "به این بخش دسترسی ندارید.";
  if (status === 404) return "پیدا نشد.";
  if (status === 429) return "تعداد درخواست‌ها زیاد است. کمی صبر کنید.";
  if (status >= 500) return "خطای سرور. کمی بعد دوباره تلاش کنید.";
  return "خطایی رخ داد. دوباره تلاش کنید.";
}

function safeJson(text: string) {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}
