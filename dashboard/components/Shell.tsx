"use client";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api } from "@/lib/api";

interface Me { username: string; isAdmin: boolean; projects: { key: string; name: string }[] }
interface Ctx { me: Me; project: string }

const ShellCtx = createContext<Ctx | null>(null);
export const useShell = () => useContext(ShellCtx)!;

const NAV = [
  { href: "/", label: "نمای کلی" },
  { href: "/events/", label: "رویدادهای زنده" },
  { href: "/users/", label: "کاربران" },
];

export function Shell({ title, actions, children }: { title: string; actions?: ReactNode; children: ReactNode }) {
  const path = usePathname();
  const [me, setMe] = useState<Me | null>(null);
  const [project, setProject] = useState("");

  useEffect(() => {
    api<Me>("me").then((m) => {
      setMe(m);
      let saved = "";
      try { saved = localStorage.getItem("rahgir_dash_project") ?? ""; } catch { /* ignore */ }
      const fromUrl = new URLSearchParams(location.search).get("p") ?? "";
      const pick = [fromUrl, saved].find((k) => m.projects.some((p) => p.key === k)) ?? m.projects[0]?.key ?? "";
      setProject(pick);
    }).catch(() => { /* redirected to login */ });
  }, []);

  const choose = (k: string) => {
    setProject(k);
    try { localStorage.setItem("rahgir_dash_project", k); } catch { /* ignore */ }
  };

  const logout = async () => {
    await api("logout", undefined, "POST").catch(() => null);
    location.href = "/login/";
  };

  return (
    <div className="shell">
      <aside className="side">
        <div className="brand">ره<span>گیر</span></div>
        <nav className="nav">
          {NAV.map((n) => (
            <Link key={n.href} href={n.href} className={path === n.href ? "active" : ""}>{n.label}</Link>
          ))}
        </nav>
        <div className="bottom">
          {me && me.projects.length > 0 && (
            <select className="select" style={{ width: "100%", marginBottom: 10 }} value={project} onChange={(e) => choose(e.target.value)} aria-label="پروژه">
              {me.projects.map((p) => <option key={p.key} value={p.key}>{p.name}</option>)}
            </select>
          )}
          {me && <div className="muted" style={{ fontSize: 12.5 }}>{me.username} · <a href="#" onClick={(e) => { e.preventDefault(); logout(); }}>خروج</a></div>}
        </div>
      </aside>
      <main className="main">
        <div className="topbar"><h1>{title}</h1>{actions}</div>
        {!me ? <div className="empty">در حال بارگذاری…</div>
          : !project ? <div className="empty">هیچ پروژه‌ای برای شما تعریف نشده است.</div>
          : <ShellCtx.Provider value={{ me, project }}>{children}</ShellCtx.Provider>}
      </main>
    </div>
  );
}
