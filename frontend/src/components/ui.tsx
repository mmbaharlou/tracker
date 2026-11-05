"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { api } from "@/lib/api";

/** Loads JSON from an endpoint, re-loads when path changes, exposes reload(). Pass null to wait. */
export function useData<T>(path: string | null, opts: { refreshMs?: number } = {}) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const seq = useRef(0);
  const reload = useCallback(async (quiet = false) => {
    if (!path) return;
    const n = ++seq.current;
    if (!quiet) setLoading(true);
    try {
      const d = await api<T>(path);
      if (n === seq.current) { setData(d); setError(null); }
    } catch (e) {
      if (n === seq.current) setError((e as Error).message);
    } finally {
      if (n === seq.current) setLoading(false);
    }
  }, [path]);
  useEffect(() => {
    setData(null);
    reload();
  }, [reload]);
  useEffect(() => {
    if (!opts.refreshMs || !path) return;
    const t = setInterval(() => { if (!document.hidden) reload(true); }, opts.refreshMs);
    return () => clearInterval(t);
  }, [opts.refreshMs, path, reload]);
  return { data, error, loading, reload, setData };
}

/** Runs an async action with a busy flag and an error / success message. */
export function useAction() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const run = useCallback(async (fn: () => Promise<unknown>, success?: string) => {
    setBusy(true);
    setError(null);
    setMessage(null);
    try {
      await fn();
      if (success) setMessage(success);
      return true;
    } catch (e) {
      setError((e as Error).message);
      return false;
    } finally {
      setBusy(false);
    }
  }, []);
  return { busy, error, message, run, setError, setMessage };
}

export function PageTitle({ title, sub, children }: { title: string; sub?: React.ReactNode; children?: React.ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-2xl font-extrabold tracking-tight lg:text-[1.65rem]">{title}</h1>
        {sub && <p className="mt-1 text-sm text-ink-3">{sub}</p>}
      </div>
      <div className="flex flex-wrap items-center gap-2">{children}</div>
    </div>
  );
}

