"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api, qs } from "@/lib/api";
import { typeLabels, type EventRow } from "@/lib/events";
import { NeedsProject } from "@/components/PanelShell";
import { Timeline } from "@/components/Timeline";
import { Button, Card, ErrorBox, inputBase, PageTitle, Select, Toggle } from "@/components/ui";

export default function EventsPage() {
  return (
    <>
      <PageTitle title="رویدادهای زنده" sub="هر بازدید، کلیک و درخواست API به محض رسیدن." />
      <NeedsProject>{(project) => <Live key={project} project={project} />}</NeedsProject>
    </>
  );
}

function Live({ project }: { project: string }) {
  const [events, setEvents] = useState<EventRow[]>([]);
  const [type, setType] = useState("");
  const [q, setQ] = useState("");
  const [auto, setAuto] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const qRef = useRef(q);
  qRef.current = q;

  const load = useCallback(async (before?: string) => {
    setLoading(true);
    try {
      const rows = await api<EventRow[]>(`/api/data/events${qs({ project, type, q: qRef.current, before, take: 100 })}`);
      setEvents((prev) => (before ? [...prev, ...rows] : rows));
      setError(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [project, type]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    if (!auto) return;
    const t = setInterval(() => { if (!document.hidden) load(); }, 5000);
    return () => clearInterval(t);
  }, [auto, load]);

  return (
    <>
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Select value={type} onChange={setType} label="نوع رویداد">
          <option value="">همه‌ی رویدادها</option>
          {Object.entries(typeLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </Select>
        <form onSubmit={(e) => { e.preventDefault(); load(); }} className="flex gap-2">
          <input className={`${inputBase} w-64`} placeholder="جستجو در مسیر، نام رویداد، کاربر…" value={q} onChange={(e) => setQ(e.target.value)} />
          <Button type="submit">جستجو</Button>
        </form>
        <Toggle checked={auto} onChange={setAuto} label="به‌روزرسانی خودکار" />
      </div>
      <ErrorBox error={error} />
      <Card>
        <Timeline events={events} showUser />
        {events.length >= 100 && (
          <div className="mt-4 text-center">
            <Button busy={loading} onClick={() => { setAuto(false); load(events[events.length - 1].receivedAt); }}>قدیمی‌ترها</Button>
          </div>
        )}
      </Card>
    </>
  );
}
