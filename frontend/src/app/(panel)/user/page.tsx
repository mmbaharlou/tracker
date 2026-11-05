"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { post, qs } from "@/lib/api";
import { fullDate, num, parseJson } from "@/lib/format";
import type { EventRow } from "@/lib/events";
import { NeedsProject } from "@/components/PanelShell";
import { useSession, withProject } from "@/components/session";
import { Timeline } from "@/components/Timeline";
import { Async, Button, Card, Empty, ErrorBox, PageTitle, useAction, useData } from "@/components/ui";

interface Profile { userId: string; traits?: string; firstSeen: string; lastSeen: string; eventCount: number; lastBrowser?: string; lastOs?: string; lastDevice?: string; lastIp?: string }
interface UserData { profile?: Profile; aliases: string[]; events: EventRow[]; stats?: { sessions: number; pageViews: number; apiCalls: number } }

export default function UserPage() {
  const id = useSearchParams().get("id") ?? "";
  return (
    <>
      <PageTitle title="پرونده‌ی کاربر" sub={<span className="ltr font-mono">{id}</span>} />
      {!id ? <Empty>کاربری انتخاب نشده.</Empty> : <NeedsProject>{(project) => <View project={project} id={id} />}</NeedsProject>}
    </>
  );
}

function View({ project, id }: { project: string; id: string }) {
  const { isAdmin } = useSession();
  const router = useRouter();
  const state = useData<UserData>(`/api/data/user${qs({ project, id })}`);
  const act = useAction();

  const forget = () => {
    if (!confirm(`همه‌ی داده‌های کاربر ${id} در این پروژه برای همیشه حذف شود؟`)) return;
    act.run(async () => { await post(`/api/data/user/forget${qs({ project, id })}`); router.replace(withProject("/users", project)); });
  };

  return (
    <Async state={state}>
      {(d) => {
        const p = d.profile;
        const traits = parseJson<Record<string, unknown>>(p?.traits) ?? {};
        const s = d.stats;
        const rows: [string, React.ReactNode][] = [
          ...Object.entries(traits).map(([k, v]) => [k, typeof v === "object" ? JSON.stringify(v) : String(v)] as [string, React.ReactNode]),
          ...(p ? [["اولین بازدید", fullDate(p.firstSeen)], ["آخرین فعالیت", fullDate(p.lastSeen)]] as [string, React.ReactNode][] : []),
          ["نشست‌ها", num(s?.sessions)], ["بازدید صفحه", num(s?.pageViews ?? 0)], ["درخواست API", num(s?.apiCalls ?? 0)],
          ...(p ? [["دستگاه", [p.lastDevice, p.lastOs, p.lastBrowser].filter(Boolean).join(" · ")]] as [string, React.ReactNode][] : []),
          ...(p?.lastIp ? [["آخرین IP", <span key="ip" className="ltr">{p.lastIp}</span>]] as [string, React.ReactNode][] : []),
          ["شناسه‌های ناشناس", num(d.aliases.length)],
        ];
        return (
          <div className="grid items-start gap-6 lg:grid-cols-[320px_1fr]">
            <Card title="مشخصات">
              {!p && <p className="mb-3 text-sm text-ink-3">پروفایلی ثبت نشده؛ فقط رویدادها نمایش داده می‌شوند.</p>}
              <dl className="divide-y divide-line text-sm">
                {rows.map(([k, v]) => (
                  <div key={k} className="flex justify-between gap-3 py-2"><dt className="text-ink-3">{k}</dt><dd className="min-w-0 break-words text-end">{v}</dd></div>
                ))}
              </dl>
              <ErrorBox error={act.error} />
              {isAdmin && <Button variant="danger" className="mt-4 w-full" busy={act.busy} onClick={forget}>حذف کامل داده‌های این کاربر</Button>}
            </Card>
            <Card title="همه‌ی کارهایی که انجام داده (جدیدترین بالا)">
              <Timeline events={d.events} />
            </Card>
          </div>
        );
      }}
    </Async>
  );
}
