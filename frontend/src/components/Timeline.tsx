"use client";

import Link from "next/link";
import { ArrowLeftRight, CircleDot, Edit3, LogIn, MousePointerClick, Send, Star, Undo2, type LucideIcon } from "lucide-react";
import { dayLabel, timeLabel, toDate } from "@/lib/format";
import { describe, statusTone, typeLabels, type EventRow } from "@/lib/events";
import { Badge, Empty } from "@/components/ui";
import { useSession, withProject } from "@/components/session";

const icons: Record<string, LucideIcon> = {
  pageview: CircleDot, pageleave: Undo2, click: MousePointerClick, submit: Send, change: Edit3, api: ArrowLeftRight, custom: Star, identify: LogIn,
};

/** Events newest first, grouped by day, with a divider where the session changes. */
export function Timeline({ events, showUser = false }: { events: EventRow[]; showUser?: boolean }) {
  const { project } = useSession();
  if (!events.length) return <Empty>رویدادی ثبت نشده است.</Empty>;
  let lastDay = "", lastSession: string | undefined = "";
  return (
    <div>
      {events.map((e) => {
        const day = toDate(e.ts).toDateString();
        const dayHeader = day !== lastDay ? <div className="sticky top-14 z-10 -mx-2 mb-2 mt-4 bg-surface-solid/80 px-2 py-1 text-xs font-bold text-ink-3 backdrop-blur lg:top-0">{dayLabel(e.ts)}</div> : null;
        const newSession = e.sessionId && e.sessionId !== lastSession && day === lastDay;
        lastDay = day;
        lastSession = e.sessionId ?? lastSession;
        return (
          <div key={e.id}>
            {dayHeader}
            {newSession && (
              <div className="my-2 flex items-center gap-2 text-xs text-ink-3">
                <span className="h-px flex-1 bg-line" /> نشست جدید ·
                <Link className="text-brand hover:underline" href={withProject(`/session?id=${encodeURIComponent(e.sessionId!)}`, project)}>مشاهده‌ی نشست</Link>
                <span className="h-px flex-1 bg-line" />
              </div>
            )}
            <EventItem e={e} showUser={showUser} />
          </div>
        );
      })}
    </div>
  );
}

export function EventItem({ e, showUser }: { e: EventRow; showUser?: boolean }) {
  const { project } = useSession();
  const Icon = icons[e.type] ?? Star;
  return (
    <div className="grid grid-cols-[4.5rem_2rem_1fr] items-start gap-2 border-b border-line/60 py-2.5 text-sm last:border-0">
      <div className="tabular pt-1 text-xs text-ink-3">{timeLabel(e.ts)}</div>
      <div className={`grid size-8 place-items-center rounded-full ${e.type === "api" ? "bg-info-soft text-info" : "bg-brand-soft text-brand-strong"}`} aria-hidden>
        <Icon className="size-4" />
      </div>
      <div className="min-w-0">
        <div className="leading-7">
          <Badge tone="gray" className="me-1.5">{typeLabels[e.type] ?? e.type}</Badge>
          {describe(e)}
          {e.type === "api" && e.statusCode ? <Badge tone={statusTone(e.statusCode)} className="ms-1.5">{e.statusCode}</Badge> : null}
          {e.type === "api" && e.name ? <span className="ltr ms-2 font-mono text-xs text-ink-3">{e.method} {e.route ?? e.path}</span> : null}
        </div>
        <div className="flex flex-wrap gap-x-3 gap-y-0.5 text-xs text-ink-3">
          {showUser && (e.userId
            ? <Link className="text-brand hover:underline" href={withProject(`/user?id=${encodeURIComponent(e.userId)}`, project)}>کاربر: <span className="ltr">{e.userId}</span></Link>
            : e.anonymousId ? <Link className="text-brand hover:underline" href={withProject(`/visitor?id=${encodeURIComponent(e.anonymousId)}`, project)}>بازدیدکننده‌ی ناشناس</Link> : null)}
          {e.device && <span>{e.device} · {e.os} · {e.browser}</span>}
          {e.ip && <span className="ltr">{e.ip}</span>}
          {e.source === "server" && <span>از سمت سرور</span>}
        </div>
        {e.properties && (
          <details className="mt-1">
            <summary className="cursor-pointer text-xs text-ink-3 hover:text-ink">جزئیات</summary>
            <pre className="ltr mt-1 max-h-72 overflow-auto rounded-xl bg-surface-2 p-3 text-xs">{pretty(e.properties)}</pre>
          </details>
        )}
      </div>
    </div>
  );
}

function pretty(s: string) {
  try {
    return JSON.stringify(JSON.parse(s), null, 2);
  } catch {
    return s;
  }
}
