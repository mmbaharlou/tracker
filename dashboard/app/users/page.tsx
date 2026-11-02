"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { Shell, useShell } from "@/components/Shell";
import { ago, api, fullDate, num, parseJson } from "@/lib/api";

export default function Page() {
  return <Shell title="کاربران"><Users /></Shell>;
}

function Users() {
  const { project } = useShell();
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<{ total: number; items: any[] } | null>(null);

  const load = (p = page) => api("users", { project, q, page: p }).then(setData).catch(() => setData({ total: 0, items: [] }));
  useEffect(() => { setPage(1); load(1); /* eslint-disable-next-line react-hooks/exhaustive-deps */ }, [project]);

  const pages = data ? Math.max(1, Math.ceil(data.total / 50)) : 1;
  return (
    <>
      <div className="topbar">
        <form onSubmit={(e) => { e.preventDefault(); setPage(1); load(1); }} style={{ display: "flex", gap: 8 }}>
          <input className="input" placeholder="جستجوی شناسه، نام، ایمیل…" value={q} onChange={(e) => setQ(e.target.value)} />
          <button className="btn">جستجو</button>
        </form>
        {data && <span className="muted">{num(data.total)} کاربر</span>}
      </div>
      <div className="panel">
        {!data ? <div className="empty">در حال بارگذاری…</div> : !data.items.length ? <div className="empty">کاربری پیدا نشد. کاربران بعد از identify یا ورود به API ثبت می‌شوند.</div> : (
          <table className="t">
            <thead><tr><th>کاربر</th><th>نام / مشخصات</th><th>آخرین فعالیت</th><th>اولین بازدید</th><th>رویدادها</th><th>دستگاه</th></tr></thead>
            <tbody>
              {data.items.map((u) => {
                const traits = parseJson(u.traits) ?? {};
                return (
                  <tr key={u.userId}>
                    <td><Link href={`/user/?id=${encodeURIComponent(u.userId)}`} className="ltr mono">{u.userId}</Link></td>
                    <td>{String(traits.name ?? traits.fullName ?? traits.email ?? traits.mobile ?? "—")}</td>
                    <td title={fullDate(u.lastSeen)}>{ago(u.lastSeen)}</td>
                    <td>{fullDate(u.firstSeen)}</td>
                    <td>{num(u.eventCount)}</td>
                    <td className="muted">{[u.lastDevice, u.lastOs, u.lastBrowser].filter(Boolean).join(" · ")}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
        {pages > 1 && (
          <div style={{ display: "flex", gap: 8, justifyContent: "center", marginTop: 12 }}>
            <button className="btn" disabled={page <= 1} onClick={() => { setPage(page - 1); load(page - 1); }}>قبلی</button>
            <span className="muted">صفحه {num(page)} از {num(pages)}</span>
            <button className="btn" disabled={page >= pages} onClick={() => { setPage(page + 1); load(page + 1); }}>بعدی</button>
          </div>
        )}
      </div>
    </>
  );
}
