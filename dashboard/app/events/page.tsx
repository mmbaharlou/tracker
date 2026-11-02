"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { Shell, useShell } from "@/components/Shell";
import { Timeline } from "@/components/Timeline";
import { api } from "@/lib/api";
import { typeLabels, type EventRow } from "@/lib/events";

export default function Page() {
  return <Shell title="رویدادهای زنده"><Live /></Shell>;
}

function Live() {
  const { project } = useShell();
  const [events, setEvents] = useState<EventRow[]>([]);
  const [type, setType] = useState("");
  const [q, setQ] = useState("");
  const [auto, setAuto] = useState(true);
  const [loading, setLoading] = useState(false);
  const qRef = useRef(q); qRef.current = q;

  const load = useCallback(async (more = false) => {
    setLoading(true);
    try {
      const before = more && events.length ? (events[events.length - 1] as any).receivedAt : undefined;
      const rows = await api<EventRow[]>("events", { project, type, q: qRef.current, before, take: 100 });
      setEvents((prev) => (more ? [...prev, ...rows] : rows));
    } catch { /* keep old */ } finally { setLoading(false); }
  }, [project, type, events]);

  useEffect(() => { load(); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [project, type]);
  useEffect(() => {
    if (!auto) return;
    const t = setInterval(() => { if (document.visibilityState === "visible") load(); }, 5000);
    return () => clearInterval(t);
  }, [auto, load]);

  return (
    <>
      <div className="topbar">
        <select className="select" value={type} onChange={(e) => setType(e.target.value)} aria-label="نوع رویداد">
          <option value="">همه رویدادها</option>
          {Object.entries(typeLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <form onSubmit={(e) => { e.preventDefault(); load(); }} style={{ display: "flex", gap: 8 }}>
          <input className="input" placeholder="جستجو در مسیر، نام رویداد، کاربر…" value={q} onChange={(e) => setQ(e.target.value)} />
          <button className="btn">جستجو</button>
        </form>
        <label className="muted"><input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} /> به‌روزرسانی خودکار</label>
      </div>
      <div className="panel">
        <Timeline events={events} showUser />
        {events.length >= 100 && <div style={{ textAlign: "center", marginTop: 12 }}>
          <button className="btn" disabled={loading} onClick={() => { setAuto(false); load(true); }}>قدیمی‌ترها</button>
        </div>}
      </div>
    </>
  );
}
