"use client";

import { useMemo, useRef, useState } from "react";
import { num, shortDateTime, shortDay, toDate } from "@/lib/format";

export interface Series { name: string; color?: string; points: { t: string | Date; v: number | null }[] }

const COLORS = ["var(--c1)", "var(--c2)", "var(--c3)", "var(--c4)", "var(--c5)"];

/**
 * Small dependency-free SVG time chart: lines or bars, a hover cross-hair with values, RTL-friendly labels.
 * Time runs left to right (as on every clock and chart), independent of the page direction.
 */
export function TimeChart({ series, height = 200, fmt = num, kind = "line", zero = true, empty = "در این بازه داده‌ای نیست.", daily = false }:
  { series: Series[]; height?: number; fmt?: (v: number) => string; kind?: "line" | "bar"; zero?: boolean; empty?: string; daily?: boolean }) {
  const label = (d: Date) => (daily ? shortDay(d.toISOString().slice(0, 10)) : shortDateTime(d));
  const ref = useRef<SVGSVGElement>(null);
  const [hover, setHover] = useState<number | null>(null);
  const W = 800, H = height, padL = 8, padR = 52, padT = 10, padB = 22;

  const model = useMemo(() => {
    const times = Array.from(new Set(series.flatMap((s) => s.points.map((p) => toDate(p.t).getTime())))).sort((a, b) => a - b);
    const vals = series.flatMap((s) => s.points.map((p) => p.v)).filter((v): v is number => v != null && isFinite(v));
    if (!times.length || !vals.length) return null;
    let min = zero ? Math.min(0, ...vals) : Math.min(...vals);
    let max = Math.max(...vals);
    if (max === min) { max = min + 1; if (!zero) min -= 1; }
    const t0 = times[0], t1 = times[times.length - 1];
    const plot = W - padL - padR;
    // Bars sit in equal slots (one per time); lines use real time spacing.
    const index = new Map(times.map((t, i) => [t, i]));
    const x = kind === "bar"
      ? (t: number) => padL + ((index.get(t) ?? 0) + 0.5) * (plot / times.length)
      : (t: number) => padL + (t1 === t0 ? plot / 2 : ((t - t0) / (t1 - t0)) * plot);
    const y = (v: number) => padT + (1 - (v - min) / (max - min)) * (H - padT - padB);
    const byTime = series.map((s) => new Map(s.points.map((p) => [toDate(p.t).getTime(), p.v])));
    return { times, min, max, x, y, byTime };
  }, [series, zero, H, kind]);

  if (!model) return <div className="grid place-items-center text-sm text-ink-3" style={{ height }}>{empty}</div>;
  const { times, min, max, x, y, byTime } = model;
  const ticks = [min, (min + max) / 2, max];
  const barW = Math.max(1.5, Math.min(36, ((W - padL - padR) / Math.max(times.length, 1)) * 0.72 / Math.max(series.length, 1)));

  const onMove = (e: React.MouseEvent) => {
    const r = ref.current!.getBoundingClientRect();
    const px = ((e.clientX - r.left) / r.width) * W;
    let best = 0, bd = Infinity;
    times.forEach((t, i) => { const d = Math.abs(x(t) - px); if (d < bd) { bd = d; best = i; } });
    setHover(best);
  };

  return (
    <div className="relative" dir="ltr">
      <svg ref={ref} viewBox={`0 0 ${W} ${H}`} className="block w-full" style={{ height }} preserveAspectRatio="none"
        onMouseMove={onMove} onMouseLeave={() => setHover(null)} role="img" aria-label={series.map((s) => s.name).join("، ")}>
        {ticks.map((t, i) => (
          <g key={i}>
            <line x1={padL} x2={W - padR} y1={y(t)} y2={y(t)} stroke="var(--line)" strokeDasharray={i === 0 ? undefined : "3 4"} vectorEffect="non-scaling-stroke" />
            <text x={W - padR + 6} y={y(t) + 4} fontSize="11" fill="var(--ink-3)">{fmt(t)}</text>
          </g>
        ))}
        {series.map((s, si) => {
          const color = s.color ?? COLORS[si % COLORS.length];
          if (kind === "bar") {
            return (
              <g key={s.name} fill={color}>
                {times.map((t) => {
                  const v = byTime[si].get(t);
                  if (v == null) return null;
                  const bx = x(t) - (barW * series.length) / 2 + si * barW;
                  return <rect key={t} x={bx} width={barW} y={y(Math.max(v, 0))} height={Math.max(0, y(min) - y(Math.max(v, 0)))} rx="1.5" opacity={0.85} />;
                })}
              </g>
            );
          }
          const pts = times.map((t) => [t, byTime[si].get(t)] as const).filter(([, v]) => v != null);
          const d = pts.map(([t, v], i) => `${i ? "L" : "M"}${x(t).toFixed(1)},${y(v!).toFixed(1)}`).join("");
          return (
            <g key={s.name}>
              {si === 0 && pts.length > 1 && (
                <path d={`${d}L${x(pts[pts.length - 1][0])},${y(min)}L${x(pts[0][0])},${y(min)}Z`} fill={color} opacity={0.08} />
              )}
              <path d={d} fill="none" stroke={color} strokeWidth="2" vectorEffect="non-scaling-stroke" strokeLinejoin="round" />
            </g>
          );
        })}
        {hover != null && <line x1={x(times[hover])} x2={x(times[hover])} y1={padT} y2={H - padB} stroke="var(--line-strong)" vectorEffect="non-scaling-stroke" />}
        <text x={padL} y={H - 6} fontSize="11" fill="var(--ink-3)">{label(new Date(times[0]))}</text>
        {times.length > 1 && <text x={W - padR} y={H - 6} fontSize="11" fill="var(--ink-3)" textAnchor="end">{label(new Date(times[times.length - 1]))}</text>}
      </svg>
      {series.length > 1 && (
        <div className="mt-1 flex flex-wrap justify-end gap-3 text-xs text-ink-2" dir="rtl">
          {series.map((s, si) => (
            <span key={s.name} className="inline-flex items-center gap-1.5">
              <span className="inline-block size-2.5 rounded-full" style={{ background: s.color ?? COLORS[si % COLORS.length] }} />{s.name}
            </span>
          ))}
        </div>
      )}
      {hover != null && (
        <div className="pointer-events-none absolute top-1 rounded-xl border border-line bg-surface-solid px-3 py-2 text-xs shadow-card" dir="rtl"
          style={{ left: `${Math.min(70, Math.max(0, (x(times[hover]) / W) * 100 - 10))}%` }}>
          <div className="mb-1 font-bold">{label(new Date(times[hover]))}</div>
          {series.map((s, si) => (
            <div key={s.name} className="flex justify-between gap-4">
              <span className="text-ink-3">{s.name}</span>
              <span className="tabular font-medium">{byTime[si].get(times[hover]) == null ? "—" : fmt(byTime[si].get(times[hover])!)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** Rows (with a time column) to a chart series. */
export const ser = <R extends Record<string, unknown>>(rows: R[] | undefined | null, key: keyof R | ((r: R) => number | null), name: string, color?: string): Series => ({
  name, color,
  points: (rows ?? []).map((r) => ({ t: String(r.t ?? r.day), v: typeof key === "function" ? key(r) : (r[key] as number | null) })),
});
