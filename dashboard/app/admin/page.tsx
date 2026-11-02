"use client";
import { useEffect, useState } from "react";
import { Shell, useShell } from "@/components/Shell";
import { api } from "@/lib/api";

interface Settings {
  enabled: boolean; sampleRate?: number | null; webSampleRate?: number | null; trackApiCalls?: boolean | null;
  autocapture?: boolean | null; captureRequestBody?: boolean | null; captureQueryString?: boolean | null;
  ignorePathPrefixes?: string[] | null; includePathPrefixes?: string[] | null;
}
interface Project {
  key: string; name: string; allowedOrigins: string[]; settings: Settings; isEnabled: boolean;
  fromConfig: boolean; hasServerSecret: boolean; hasReadKey: boolean;
}
interface User { username: string; projects: string[]; isAdmin: boolean; fromConfig: boolean }
type Secrets = { title: string; items: [string, string][] };

export default function Page() {
  return <Shell title="مدیریت" needsProject={false}><Admin /></Shell>;
}

function Admin() {
  const { me } = useShell();
  const [projects, setProjects] = useState<Project[]>([]);
  const [users, setUsers] = useState<User[]>([]);
  const [secrets, setSecrets] = useState<Secrets | null>(null);
  const [editing, setEditing] = useState<Project | "new" | null>(null);
  const [error, setError] = useState("");

  const load = () => Promise.all([
    api<Project[]>("admin/projects").then(setProjects),
    api<User[]>("admin/users").then(setUsers),
  ]).catch((e) => setError(e.message));
  useEffect(() => { load(); }, []);

  if (!me.isAdmin) return <div className="empty">فقط مدیر به این بخش دسترسی دارد.</div>;

  const run = async (fn: () => Promise<unknown>) => {
    setError("");
    try { await fn(); await load(); } catch (e: any) { setError(e.message); }
  };

  const rotate = (p: Project, what: "server" | "read") => {
    const label = what === "server" ? "کلید سرور" : "کلید فقط‌خواندنی";
    if (!confirm(`${label} پروژه «${p.name}» عوض شود؟ نصب‌هایی که کلید قبلی را دارند تا وقتی کلید جدید را بگذارید کار نمی‌کنند.`)) return;
    run(async () => {
      const r = await api<{ value: string }>(`admin/projects/${encodeURIComponent(p.key)}/rotate`, { what }, "POST");
      setSecrets({ title: `${label} جدید «${p.name}»`, items: [[what === "server" ? "Rahgir:ServerSecret" : "Rahgir:ReadKey", r.value]] });
    });
  };

  return (
    <>
      {error && <div className="panel error" style={{ marginBottom: 16 }}>{error}</div>}
      {secrets && <SecretsBox s={secrets} onClose={() => setSecrets(null)} />}

      <div className="panel" style={{ marginBottom: 16 }}>
        <div style={{ display: "flex", alignItems: "center", marginBottom: 8 }}>
          <h2 style={{ flex: 1, margin: 0 }}>پروژه‌ها</h2>
          <button className="btn primary" onClick={() => setEditing("new")}>پروژه جدید</button>
        </div>
        <p className="muted" style={{ marginTop: 0 }}>تغییر تنظیمات هر پروژه حداکثر ظرف یک دقیقه روی همه نصب‌های آن اعمال می‌شود.</p>
        <table className="t">
          <thead><tr><th>نام</th><th>کلید</th><th>وضعیت</th><th>Origin های مجاز</th><th>نمونه‌گیری</th><th /></tr></thead>
          <tbody>
            {projects.map((p) => (
              <tr key={p.key}>
                <td>{p.name}{p.fromConfig && <span className="badge" style={{ marginInlineStart: 6 }}>از فایل تنظیمات</span>}</td>
                <td><span className="mono ltr">{p.key}</span></td>
                <td>{!p.isEnabled ? <span className="badge bad">غیرفعال</span> : p.settings.enabled === false ? <span className="badge warn">ثبت خاموش</span> : <span className="badge ok">فعال</span>}</td>
                <td style={{ fontSize: 12 }}>{p.allowedOrigins.length ? p.allowedOrigins.map((o) => <div key={o} className="mono ltr">{o}</div>) : <span className="muted">همه (توصیه نمی‌شود)</span>}</td>
                <td><div>API: {pct(p.settings.sampleRate)}</div><div>مرورگر: {pct(p.settings.webSampleRate)}</div></td>
                <td style={{ whiteSpace: "nowrap" }}>
                  {!p.fromConfig && <>
                    <button className="btn" onClick={() => setEditing(p)}>ویرایش</button>{" "}
                    <button className="btn" onClick={() => rotate(p, "server")}>کلید سرور جدید</button>{" "}
                    <button className="btn" onClick={() => rotate(p, "read")}>کلید خواندن جدید</button>{" "}
                    <button className="btn danger" onClick={() => confirm(`پروژه «${p.name}» حذف شود؟ داده‌های ثبت‌شده باقی می‌مانند ولی دیگر داده جدیدی پذیرفته نمی‌شود.`)
                      && run(() => api(`admin/projects/${encodeURIComponent(p.key)}`, undefined, "DELETE"))}>حذف</button>
                  </>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {editing && <ProjectForm project={editing === "new" ? null : editing} onClose={() => setEditing(null)}
        onSaved={(s) => { setEditing(null); if (s) setSecrets(s); load(); }} />}

      <Users users={users} projects={projects} me={me.username} run={run} />
    </>
  );
}

const pct = (v?: number | null) => (v == null ? "۱۰۰٪" : `${new Intl.NumberFormat("fa-IR").format(Math.round(v * 100))}٪`);
const lines = (s: string) => s.split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
const tri = (v: boolean | null | undefined) => (v == null ? "" : v ? "on" : "off");
const fromTri = (v: string) => (v === "" ? null : v === "on");

function ProjectForm({ project, onClose, onSaved }: { project: Project | null; onClose: () => void; onSaved: (s: Secrets | null) => void }) {
  const s = project?.settings ?? { enabled: true };
  const [key, setKey] = useState(project?.key ?? "");
  const [name, setName] = useState(project?.name ?? "");
  const [origins, setOrigins] = useState((project?.allowedOrigins ?? []).join("\n"));
  const [isEnabled, setIsEnabled] = useState(project?.isEnabled ?? true);
  const [enabled, setEnabled] = useState(s.enabled !== false);
  const [sample, setSample] = useState(s.sampleRate == null ? "" : String(s.sampleRate * 100));
  const [webSample, setWebSample] = useState(s.webSampleRate == null ? "" : String(s.webSampleRate * 100));
  const [trackApi, setTrackApi] = useState(tri(s.trackApiCalls));
  const [autocapture, setAutocapture] = useState(tri(s.autocapture));
  const [body, setBody] = useState(tri(s.captureRequestBody));
  const [ignore, setIgnore] = useState((s.ignorePathPrefixes ?? []).join("\n"));
  const [include, setInclude] = useState((s.includePathPrefixes ?? []).join("\n"));
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setError("");
    const settings: Settings = {
      enabled,
      sampleRate: sample === "" ? null : Number(sample) / 100,
      webSampleRate: webSample === "" ? null : Number(webSample) / 100,
      trackApiCalls: fromTri(trackApi), autocapture: fromTri(autocapture), captureRequestBody: fromTri(body),
      ignorePathPrefixes: lines(ignore).length ? lines(ignore) : null,
      includePathPrefixes: lines(include).length ? lines(include) : null,
    };
    const input = { key, name, allowedOrigins: lines(origins), settings, isEnabled };
    try {
      if (project) { await api(`admin/projects/${encodeURIComponent(project.key)}`, undefined, "PUT", input); onSaved(null); }
      else {
        const r = await api<{ key: string; serverSecret: string; readKey: string }>("admin/projects", undefined, "POST", input);
        onSaved({ title: `کلیدهای پروژه «${name || r.key}»`, items: [["Rahgir:ProjectKey", r.key], ["Rahgir:ServerSecret", r.serverSecret], ["Rahgir:ReadKey", r.readKey]] });
      }
    } catch (err: any) { setError(err.message); } finally { setBusy(false); }
  };

  const triSelect = (v: string, set: (v: string) => void) => (
    <select className="select" value={v} onChange={(e) => set(e.target.value)}>
      <option value="">طبق تنظیمات خود نصب</option><option value="on">روشن</option><option value="off">خاموش</option>
    </select>
  );

  return (
    <form className="panel" style={{ marginBottom: 16, display: "grid", gap: 12 }} onSubmit={save}>
      <h2 style={{ margin: 0 }}>{project ? `ویرایش «${project.name}»` : "پروژه جدید"}</h2>
      <div className="grid g3">
        <label>کلید پروژه (انگلیسی، مثلاً verti)
          <input className="input ltr" style={{ width: "100%" }} value={key} onChange={(e) => setKey(e.target.value)} disabled={!!project} required pattern="[a-z0-9][a-z0-9-]{1,39}" />
        </label>
        <label>نام نمایشی
          <input className="input" style={{ width: "100%" }} value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label>Origin های مجاز مرورگر (هر خط یکی)
          <textarea className="input ltr" rows={2} style={{ width: "100%" }} value={origins} onChange={(e) => setOrigins(e.target.value)} placeholder="https://verti.ir" />
        </label>
      </div>
      <div className="grid g3">
        <label><input type="checkbox" checked={isEnabled} onChange={(e) => setIsEnabled(e.target.checked)} /> پروژه فعال باشد (غیرفعال: هیچ داده‌ای پذیرفته نمی‌شود)</label>
        <label><input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> ثبت رویداد در همه نصب‌ها روشن باشد</label>
      </div>
      <div className="grid g3">
        <label>درصد ثبت درخواست‌های API (خالی = ۱۰۰)
          <input className="input" type="number" min={0} max={100} step="any" style={{ width: "100%" }} value={sample} onChange={(e) => setSample(e.target.value)} />
        </label>
        <label>درصد بازدیدکنندگانی که ردیابی می‌شوند (خالی = ۱۰۰)
          <input className="input" type="number" min={0} max={100} step="any" style={{ width: "100%" }} value={webSample} onChange={(e) => setWebSample(e.target.value)} />
        </label>
        <div />
        <label>ثبت خودکار API ها {triSelect(trackApi, setTrackApi)}</label>
        <label>ثبت خودکار کلیک و فرم {triSelect(autocapture, setAutocapture)}</label>
        <label>ذخیره بدنه درخواست API {triSelect(body, setBody)}</label>
        <label>مسیرهایی که ثبت نشوند (هر خط یکی)
          <textarea className="input ltr" rows={3} style={{ width: "100%" }} value={ignore} onChange={(e) => setIgnore(e.target.value)} placeholder="/api/internal" />
        </label>
        <label>فقط این مسیرها ثبت شوند (خالی = همه)
          <textarea className="input ltr" rows={3} style={{ width: "100%" }} value={include} onChange={(e) => setInclude(e.target.value)} placeholder="/api" />
        </label>
      </div>
      {error && <div className="error">{error}</div>}
      <div style={{ display: "flex", gap: 8 }}>
        <button className="btn primary" disabled={busy}>{project ? "ذخیره" : "ساخت پروژه"}</button>
        <button type="button" className="btn" onClick={onClose}>انصراف</button>
      </div>
    </form>
  );
}

function SecretsBox({ s, onClose }: { s: Secrets; onClose: () => void }) {
  return (
    <div className="panel" style={{ marginBottom: 16, borderColor: "var(--warn)" }}>
      <h2>{s.title}</h2>
      <p className="muted" style={{ marginTop: 0 }}>این مقادیر فقط همین یک بار نمایش داده می‌شوند. ServerSecret و ReadKey را فقط در تنظیمات بک‌اند (User Secrets یا متغیر محیطی) بگذارید، هرگز در کد فرانت.</p>
      <table className="t"><tbody>
        {s.items.map(([k, v]) => <tr key={k}><td><span className="mono ltr">{k}</span></td><td><input className="input mono ltr" style={{ width: "100%" }} readOnly value={v} onFocus={(e) => e.target.select()} /></td></tr>)}
      </tbody></table>
      <button className="btn" style={{ marginTop: 12 }} onClick={onClose}>ذخیره کردم، ببند</button>
    </div>
  );
}

function Users({ users, projects, me, run }: { users: User[]; projects: Project[]; me: string; run: (fn: () => Promise<unknown>) => void }) {
  const [form, setForm] = useState<{ editing: User | null; username: string; password: string; all: boolean; picked: string[]; isAdmin: boolean } | null>(null);
  const open = (u: User | null) => setForm({ editing: u, username: u?.username ?? "", password: "", all: u ? u.projects.includes("*") : true,
    picked: u ? u.projects.filter((p) => p !== "*") : [], isAdmin: u?.isAdmin ?? false });

  const save = (e: React.FormEvent) => {
    e.preventDefault();
    if (!form) return;
    const body = { username: form.username, password: form.password || undefined, projects: form.all ? ["*"] : form.picked, isAdmin: form.isAdmin };
    run(async () => {
      if (form.editing) await api(`admin/users/${encodeURIComponent(form.editing.username)}`, undefined, "PUT", body);
      else await api("admin/users", undefined, "POST", body);
      setForm(null);
    });
  };

  return (
    <div className="panel">
      <div style={{ display: "flex", alignItems: "center", marginBottom: 8 }}>
        <h2 style={{ flex: 1, margin: 0 }}>کاربران داشبورد</h2>
        <button className="btn primary" onClick={() => open(null)}>کاربر جدید</button>
      </div>
      {form && (
        <form onSubmit={save} style={{ display: "grid", gap: 10, margin: "12px 0 20px" }}>
          <div className="grid g3">
            <input className="input ltr" placeholder="نام کاربری" value={form.username} disabled={!!form.editing} required onChange={(e) => setForm({ ...form, username: e.target.value })} />
            <input className="input" type="password" autoComplete="new-password" placeholder={form.editing ? "رمز جدید (خالی = بدون تغییر)" : "رمز عبور (حداقل ۱۰ کاراکتر)"}
              value={form.password} required={!form.editing} minLength={10} onChange={(e) => setForm({ ...form, password: e.target.value })} />
            <label><input type="checkbox" checked={form.isAdmin} disabled={form.editing?.username === me} onChange={(e) => setForm({ ...form, isAdmin: e.target.checked })} /> مدیر (دسترسی به این صفحه)</label>
          </div>
          <div>
            <label><input type="checkbox" checked={form.all} onChange={(e) => setForm({ ...form, all: e.target.checked })} /> دسترسی به همه پروژه‌ها</label>
            {!form.all && <div style={{ display: "flex", gap: 14, flexWrap: "wrap", marginTop: 6 }}>
              {projects.map((p) => <label key={p.key}><input type="checkbox" checked={form.picked.includes(p.key)}
                onChange={(e) => setForm({ ...form, picked: e.target.checked ? [...form.picked, p.key] : form.picked.filter((x) => x !== p.key) })} /> {p.name}</label>)}
            </div>}
          </div>
          <div style={{ display: "flex", gap: 8 }}>
            <button className="btn primary">{form.editing ? "ذخیره" : "ساخت کاربر"}</button>
            <button type="button" className="btn" onClick={() => setForm(null)}>انصراف</button>
          </div>
        </form>
      )}
      <table className="t">
        <thead><tr><th>نام کاربری</th><th>پروژه‌ها</th><th>نقش</th><th /></tr></thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.username}>
              <td><span className="ltr">{u.username}</span>{u.fromConfig && <span className="badge" style={{ marginInlineStart: 6 }}>از فایل تنظیمات</span>}</td>
              <td>{u.projects.includes("*") ? "همه" : u.projects.map((k) => projects.find((p) => p.key === k)?.name ?? k).join("، ")}</td>
              <td>{u.isAdmin ? "مدیر" : "بیننده"}</td>
              <td style={{ whiteSpace: "nowrap" }}>{!u.fromConfig && <>
                <button className="btn" onClick={() => open(u)}>ویرایش</button>{" "}
                {u.username !== me && <button className="btn danger" onClick={() => confirm(`کاربر ${u.username} حذف شود؟`) && run(() => api(`admin/users/${encodeURIComponent(u.username)}`, undefined, "DELETE"))}>حذف</button>}
              </>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
