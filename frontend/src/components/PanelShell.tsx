"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { FolderKanban, KeyRound, LogOut, Menu, ScrollText, UserCog, X } from "lucide-react";
import { app, Logo, type NavItem } from "@/lib/app";
import { post } from "@/lib/api";
import { Loading } from "@/components/ui";
import { roleLabel, SessionProvider, useSession, withProject } from "@/components/session";

const adminNav: NavItem[] = [
  { href: "/admin/projects", label: "پروژه‌ها (سایت‌ها)", icon: FolderKanban },
  { href: "/admin/users", label: "کاربران پنل", icon: UserCog },
  { href: "/admin/audit", label: "گزارش امنیتی", icon: ScrollText },
];

export function PanelShell({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={<Splash />}>
      <SessionProvider fallback={<Splash />}>
        <Frame>{children}</Frame>
      </SessionProvider>
    </Suspense>
  );
}

function Splash() {
  return <div className="grid min-h-dvh place-items-center"><Loading /></div>;
}

function Frame({ children }: { children: React.ReactNode }) {
  const { me, project, setProject, isAdmin } = useSession();
  const pathname = usePathname();
  const router = useRouter();
  const [menuOpen, setMenuOpen] = useState(false);
  useEffect(() => setMenuOpen(false), [pathname]);

  const logout = async () => {
    await post("/api/auth/logout").catch(() => null);
    router.replace("/login");
  };

  const link = (n: NavItem) => {
    const active = n.href === "/" ? pathname === "/" : pathname.startsWith(n.href);
    return (
      <Link key={n.href} href={withProject(n.href, project)} aria-current={active ? "page" : undefined}
        className={`flex h-10 items-center gap-3 rounded-xl px-3 text-sm transition ${active ? "bg-brand-soft font-bold text-brand-strong" : "text-ink-2 hover:bg-surface-2 hover:text-ink"}`}>
        <n.icon className={`size-[18px] ${active ? "text-brand" : "text-ink-3"}`} aria-hidden />
        {n.label}
      </Link>
    );
  };

  const sidebar = (
    <>
      {me.projects.length > 0 && (
        <div className="px-3 pb-3">
          <label className="mb-1 block px-1 text-xs text-ink-3" htmlFor="project-switch">پروژه</label>
          <select id="project-switch" value={project} onChange={(e) => setProject(e.target.value)}
            className="h-10 w-full rounded-xl border border-line-strong bg-surface-solid/90 px-3 text-sm font-medium outline-none focus:border-brand">
            {me.projects.map((p) => <option key={p.key} value={p.key}>{p.name}{p.isEnabled ? "" : " (غیرفعال)"}</option>)}
          </select>
        </div>
      )}
      <nav className="flex-1 space-y-0.5 overflow-y-auto px-3" aria-label="منوی اصلی">
        {app.nav.map(link)}
        {isAdmin && (
          <>
            <div className="px-3 pb-1 pt-5 text-xs font-medium text-ink-3">مدیریت</div>
            {("serverNav" in app ? (app.serverNav as NavItem[]) : []).map(link)}
            {adminNav.map(link)}
          </>
        )}
      </nav>
      <div className="m-3 rounded-2xl bg-surface-2 p-3 text-xs">
        <div className="flex items-center gap-2">
          <span className="grid size-8 shrink-0 place-items-center rounded-full bg-brand text-xs font-bold uppercase text-brand-ink">{me.username.slice(0, 2)}</span>
          <div className="min-w-0">
            <div className="truncate font-bold text-ink">{me.displayName ?? me.username}</div>
            <div className="text-ink-3">{roleLabel[me.role]}</div>
          </div>
        </div>
        <div className="mt-3 flex gap-1">
          <Link href="/account" className="flex h-9 flex-1 items-center justify-center gap-1.5 rounded-lg text-ink-2 hover:bg-surface"><KeyRound className="size-3.5" aria-hidden /> حساب من</Link>
          <button onClick={logout} className="flex h-9 flex-1 items-center justify-center gap-1.5 rounded-lg text-danger hover:bg-danger-soft"><LogOut className="size-3.5" aria-hidden /> خروج</button>
        </div>
      </div>
    </>
  );

  return (
    <div className="min-h-dvh lg:grid lg:grid-cols-[260px_1fr]">
      <aside className="hidden border-e border-line bg-surface lg:sticky lg:top-0 lg:flex lg:h-dvh lg:flex-col">
        <Link href={withProject("/", project)} className="flex h-16 items-center gap-2 px-6 font-extrabold"><Logo size={26} /> {app.name}</Link>
        {sidebar}
      </aside>

      <header className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-line bg-surface/85 px-4 backdrop-blur lg:hidden">
        <Link href={withProject("/", project)} className="flex items-center gap-2 font-extrabold"><Logo size={24} /> {app.name}</Link>
        <button className="grid size-10 place-items-center rounded-full hover:bg-surface-2" onClick={() => setMenuOpen(true)} aria-label="باز کردن منو" aria-expanded={menuOpen}>
          <Menu className="size-5" />
        </button>
      </header>
      {menuOpen && (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="منو">
          <button className="absolute inset-0 bg-ink/40" aria-label="بستن" onClick={() => setMenuOpen(false)} />
          <div className="animate-rise absolute inset-y-0 start-0 flex w-72 max-w-[85vw] flex-col bg-surface-solid shadow-lift">
            <div className="flex h-14 items-center justify-between px-4">
              <span className="font-extrabold">منو</span>
              <button className="grid size-10 place-items-center rounded-full hover:bg-surface-2" onClick={() => setMenuOpen(false)} aria-label="بستن"><X className="size-5" /></button>
            </div>
            {sidebar}
          </div>
        </div>
      )}

      <main className="min-w-0 px-4 py-6 lg:px-10 lg:py-8">{children}</main>
    </div>
  );
}

/** Wraps pages that need a selected project. */
export function NeedsProject({ children }: { children: (project: string) => React.ReactNode }) {
  const { project, isAdmin } = useSession();
  if (!project)
    return (
      <div className="mx-auto max-w-md py-16 text-center">
        <h1 className="mb-2 text-xl font-extrabold">هنوز پروژه‌ای در دسترس شما نیست</h1>
        <p className="text-sm leading-7 text-ink-2">
          {isAdmin ? <>از <Link className="text-brand underline" href="/admin/projects">مدیریت پروژه‌ها</Link> اولین سایت را اضافه کنید.</> : "از مدیر بخواهید دسترسی یک پروژه را به حساب شما بدهد."}
        </p>
      </div>
    );
  return <>{children(project)}</>;
}
