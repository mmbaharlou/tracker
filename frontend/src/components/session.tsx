"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { api, ApiError } from "@/lib/api";

export type Role = "Viewer" | "Admin" | "Owner";
export interface ProjectRef { key: string; name: string; isEnabled: boolean }
export interface Me {
  id: number;
  username: string;
  displayName?: string | null;
  role: Role;
  mustChangePassword: boolean;
  lastLoginAt?: string;
  passwordChangedAt?: string;
  projects: ProjectRef[];
}

interface Ctx {
  me: Me;
  reloadMe: () => Promise<void>;
  /** Selected project key ("" when the user has none). Kept in the URL (?p=) and remembered per browser. */
  project: string;
  setProject: (key: string) => void;
  isAdmin: boolean;
  isOwner: boolean;
}

const SessionCtx = createContext<Ctx | null>(null);
export const useSession = () => {
  const c = useContext(SessionCtx);
  if (!c) throw new Error("useSession outside SessionProvider");
  return c;
};

const PROJECT_KEY = "panel.project";

export function SessionProvider({ children, fallback }: { children: React.ReactNode; fallback: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const search = useSearchParams();
  const [me, setMe] = useState<Me | null>(null);
  const [project, setProjectState] = useState("");

  const toLogin = useCallback(() => {
    const next = encodeURIComponent(pathname + (search.size ? `?${search}` : ""));
    router.replace(`/login?next=${next}`);
  }, [pathname, search, router]);

  const reloadMe = useCallback(async () => {
    try {
      const m = await api<Me>("/api/auth/me");
      setMe(m);
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) toLogin();
    }
  }, [toLogin]);

  useEffect(() => { reloadMe(); }, [reloadMe]);

  // Any 401 later (session revoked or expired) sends the user back to the login page.
  useEffect(() => {
    const onErr = (e: Event) => {
      const err = (e as CustomEvent<ApiError>).detail;
      if (err.status === 401) toLogin();
      if (err.code === "password_change_required") router.replace("/account?force=1");
    };
    window.addEventListener("panel:api-error", onErr);
    return () => window.removeEventListener("panel:api-error", onErr);
  }, [toLogin, router]);

  useEffect(() => {
    if (me?.mustChangePassword && pathname !== "/account") router.replace("/account?force=1");
  }, [me, pathname, router]);

  // Pick the project: URL, then last used, then the first one.
  const urlProject = search.get("p") ?? "";
  useEffect(() => {
    if (!me) return;
    let saved = "";
    try { saved = localStorage.getItem(PROJECT_KEY) ?? ""; } catch { /* storage unavailable */ }
    const pick = [urlProject, saved, project].find((k) => k && me.projects.some((p) => p.key === k)) ?? me.projects[0]?.key ?? "";
    setProjectState(pick);
  }, [me, urlProject]); // eslint-disable-line react-hooks/exhaustive-deps

  const setProject = useCallback((key: string) => {
    setProjectState(key);
    try { localStorage.setItem(PROJECT_KEY, key); } catch { /* storage unavailable */ }
    const p = new URLSearchParams(search.toString());
    p.set("p", key);
    router.replace(`${pathname}?${p}`);
  }, [pathname, router, search]);

  const value = useMemo<Ctx | null>(() => me && {
    me, reloadMe, project, setProject,
    isAdmin: me.role === "Admin" || me.role === "Owner",
    isOwner: me.role === "Owner",
  }, [me, reloadMe, project, setProject]);

  if (!value) return <>{fallback}</>;
  return <SessionCtx.Provider value={value}>{children}</SessionCtx.Provider>;
}

export const roleLabel: Record<Role, string> = { Viewer: "بیننده", Admin: "مدیر", Owner: "مالک" };

/** Link target that keeps the selected project. */
export function withProject(href: string, project: string) {
  if (!project) return href;
  return href + (href.includes("?") ? "&" : "?") + "p=" + encodeURIComponent(project);
}
