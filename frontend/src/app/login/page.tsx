"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Eye, EyeOff, LockKeyhole } from "lucide-react";
import { app, Logo } from "@/lib/app";
import { post } from "@/lib/api";
import { Button, ErrorBox, inputCls } from "@/components/ui";

export default function LoginPage() {
  return <Suspense><Login /></Suspense>;
}

function Login() {
  const router = useRouter();
  const search = useSearchParams();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [remember, setRemember] = useState(false);
  const [show, setShow] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const r = await post<{ mustChangePassword: boolean }>("/api/auth/login", { username, password, remember });
      const next = search.get("next");
      // Only same-site paths: never redirect to another origin after login.
      const safe = next && next.startsWith("/") && !next.startsWith("//") && !next.startsWith("/login") ? next : "/";
      router.replace(r.mustChangePassword ? "/account?force=1" : safe);
    } catch (err) {
      setError((err as Error).message);
      setPassword("");
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="grid min-h-dvh place-items-center p-4">
      <form onSubmit={submit} className="animate-rise w-full max-w-sm rounded-3xl border border-white/60 bg-surface p-6 shadow-lift sm:p-8">
        <div className="mb-6 flex flex-col items-center gap-3 text-center">
          <div className="grid size-16 place-items-center rounded-3xl bg-surface-solid shadow-card"><Logo size={38} /></div>
          <h1 className="text-xl font-extrabold">{app.name}</h1>
          <p className="text-sm text-ink-3">{app.tagline}</p>
        </div>
        <ErrorBox error={error} />
        <label className="mb-4 block">
          <span className="text-sm font-medium text-ink-2">نام کاربری</span>
          <input className={`${inputCls} mt-1`} dir="ltr" autoComplete="username" autoCapitalize="none" spellCheck={false} required autoFocus
            value={username} onChange={(e) => setUsername(e.target.value)} maxLength={64} />
        </label>
        <label className="mb-4 block">
          <span className="text-sm font-medium text-ink-2">رمز عبور</span>
          <div className="relative mt-1">
            <input className={`${inputCls} pe-10`} dir="ltr" type={show ? "text" : "password"} autoComplete="current-password" required
              value={password} onChange={(e) => setPassword(e.target.value)} maxLength={128} />
            <button type="button" onClick={() => setShow(!show)} className="absolute inset-y-0 end-0 grid w-10 place-items-center text-ink-3 hover:text-ink"
              aria-label={show ? "پنهان کردن رمز" : "نمایش رمز"}>
              {show ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
            </button>
          </div>
        </label>
        <label className="mb-6 flex cursor-pointer items-center gap-2 text-sm text-ink-2">
          <input type="checkbox" className="size-4 accent-[var(--brand)]" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
          مرا به خاطر بسپار (۳۰ روز روی این دستگاه)
        </label>
        <Button variant="primary" className="w-full" busy={busy} type="submit"><LockKeyhole className="size-4" aria-hidden /> ورود</Button>
        <p className="mt-5 text-center text-xs leading-6 text-ink-3">بعد از ۵ تلاش ناموفق، حساب برای مدتی قفل می‌شود. همه‌ی ورودها ثبت می‌شوند.</p>
      </form>
    </main>
  );
}
