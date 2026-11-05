"use client";

import { useSearchParams } from "next/navigation";
import { qs } from "@/lib/api";
import type { EventRow } from "@/lib/events";
import { NeedsProject } from "@/components/PanelShell";
import { Timeline } from "@/components/Timeline";
import { Async, Card, Empty, PageTitle, useData } from "@/components/ui";

export default function VisitorPage() {
  const id = useSearchParams().get("id") ?? "";
  return (
    <>
      <PageTitle title="بازدیدکننده‌ی ناشناس" sub="کسی که هنوز وارد حساب نشده؛ بعد از ورود، تاریخچه‌اش به پرونده‌ی کاربر وصل می‌شود." />
      {!id ? <Empty>بازدیدکننده‌ای انتخاب نشده.</Empty> : <NeedsProject>{(project) => <View project={project} id={id} />}</NeedsProject>}
    </>
  );
}

function View({ project, id }: { project: string; id: string }) {
  const state = useData<EventRow[]>(`/api/data/visitor${qs({ project, id })}`);
  return <Async state={state}>{(events) => <Card><Timeline events={events} /></Card>}</Async>;
}