export function Card({ children, className = "", title, actions }: { children: React.ReactNode; className?: string; title?: React.ReactNode; actions?: React.ReactNode }) {
  return (
    <section className={`rounded-3xl border border-white/60 bg-surface p-5 shadow-soft sm:p-6 ${className}`}>
      {(title || actions) && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
          {title && <h2 className="font-bold">{title}</h2>}
          {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

export function Stat({ label, value, sub, tone }: { label: string; value: React.ReactNode; sub?: React.ReactNode; tone?: "ok" | "warn" | "bad" }) {
  const ring = tone === "bad" ? "ring-2 ring-danger/30" : tone === "warn" ? "ring-2 ring-accent/30" : "";
  return (
    <div className={`rounded-3xl border border-white/60 bg-surface p-4 shadow-soft sm:p-5 ${ring}`}>
      <div className="text-xs font-medium text-ink-3">{label}</div>
      <div className="tabular mt-1 text-2xl font-extrabold tracking-tight">{value}</div>
      {sub && <div className="mt-1 text-xs text-ink-3">{sub}</div>}
    </div>
  );
}

type Variant = "primary" | "secondary" | "danger" | "ghost";
const variants: Record<Variant, string> = {
  primary: "bg-brand text-brand-ink shadow-soft hover:bg-brand-strong",
  secondary: "border border-line bg-surface hover:border-line-strong hover:bg-surface-2",
  danger: "border border-danger/30 bg-danger-soft text-danger hover:bg-danger/10",
  ghost: "text-ink-2 hover:bg-surface-2",
};

export function Button({ variant = "secondary", size = "md", className = "", busy, children, ...rest }:
  React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean; size?: "sm" | "md" }) {
  const sz = size === "sm" ? "h-8 px-3 text-xs rounded-lg" : "h-10 px-4 text-sm rounded-xl";
  return (
    <button {...rest} disabled={rest.disabled || busy}
      className={`inline-flex items-center justify-center gap-1.5 font-medium transition disabled:opacity-50 ${sz} ${variants[variant]} ${className}`}>
      {busy ? <Spinner /> : null}
      {children}
    </button>
  );
}

export function Spinner({ className = "size-4" }: { className?: string }) {
  return <span className={`inline-block animate-spin rounded-full border-2 border-current border-t-transparent ${className}`} aria-hidden />;
}

export function Field({ label, hint, children }: { label: string; hint?: React.ReactNode; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="text-sm font-medium text-ink-2">{label}</span>
      <div className="mt-1">{children}</div>
      {hint && <span className="mt-1 block text-xs leading-6 text-ink-3">{hint}</span>}
    </label>
  );
}

export const inputBase = "min-h-10 rounded-xl border border-line-strong bg-surface-solid/90 px-3 py-2 text-sm outline-none transition focus:border-brand";
export const inputCls = `w-full ${inputBase}`;

export function Select({ value, onChange, children, className = "", label }: { value: string | number; onChange: (v: string) => void; children: React.ReactNode; className?: string; label?: string }) {
  return (
    <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} className={`${inputBase} ${className}`}>
      {children}
    </select>
  );
}

export function Toggle({ checked, onChange, label, hint }: { checked: boolean; onChange: (v: boolean) => void; label: string; hint?: string }) {
  return (
    <label className="flex cursor-pointer items-start gap-3 py-1">
      <input type="checkbox" className="mt-1.5 size-4 accent-[var(--brand)]" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>
        <span className="text-sm font-medium">{label}</span>
        {hint && <span className="block text-xs leading-6 text-ink-3">{hint}</span>}
      </span>
    </label>
  );
}

const tones = {
  gray: "bg-surface-2 text-ink-2",
  green: "bg-ok-soft text-ok",
  amber: "bg-accent-soft text-accent",
  red: "bg-danger-soft text-danger",
  brand: "bg-brand-soft text-brand-strong",
  blue: "bg-info-soft text-info",
};
export type Tone = keyof typeof tones;
export function Badge({ tone = "gray", children, className = "" }: { tone?: Tone; children: React.ReactNode; className?: string }) {
  return <span className={`inline-block whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium ${tones[tone]} ${className}`}>{children}</span>;
}

export function ErrorBox({ error }: { error: string | null | undefined }) {
  return error ? <p className="mb-4 rounded-2xl bg-danger-soft px-4 py-3 text-sm text-danger" role="alert">{error}</p> : null;
}

export function SuccessBox({ message }: { message: string | null | undefined }) {
  return message ? <p className="mb-4 rounded-2xl bg-ok-soft px-4 py-3 text-sm text-ok" role="status">{message}</p> : null;
}

export function Notice({ children, tone = "brand" }: { children: React.ReactNode; tone?: "brand" | "amber" | "red" }) {
  const cls = tone === "brand" ? "border-brand/15 bg-brand-soft/70" : tone === "amber" ? "border-accent/20 bg-accent-soft" : "border-danger/20 bg-danger-soft";
  return <div className={`mb-5 rounded-2xl border px-4 py-3 text-sm leading-7 text-ink ${cls}`}>{children}</div>;
}

export function Empty({ children = "داده‌ای نیست." }: { children?: React.ReactNode }) {
  return <div className="py-10 text-center text-sm text-ink-3">{children}</div>;
}

export function Loading() {
  return <div className="flex items-center justify-center gap-2 py-12 text-sm text-ink-3"><Spinner /> در حال بارگذاری…</div>;
}

/** Shows loading / error / empty states, then children(data). */
export function Async<T>({ state, children, empty }: { state: { data: T | null; error: string | null; loading: boolean }; children: (d: T) => React.ReactNode; empty?: (d: T) => boolean }) {
  if (state.error && !state.data) return <ErrorBox error={state.error} />;
  if (!state.data) return <Loading />;
  if (empty?.(state.data)) return <Empty />;
  return <>{children(state.data)}</>;
}

export function Table({ head, children, className = "" }: { head: React.ReactNode[]; children: React.ReactNode; className?: string }) {
  return (
    <div className={`overflow-x-auto rounded-3xl border border-white/60 bg-surface shadow-soft ${className}`}>
      <table className="w-full text-sm">
        <thead className="border-b border-line bg-surface-2/70 text-xs text-ink-3">
          <tr>{head.map((h, i) => <th key={i} className="whitespace-nowrap px-4 py-3 text-start font-medium">{h}</th>)}</tr>
        </thead>
        <tbody className="divide-y divide-line [&_td]:px-4 [&_td]:py-2.5 [&_tr]:transition-colors [&_tr:hover]:bg-surface-2/50">{children}</tbody>
      </table>
    </div>
  );
}

export function Pager({ page, total, pageSize, onPage }: { page: number; total: number; pageSize: number; onPage: (p: number) => void }) {
  const pages = Math.ceil(total / pageSize);
  if (pages <= 1) return null;
  return (
    <div className="mt-4 flex items-center justify-center gap-2 text-sm">
      <Button disabled={page <= 1} onClick={() => onPage(page - 1)}>قبلی</Button>
      <span>صفحه {page.toLocaleString("fa-IR")} از {pages.toLocaleString("fa-IR")}</span>
      <Button disabled={page >= pages} onClick={() => onPage(page + 1)}>بعدی</Button>
    </div>
  );
}

export function Modal({ open, onClose, title, children, wide }: { open: boolean; onClose: () => void; title: string; children: React.ReactNode; wide?: boolean }) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);
  if (!open || typeof document === "undefined") return null;
  // Portal: a parent with backdrop-filter (the glass cards) would otherwise trap the fixed overlay inside it.
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto p-4 sm:items-center" role="dialog" aria-modal="true" aria-label={title}>
      <button className="fixed inset-0 bg-ink/40" aria-label="بستن" onClick={onClose} />
      <div className={`animate-rise relative my-8 w-full ${wide ? "max-w-4xl" : "max-w-lg"} rounded-3xl border border-white/60 bg-surface-solid p-5 shadow-lift sm:p-6`}>
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2 className="text-lg font-extrabold">{title}</h2>
          <button className="grid size-9 place-items-center rounded-full hover:bg-surface-2" onClick={onClose} aria-label="بستن"><X className="size-4" /></button>
        </div>
        {children}
      </div>
    </div>,
    document.body,
  );
}

