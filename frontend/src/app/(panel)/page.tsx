"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { qs } from "@/lib/api";
import { num } from "@/lib/format";
import { NeedsProject } from "@/components/PanelShell";
import { withProject } from "@/components/session";
import { TimeChart, ser } from "@/components/charts";
import { Async, BarList, Card, PageTitle, Select, Stat, useData } from "@/components/ui";

const RANGES = [{ d: 1, label: "۲۴ ساعت اخیر" }, { d: 7, label: "۷ روز اخیر" }, { d: 30, label: "۳۰ روز اخیر" }, { d: 90, label: "۹۰ روز اخیر" }];

interface Row { label?: string; count: number; visitors?: number; failed?: number; kind?: string }
interface Overview {
  totals: { events: number; visitors: number; users: number; sessions: number; pageViews: number; apiCalls: number };
  daily: { day: string; visitors: number; pageViews: number; events: number }[];
  topPages: Row[]; topActions: Row[]; topEvents: Row[]; topReferrers: Row[]; breakdown: Row[];
}

export default function OverviewPage() {
  const [days, setDays] = useState(30);
  return (
    <>
      <PageTitle title="نمای کلی">
        <Select value={days} onChange={(v) => setDays(Number(v))} label="بازه‌ی زمانی">
          {RANGES.map((r) => <option key={r.d} value={r.d}>{r.label}</option>)}
        </Select>
      </PageTitle>
      <NeedsProject>{(project) => <Body project={project} days={days} />}</NeedsProject>
    </>
  );
}

function Body({ project, days }: { project: string; days: number }) {
  const path = useMemo(() => {
    const to = new Date();
    const from = new Date(to.getTime() - days * 86400000);
    return `/api/data/overview${qs({ project, from: from.toISOString(), to: to.toISOString(), tz: -new Date().getTimezoneOffset() })}`;
  }, [project, days]);
  const state = useData<Overview>(path, { refreshMs: 60000 });

  return (
    <Async state={state}>
      {(d) => {
        const t = d.totals;
        const group = (kind: string) => d.breakdown.filter((b) => b.kind === kind).sort((a, b) => b.count - a.count).map((b) => ({ label: b.label ?? "نامشخص", count: b.count }));
        return (
          <div className="space-y-6">
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
              <Stat label="بازدیدکننده" value={num(t.visitors)} />
              <Stat label="کاربر واردشده" value={num(t.users)} />
              <Stat label="نشست" value={num(t.sessions)} />
              <Stat label="بازدید صفحه" value={num(t.pageViews ?? 0)} />
              <Stat label="درخواست API" value={num(t.apiCalls ?? 0)} />
              <Stat label="کل رویدادها" value={num(t.events)} />
            </div>
            <Card title="روند روزانه">
              <TimeChart kind="bar" daily series={[ser(d.daily, "visitors", "بازدیدکننده"), ser(d.daily, "pageViews", "بازدید صفحه")]} height={220} />
            </Card>
            <div className="grid gap-6 lg:grid-cols-2">
              <Card title="صفحات پربازدید">
                <BarList items={d.topPages.map((p) => ({ label: <span className="ltr">{p.label}</span>, count: p.count, extra: `${num(p.visitors)} نفر` }))} />
              </Card>
              <Card title="پرکاربردترین کارها در API">
                <BarList items={d.topActions.map((a) => ({ label: a.label, count: a.count, extra: a.failed ? `${num(a.failed)} ناموفق` : `${num(a.visitors)} کاربر` }))} />
              </Card>
              <Card title="رویدادهای سفارشی">
                <BarList items={d.topEvents.map((a) => ({ label: a.label, count: a.count, extra: `${num(a.visitors)} نفر` }))} empty="هنوز با track() رویدادی ثبت نشده" />
              </Card>
              <Card title="منابع ورود">
                <BarList items={d.topReferrers.map((r) => ({ label: <span className="ltr">{hostOf(r.label ?? "")}</span>, count: r.count }))} empty="ورودی از سایت دیگری ثبت نشده" />
              </Card>
            </div>
            <div className="grid gap-6 md:grid-cols-3">
              <Card title="دستگاه"><BarList items={group("device")} /></Card>
              <Card title="سیستم‌عامل"><BarList items={group("os")} /></Card>
              <Card title="مرورگر"><BarList items={group("browser")} /></Card>
            </div>
            <p className="text-sm text-ink-3">برای دیدن کارهای یک کاربر خاص به <Link className="text-brand underline" href={withProject("/users", project)}>کاربران سایت</Link> بروید.</p>
          </div>
        );
      }}
    </Async>
  );
}

function hostOf(url: string) {
  try { return new URL(url).host; } catch { return url; }
}
