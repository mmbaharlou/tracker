"use client";
import { useEffect, useState } from "react";
import { Shell, useShell } from "@/components/Shell";
import { Timeline } from "@/components/Timeline";
import { api, fullDate, num, parseJson } from "@/lib/api";

export default function Page() {
  return <Shell title="پرونده کاربر"><UserView /></Shell>;
}

function UserView() {
  const { project, me } = useShell();
  const [id, setId] = useState("");
  const [data, setData] = useState<any>(null);
  const [error, setError] = useState(false);

  useEffect(() => { setId(new URLSearchParams(location.search).get("id") ?? ""); }, []);
  useEffect(() => {
    if (!id) return;
    setData(null);
    api("user", { project, id }).then(setData).catch(() => setError(true));
  }, [project, id]);

  const forget = async () => {
    if (!confirm(`همه داده‌های کاربر ${id} برای همیشه حذف شود؟`)) return;
    await api("user/forget", { project, id }, "POST");
    location.href = "/users/";
  };

  if (!id) return <div className="empty">کاربری انتخاب نشده.</div>;
  if (error) return <div className="empty">خطا در دریافت داده.</div>;
  if (!data) return <div className="empty">در حال بارگذاری…</div>;
  const p = data.profile;
  const traits = parseJson(p?.traits) ?? {};
  const s = data.stats ?? {};

  return (
    <div className="grid" style={{ gridTemplateColumns: "300px 1fr", alignItems: "start" }}>
      <div className="panel">
        <h2 className="ltr mono" style={{ wordBreak: "break-all" }}>{id}</h2>
        {!p && <div className="muted">پروفایلی ثبت نشده؛ فقط رویدادها نمایش داده می‌شوند.</div>}
        <table className="t">
          <tbody>
            {Object.entries(traits).map(([k, v]) => <tr key={k}><td className="muted">{k}</td><td>{typeof v === "object" ? JSON.stringify(v) : String(v)}</td></tr>)}
            {p && <tr><td className="muted">اولین بازدید</td><td>{fullDate(p.firstSeen)}</td></tr>}
            {p && <tr><td className="muted">آخرین فعالیت</td><td>{fullDate(p.lastSeen)}</td></tr>}
            <tr><td className="muted">نشست‌ها</td><td>{num(s.sessions)}</td></tr>
            <tr><td className="muted">بازدید صفحه</td><td>{num(s.pageViews)}</td></tr>
            <tr><td className="muted">درخواست API</td><td>{num(s.apiCalls)}</td></tr>
            {p && <tr><td className="muted">دستگاه</td><td>{[p.lastDevice, p.lastOs, p.lastBrowser].filter(Boolean).join(" · ")}</td></tr>}
            {p?.lastIp && <tr><td className="muted">آخرین IP</td><td className="ltr">{p.lastIp}</td></tr>}
            <tr><td className="muted">شناسه‌های ناشناس</td><td>{num(data.aliases.length)}</td></tr>
          </tbody>
        </table>
        {me.isAdmin && <button className="btn danger" style={{ marginTop: 12, width: "100%" }} onClick={forget}>حذف کامل داده‌های این کاربر</button>}
      </div>
      <div className="panel">
        <h2>همه کارهایی که انجام داده (جدیدترین بالا)</h2>
        <Timeline events={data.events} />
      </div>
    </div>
  );
}
