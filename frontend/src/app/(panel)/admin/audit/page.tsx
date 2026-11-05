"use client";

import { useState } from "react";
import { qs } from "@/lib/api";
import { fullDate } from "@/lib/format";
import { Badge, Button, ErrorBox, inputBase, PageTitle, Select, Table, useData, type Tone } from "@/components/ui";

interface Entry { id: number; at: string; userId?: number; username?: string; action: string; target?: string; detail?: string; ip?: string }

const labels: Record<string, [string, Tone]> = {
  "login.ok": ["ورود موفق", "green"],
  "login.failed": ["ورود ناموفق", "red"],
  "login.locked": ["تلاش روی حساب قفل", "red"],
  "login.ip-blocked": ["IP مسدود", "red"],
  "login.disabled": ["ورود حساب غیرفعال", "red"],
  logout: ["خروج", "gray"],
  "password.changed": ["تغییر رمز", "blue"],
  "password.change-failed": ["تغییر رمز ناموفق", "amber"],
  "session.revoke": ["خروج یک دستگاه", "gray"],
  "session.revoke-others": ["خروج بقیه‌ی دستگاه‌ها", "gray"],
  "user.create": ["ساخت کاربر", "brand"],
  "user.update": ["ویرایش کاربر", "brand"],
  "user.delete": ["حذف کاربر", "red"],
  "user.reset-password": ["رمز موقت", "amber"],
  "user.unlock": ["باز کردن قفل", "brand"],
  "user.revoke-sessions": ["خروج اجباری", "amber"],
  "user.bootstrap": ["ساخت مالک اولیه", "brand"],
  "project.create": ["پروژه‌ی جدید", "brand"],
  "project.update": ["ویرایش پروژه", "brand"],
  "project.rotate": ["کلید جدید", "amber"],
  "project.delete": ["حذف پروژه", "red"],
  "project.import": ["انتقال از نسخه‌ی قبل", "brand"],
  "user.import": ["انتقال کاربر از نسخه‌ی قبل", "brand"],
  "settings.alerts": ["تنظیمات هشدار", "amber"],
  "settings.test-alert": ["پیام آزمایشی", "gray"],
  "data.forget-user": ["حذف داده‌ی کاربر", "red"],
};

export default function AuditPage() {
  const [q, setQ] = useState("");
  const [action, setAction] = useState("");
  const [search, setSearch] = useState("");
  const [before, setBefore] = useState<number | null>(null);
  const { data, error } = useData<Entry[]>(`/api/admin/audit${qs({ q: search, action, before, take: 100 })}`);

  return (
    <>
      <PageTitle title="گزارش امنیتی" sub="ورودها، تغییر رمزها و همه‌ی تغییرات مدیریتی، با زمان و IP." />
      <form className="mb-4 flex flex-wrap gap-2" onSubmit={(e) => { e.preventDefault(); setBefore(null); setSearch(q); }}>
        <Select value={action} onChange={(v) => { setBefore(null); setAction(v); }} label="نوع">
          <option value="">همه</option>
          <option value="login">ورود</option>
          <option value="password">رمز</option>
          <option value="user">کاربران</option>
          <option value="project">پروژه‌ها</option>
          <option value="session">نشست‌ها</option>
        </Select>
        <input className={`${inputBase} w-56`} placeholder="نام کاربری، IP یا هدف…" value={q} onChange={(e) => setQ(e.target.value)} />
        <Button type="submit">جستجو</Button>
      </form>
      <ErrorBox error={error} />
      <Table head={["زمان", "کاربر", "رویداد", "هدف", "جزئیات", "IP"]}>
        {(data ?? []).map((e) => {
          const [label, tone] = labels[e.action] ?? [e.action, "gray" as Tone];
          return (
            <tr key={e.id}>
              <td className="whitespace-nowrap text-xs">{fullDate(e.at)}</td>
              <td className="ltr text-start text-xs">{e.username ?? "—"}</td>
              <td><Badge tone={tone}>{label}</Badge></td>
              <td className="ltr text-start text-xs">{e.target ?? ""}</td>
              <td className="text-xs text-ink-2">{e.detail ?? ""}</td>
              <td className="ltr text-start text-xs text-ink-3">{e.ip ?? ""}</td>
            </tr>
          );
        })}
      </Table>
      {data && data.length === 100 && (
        <div className="mt-4 text-center"><Button onClick={() => setBefore(data[data.length - 1].id)}>قدیمی‌ترها</Button></div>
      )}
    </>
  );
}
