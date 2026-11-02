import { num } from "@/lib/api";
import type { ReactNode } from "react";

export interface BarItem { label: ReactNode; count: number; extra?: ReactNode; key?: string }

export function BarList({ items, empty = "داده‌ای نیست" }: { items: BarItem[]; empty?: string }) {
  if (!items.length) return <div className="empty">{empty}</div>;
  const max = Math.max(...items.map((i) => i.count), 1);
  return (
    <div className="barlist">
      {items.map((i, idx) => (
        <div className="row" key={i.key ?? idx}>
          <div className="fill" style={{ width: `${(i.count / max) * 100}%` }} />
          <span className="label">{i.label}</span>
          <span className="n">{i.extra}{i.extra ? " · " : ""}{num(i.count)}</span>
        </div>
      ))}
    </div>
  );
}
