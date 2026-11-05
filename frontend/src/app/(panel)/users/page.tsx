"use client";

import Link from "next/link";
import { useState } from "react";
import { qs } from "@/lib/api";
import { ago, fullDate, num, parseJson } from "@/lib/format";
import { NeedsProject } from "@/components/PanelShell";
import { withProject } from "@/components/session";
import { Async, Button, Empty, inputBase, PageTitle, Pager, Table, useData } from "@/components/ui";

interface UserRow { userId: string; traits?: string; firstSeen: string; lastSeen: string; eventCount: number; lastBrowser?: string; lastOs?: string; lastDevice?: string }

export default function UsersPage() {
  return (
    <>
      <PageTitle title="کاربران سایت" sub="کاربرانی که وارد سایت شده‌اند (identify یا درخواست API با کاربر)." />
      <NeedsProject>{(project) => <Users key={project} project={project} />}</NeedsProject>
    </>
  );
}

function Users({ project }: { project: string }) {
  const [q, setQ] = useState("");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const state = useData<{ total: number; items: UserRow[] }>(`/api/data/users${qs({ project, q: search, page })}`);

  return (
    <>
      <form onSubmit={(e) => { e.preventDefault(); setPage(1); setSearch(q); }} className="mb-4 flex flex-wrap items-center gap-2">
        <input className={`${inputBase} w-72`} placeholder="جستجوی شناسه، نام، ایمیل، موبایل…" value={q} onChange={(e) => setQ(e.target.value)} />
        <Button type="submit">جستجو</Button>
        {state.data && <span className="text-sm text-ink-3">{num(state.data.total)} کاربر</span>}
      </form>
      <Async state={state}>
        {(d) => !d.items.length ? <Empty>کاربری پیدا نشد. کاربران بعد از identify یا ورود به API ثبت می‌شوند.</Empty> : (
          <>
            <Table head={["کاربر", "نام / مشخصات", "آخرین فعالیت", "اولین بازدید", "رویدادها", "دستگاه"]}>
              {d.items.map((u) => {
                const t = parseJson<Record<string, unknown>>(u.traits) ?? {};
                return (
                  <tr key={u.userId}>
                    <td><Link href={withProject(`/user?id=${encodeURIComponent(u.userId)}`, project)} className="ltr font-mono text-brand hover:underline">{u.userId}</Link></td>
                    <td>{String(t.name ?? t.fullName ?? t.email ?? t.mobile ?? "—")}</td>
                    <td title={fullDate(u.lastSeen)}>{ago(u.lastSeen)}</td>
                    <td className="text-xs">{fullDate(u.firstSeen)}</td>
                    <td className="tabular">{num(u.eventCount)}</td>
                    <td className="text-xs text-ink-3">{[u.lastDevice, u.lastOs, u.lastBrowser].filter(Boolean).join(" · ")}</td>
                  </tr>
                );
              })}
            </Table>
            <Pager page={page} total={d.total} pageSize={50} onPage={setPage} />
          </>
        )}
      </Async>
    </>
  );
}
