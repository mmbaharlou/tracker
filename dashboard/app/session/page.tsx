"use client";
import { useEffect, useState } from "react";
import { Shell, useShell } from "@/components/Shell";
import { Timeline } from "@/components/Timeline";
import { api } from "@/lib/api";

export default function Page() {
  return <Shell title="جزئیات نشست"><View /></Shell>;
}

function View() {
  const { project } = useShell();
  const [events, setEvents] = useState<any[] | null>(null);
  useEffect(() => {
    const id = new URLSearchParams(location.search).get("id");
    if (id) api<any[]>("session", { project, id }).then((r) => setEvents([...r].reverse())).catch(() => setEvents([]));
  }, [project]);
  return <div className="panel">{events ? <Timeline events={events} showUser /> : <div className="empty">در حال بارگذاری…</div>}</div>;
}
