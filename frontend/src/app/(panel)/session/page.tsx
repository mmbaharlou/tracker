"use client";

import { useSearchParams } from "next/navigation";
import { qs } from "@/lib/api";
import type { EventRow } from "@/lib/events";
import { NeedsProject } from "@/components/PanelShell";
import { Timeline } from "@/components/Timeline";
import { Async, Card, Empty, PageTitle, useData } from "@/components/ui";

export default function SessionPage() {
  const id = useSearchParams().get("id") ?? "";
  return (
    <>
      <PageTitle title="جزئیات نشست" sub="یک بار حضور در سایت، از ورود تا خروج." />
      {!id ? <Empty>نشستی انتخاب نشده.</Empty> : <NeedsProject>{(project) => <View project={project} id={id} />}</NeedsProject>}
    </>
  );
}

function View({ project, id }: { project: string; id: string }) {
  const state = useData<EventRow[]>(`/api/data/session${qs({ project, id })}`);
  return <Async state={state}>{(events) => <Card><Timeline events={[...events].reverse()} showUser /></Card>}</Async>;
}
