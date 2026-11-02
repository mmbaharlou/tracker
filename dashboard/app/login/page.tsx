"use client";
import { useState } from "react";

export default function Login() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true); setError("");
    try {
      const res = await fetch("/api/dash/login", {
        method: "POST", credentials: "same-origin",
        headers: { "Content-Type": "application/json", "X-Tracker-Csrf": "1" },
        body: JSON.stringify({ username, password }),
      });
      if (res.ok) { location.href = "/"; return; }
      setError(res.status === 429 ? "تلاش‌های زیاد. چند دقیقه بعد دوباره امتحان کنید." : "نام کاربری یا رمز عبور اشتباه است.");
    } catch {
      setError("ارتباط با سرور برقرار نشد.");
    } finally {
      setBusy(false); setPassword("");
    }
  };

  return (
    <div className="login">
      <form className="panel" onSubmit={submit}>
        <div className="brand" style={{ margin: 0 }}>ره<span style={{ color: "var(--accent)" }}>گیر</span></div>
        <div className="muted">ورود به داشبورد رهگیری کاربران</div>
        <input className="input" placeholder="نام کاربری" autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} required />
        <input className="input" placeholder="رمز عبور" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        {error && <div className="error">{error}</div>}
        <button className="btn primary" disabled={busy}>{busy ? "…" : "ورود"}</button>
      </form>
    </div>
  );
}
