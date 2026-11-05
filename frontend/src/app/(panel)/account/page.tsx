"use client";

import { useState } from "react";
import { useSearchParams } from "next/navigation";
import { MonitorSmartphone, ShieldCheck } from "lucide-react";
import { del, post } from "@/lib/api";
import { ago, deviceName, fullDate } from "@/lib/format";
import { Badge, Button, Card, ErrorBox, Field, inputCls, Notice, PageTitle, SuccessBox, Table, useAction, useData } from "@/components/ui";
import { roleLabel, useSession } from "@/components/session";

interface SessionRow { id: string; createdAt: string; lastSeenAt: string; expiresAt: string; ip?: string; userAgent?: string; persistent: boolean; current: boolean }

export default function AccountPage() {
  const { me, reloadMe } = useSession();
  const force = useSearchParams().get("force") === "1" || me.mustChangePassword;
  const sessions = useData<SessionRow[]>("/api/auth/sessions");
  const pw = useAction();
  const act = useAction();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");

  const change = async (e: React.FormEvent) => {
    e.preventDefault();
    if (next !== repeat) { pw.setError("رمز جدید و تکرار آن یکی نیستند."); return; }
    const ok = await pw.run(() => post("/api/auth/change-password", { currentPassword: current, newPassword: next }), "رمز عبور عوض شد. بقیه‌ی دستگاه‌ها از حساب خارج شدند.");
    if (ok) { setCurrent(""); setNext(""); setRepeat(""); await reloadMe(); sessions.reload(true); }
  };

  return (
    <>
      <PageTitle title="حساب من" sub={<>{me.username} · {roleLabel[me.role]}</>} />
      {force && me.mustChangePassword && (
        <Notice tone="amber">این حساب با رمز موقت ساخته شده یا رمزش توسط مدیر عوض شده است. برای ادامه، یک رمز جدید انتخاب کنید.</Notice>
      )}
      <div className="grid gap-6 xl:grid-cols-[minmax(0,420px)_1fr]">
        <Card title={<span className="flex items-center gap-2"><ShieldCheck className="size-5 text-brand" /> تغییر رمز عبور</span>}>
          <form onSubmit={change} className="space-y-4">
            <ErrorBox error={pw.error} />
            <SuccessBox message={pw.message} />
            <Field label="رمز فعلی">
              <input className={inputCls} dir="ltr" type="password" autoComplete="current-password" required value={current} onChange={(e) => setCurrent(e.target.value)} />
            </Field>
            <Field label="رمز جدید" hint="حداقل ۱۲ کاراکتر، با سه نوع از: حرف کوچک، حرف بزرگ، عدد، نماد (یا یک عبارت ۱۶ کاراکتری به بالا). شامل نام کاربری نباشد.">
              <input className={inputCls} dir="ltr" type="password" autoComplete="new-password" required minLength={12} value={next} onChange={(e) => setNext(e.target.value)} />
            </Field>
            <Field label="تکرار رمز جدید">
              <input className={inputCls} dir="ltr" type="password" autoComplete="new-password" required value={repeat} onChange={(e) => setRepeat(e.target.value)} />
            </Field>
            <Button variant="primary" busy={pw.busy} type="submit">ذخیره‌ی رمز جدید</Button>
            {me.passwordChangedAt && <p className="text-xs text-ink-3">آخرین تغییر رمز: {fullDate(me.passwordChangedAt)}</p>}
          </form>
        </Card>

        <div>
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <h2 className="flex items-center gap-2 font-bold"><MonitorSmartphone className="size-5 text-brand" /> دستگاه‌هایی که وارد شده‌اند</h2>
            <Button size="sm" variant="danger" busy={act.busy} disabled={!sessions.data || sessions.data.length < 2}
              onClick={() => act.run(async () => { await post("/api/auth/sessions/revoke-others"); await sessions.reload(true); }, "از همه‌ی دستگاه‌های دیگر خارج شدید.")}>
              خروج از بقیه‌ی دستگاه‌ها
            </Button>
          </div>
          <ErrorBox error={act.error ?? sessions.error} />
          <SuccessBox message={act.message} />
          <Table head={["دستگاه", "IP", "ورود", "آخرین فعالیت", ""]}>
            {(sessions.data ?? []).map((s) => (
              <tr key={s.id}>
                <td>
                  <div className="font-medium">{deviceName(s.userAgent)}</div>
                  <div className="flex gap-1.5 pt-0.5">
                    {s.current && <Badge tone="green">همین دستگاه</Badge>}
                    {s.persistent && <Badge>به خاطر سپرده</Badge>}
                  </div>
                </td>
                <td className="ltr text-xs text-ink-2">{s.ip ?? "—"}</td>
                <td className="text-xs" title={fullDate(s.createdAt)}>{ago(s.createdAt)}</td>
                <td className="text-xs" title={fullDate(s.lastSeenAt)}>{ago(s.lastSeenAt)}</td>
                <td className="text-end">
                  {!s.current && (
                    <Button size="sm" variant="ghost" onClick={() => act.run(async () => { await del(`/api/auth/sessions/${s.id}`); await sessions.reload(true); })}>خروج</Button>
                  )}
                </td>
              </tr>
            ))}
          </Table>
        </div>
      </div>
    </>
  );
}
