import { Activity, LayoutDashboard, Users, type LucideIcon } from "lucide-react";

export interface NavItem { href: string; label: string; icon: LucideIcon }

export const app = {
  name: "ترکر راینوکس",
  tagline: "رفتار کاربران همه‌ی سایت‌ها در یک پنل",
  nav: [
    { href: "/", label: "نمای کلی", icon: LayoutDashboard },
    { href: "/events", label: "رویدادهای زنده", icon: Activity },
    { href: "/users", label: "کاربران سایت", icon: Users },
  ] as NavItem[],
};

export function Logo({ size = 28 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden>
      <rect width="32" height="32" rx="9" fill="var(--brand)" />
      <circle cx="16" cy="13" r="5" fill="none" stroke="#fff" strokeWidth="2.6" />
      <path d="M8 25c1.8-3.6 4.6-5.4 8-5.4s6.2 1.8 8 5.4" fill="none" stroke="#fff" strokeWidth="2.6" strokeLinecap="round" />
      <circle cx="24" cy="8" r="2.4" fill="#5eead4" />
    </svg>
  );
}
