import Link from "next/link";
import { dayLabel, timeLabel, toDate } from "@/lib/api";
import { describe, statusClass, typeIcons, typeLabels, type EventRow } from "@/lib/events";

/** Events newest first, grouped by day, with a divider when the session changes. */
export function Timeline({ events, showUser = false }: { events: EventRow[]; showUser?: boolean }) {
  if (!events.length) return <div className="empty">رویدادی ثبت نشده است.</div>;
  let lastDay = "", lastSession: string | undefined = "";
  return (
    <div className="timeline">
      {events.map((e) => {
        const day = toDate(e.ts).toDateString();
        const dayHeader = day !== lastDay ? <div className="day">{dayLabel(e.ts)}</div> : null;
        const newSession = e.sessionId && e.sessionId !== lastSession && day === lastDay;
        lastDay = day; lastSession = e.sessionId ?? lastSession;
        return (
          <div key={e.id}>
            {dayHeader}
            {newSession && <div className="session-sep">نشست جدید · <Link href={`/session/?id=${encodeURIComponent(e.sessionId!)}`}>مشاهده نشست</Link></div>}
            <EventItem e={e} showUser={showUser} />
          </div>
        );
      })}
    </div>
  );
}

export function EventItem({ e, showUser }: { e: EventRow; showUser?: boolean }) {
  return (
    <div className="item">
      <div className="time">{timeLabel(e.ts)}</div>
      <div className={`icon ${e.type === "api" ? "api" : ""}`} aria-hidden>{typeIcons[e.type] ?? "•"}</div>
      <div>
        <div>
          <span className="badge type">{typeLabels[e.type] ?? e.type}</span>{" "}
          {describe(e)}{" "}
          {e.type === "api" && e.statusCode ? <span className={`badge ${statusClass(e.statusCode)}`}>{e.statusCode}</span> : null}
          {e.type === "api" && e.name ? <span className="muted mono ltr" style={{ marginInlineStart: 8 }}>{e.method} {e.route ?? e.path}</span> : null}
        </div>
        <div className="meta">
          {showUser && (e.userId
            ? <Link href={`/user/?id=${encodeURIComponent(e.userId)}`}>کاربر: <span className="ltr">{e.userId}</span></Link>
            : e.anonymousId ? <Link href={`/visitor/?id=${encodeURIComponent(e.anonymousId)}`}>بازدیدکننده ناشناس</Link> : null)}
          {e.device && <span>{e.device} · {e.os} · {e.browser}</span>}
          {e.ip && <span className="ltr">{e.ip}</span>}
          {e.source === "server" && <span>از سمت سرور</span>}
        </div>
        {e.properties && (
          <details className="props"><summary>جزئیات</summary><pre>{pretty(e.properties)}</pre></details>
        )}
      </div>
    </div>
  );
}

function pretty(s: string) {
  try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; }
}
