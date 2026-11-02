"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { Shell, useShell } from "@/components/Shell";
import { BarList } from "@/components/BarList";
import { DailyChart } from "@/components/DailyChart";
import { api, num } from "@/lib/api";

const RANGES = [
  { d: 1, label: "۲۴ ساعت" }, { d: 7, label: "۷ روز" }, { d: 30, label: "۳۰ روز" }, { d: 90, label: "۹۰ روز" },
];

export default function Page() {
  const [days, setDays] = useState(30);
  return (
    <Shell title="نمای کلی" actions={
      <select className="select" value={days} onChange={(e) => setDays(Number(e.target.value))} aria-label="بازه زمانی">
        {RANGES.map((r) => <option key={r.d} value={r.d}>{r.label} اخیر</option>)}
      </select>}>
      <Overview days={days} />
    </Shell>
  );
}

function Overview({ days }: { days: number }) {
  const { project } = useShell();
  const [data, setData] = useState<any>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    setData(null); setError(false);
    const to = new Date();
    const from = new Date(to.getTime() - days * 86400000);
    api("overview", { project, from: from.toISOString(), to: to.toISOString(), tz: -new Date().getTimezoneOffset() })
      .then(setData).catch(() => setError(true));
  }, [project, days]);

  if (error) return <div className="empty">خطا در دریافت داده.</div>;
  if (!data) return <div className="empty">در حال بارگذاری…</div>;
  const t = data.totals ?? {};
  const kpis = [
    ["بازدیدکننده", t.visitors], ["کاربر واردشده", t.users], ["نشست", t.sessions],
    ["بازدید صفحه", t.pageViews], ["درخواست API", t.apiCalls], ["کل رویدادها", t.events],
  ];
  const group = (kind: string) => (data.breakdown as any[]).filter((b) => b.kind === kind)
    .sort((a, b) => b.count - a.count).map((b) => ({ label: b.label ?? "نامشخص", count: b.count }));

  return (
    <>
      <div className="kpis">
        {kpis.map(([l, v]) => <div className="panel kpi" key={l as string}><div className="v">{num(v as number)}</div><div className="l">{l}</div></div>)}
      </div>
      <div className="panel" style={{ marginBottom: 16 }}>
        <h2>بازدیدکنندگان روزانه</h2>
        <DailyChart days={data.daily} />
      </div>
      <div className="grid g2" style={{ marginBottom: 16 }}>
        <div className="panel"><h2>صفحات پربازدید</h2>
          <BarList items={data.topPages.map((p: any) => ({ label: <span className="ltr">{p.label}</span>, count: p.count, extra: `${num(p.visitors)} نفر` }))} />
        </div>
        <div className="panel"><h2>پرکاربردترین اکشن‌های API</h2>
          <BarList items={data.topActions.map((a: any) => ({ label: a.label, count: a.count, extra: a.failed ? `${num(a.failed)} ناموفق` : `${num(a.visitors)} کاربر` }))} />
        </div>
        <div className="panel"><h2>رویدادهای سفارشی</h2>
          <BarList items={data.topEvents.map((a: any) => ({ label: a.label, count: a.count, extra: `${num(a.visitors)} نفر` }))} empty="هنوز با track() رویدادی ثبت نشده" />
        </div>
        <div className="panel"><h2>منابع ورود</h2>
          <BarList items={data.topReferrers.map((r: any) => ({ label: <span className="ltr">{hostOf(r.label)}</span>, count: r.count }))} empty="ورودی از سایت دیگری ثبت نشده" />
        </div>
      </div>
      <div className="grid g3">
        <div className="panel"><h2>دستگاه</h2><BarList items={group("device")} /></div>
        <div className="panel"><h2>سیستم‌عامل</h2><BarList items={group("os")} /></div>
        <div className="panel"><h2>مرورگر</h2><BarList items={group("browser")} /></div>
      </div>
      <p className="muted" style={{ marginTop: 16 }}>برای دیدن فعالیت یک کاربر خاص به <Link href="/users/">کاربران</Link> بروید.</p>
    </>
  );
}

function hostOf(url: string) {
  try { return new URL(url).host; } catch { return url; }
}