/** Shows a secret once, with copy. */
export function SecretBox({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="mb-3">
      <div className="mb-1 text-xs font-medium text-ink-3">{label}</div>
      <div className="flex items-center gap-2">
        <code className="ltr block flex-1 overflow-x-auto whitespace-nowrap rounded-xl border border-line bg-surface-2 px-3 py-2 text-xs">{value}</code>
        <Button size="sm" onClick={() => { navigator.clipboard?.writeText(value); setCopied(true); setTimeout(() => setCopied(false), 1500); }}>
          {copied ? "کپی شد" : "کپی"}
        </Button>
      </div>
    </div>
  );
}

/** Horizontal bars for "top N" lists. */
export function BarList({ items, empty = "داده‌ای نیست" }: { items: { label: React.ReactNode; count: number; extra?: React.ReactNode; key?: string }[]; empty?: string }) {
  if (!items.length) return <Empty>{empty}</Empty>;
  const max = Math.max(...items.map((i) => i.count), 1);
  return (
    <div className="space-y-1.5">
      {items.map((i, idx) => (
        <div key={i.key ?? idx} className="relative flex items-center justify-between gap-3 overflow-hidden rounded-xl px-3 py-1.5 text-sm">
          <div className="absolute inset-y-0 start-0 rounded-xl bg-brand-soft" style={{ width: `${(i.count / max) * 100}%` }} aria-hidden />
          <span className="relative min-w-0 truncate">{i.label}</span>
          <span className="tabular relative shrink-0 text-xs text-ink-2">{i.extra}{i.extra ? " · " : ""}{i.count.toLocaleString("fa-IR")}</span>
        </div>
      ))}
    </div>
  );
}
