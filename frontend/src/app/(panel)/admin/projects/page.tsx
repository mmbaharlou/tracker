"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { api, del, post, put } from "@/lib/api";
import { useSession } from "@/components/session";
import { Badge, Button, ErrorBox, Field, inputCls, Modal, Notice, PageTitle, SecretBox, Select, SuccessBox, Table, Toggle, useAction, useData } from "@/components/ui";

interface Settings {
  enabled: boolean; sampleRate?: number | null; webSampleRate?: number | null; trackApiCalls?: boolean | null;
  autocapture?: boolean | null; captureRequestBody?: boolean | null; captureQueryString?: boolean | null;
  ignorePathPrefixes?: string[] | null; includePathPrefixes?: string[] | null;
}
interface Project { key: string; name: string; allowedOrigins: string[]; settings: Settings; isEnabled: boolean; fromConfig: boolean; hasServerSecret: boolean; hasReadKey: boolean }
interface Secrets { title: string; projectKey: string; serverSecret?: string; readKey?: string }

const pct = (v?: number | null) => (v == null ? "۱۰۰٪" : `${Math.round(v * 100).toLocaleString("fa-IR")}٪`);
const lines = (s: string) => s.split(/\r?\n/).map((x) => x.trim()).filter(Boolean);
const tri = (v: boolean | null | undefined) => (v == null ? "" : v ? "on" : "off");
const fromTri = (v: string) => (v === "" ? null : v === "on");

export default function ProjectsPage() {
  const { isOwner, reloadMe } = useSession();
  const list = useData<Project[]>("/api/admin/projects");
  const act = useAction();
  const [editing, setEditing] = useState<Project | "new" | null>(null);
  const [secrets, setSecrets] = useState<Secrets | null>(null);

  const refresh = async () => { await list.reload(true); await reloadMe(); };

  const rotate = (p: Project, what: "server" | "read") => {
    const label = what === "server" ? "کلید سرور" : "کلید فقط‌خواندنی";
    if (!confirm(`${label} پروژه‌ی «${p.name}» عوض شود؟ نصب‌هایی که کلید قبلی را دارند تا وقتی کلید جدید را بگذارید کار نمی‌کنند.`)) return;
    act.run(async () => {
      const r = await post<{ value: string }>(`/api/admin/projects/${encodeURIComponent(p.key)}/rotate`, {}, { what });
      setSecrets({ title: `${label} جدید «${p.name}»`, projectKey: p.key, ...(what === "server" ? { serverSecret: r.value } : { readKey: r.value }) });
    });
  };

  return (
    <>
      <PageTitle title="پروژه‌ها (سایت‌ها)" sub="هر سایتی که پکیج Tracker را نصب کرده، یک پروژه است. تغییرات تنظیمات ظرف یک دقیقه روی همه‌ی نصب‌ها اعمال می‌شود.">
        <Button variant="primary" onClick={() => setEditing("new")}><Plus className="size-4" /> پروژه‌ی جدید</Button>
      </PageTitle>
      <ErrorBox error={act.error ?? list.error} />
      <SuccessBox message={act.message} />
      <Table head={["نام", "کلید", "وضعیت", "Originهای مجاز", "نمونه‌گیری", ""]}>
        {(list.data ?? []).map((p) => (
          <tr key={p.key}>
            <td className="font-medium">{p.name}{p.fromConfig && <Badge className="ms-2">از فایل تنظیمات</Badge>}</td>
            <td className="ltr text-start font-mono text-xs">{p.key}</td>
            <td>{!p.isEnabled ? <Badge tone="red">غیرفعال</Badge> : p.settings.enabled === false ? <Badge tone="amber">ثبت خاموش</Badge> : <Badge tone="green">فعال</Badge>}</td>
            <td className="text-xs">{p.allowedOrigins.length ? p.allowedOrigins.map((o) => <div key={o} className="ltr text-start font-mono">{o}</div>) : <span className="text-danger">همه (توصیه نمی‌شود)</span>}</td>
            <td className="text-xs"><div>API: {pct(p.settings.sampleRate)}</div><div>مرورگر: {pct(p.settings.webSampleRate)}</div></td>
            <td className="whitespace-nowrap text-end">
              {!p.fromConfig && (
                <div className="flex justify-end gap-1">
                  <Button size="sm" variant="ghost" onClick={() => setEditing(p)}>ویرایش</Button>
                  <Button size="sm" variant="ghost" onClick={() => rotate(p, "server")}>کلید سرور جدید</Button>
                  <Button size="sm" variant="ghost" onClick={() => rotate(p, "read")}>کلید خواندن جدید</Button>
                  {isOwner && (
                    <Button size="sm" variant="ghost" className="text-danger" onClick={() => {
                      if (confirm(`پروژه‌ی «${p.name}» حذف شود؟ داده‌های ثبت‌شده می‌مانند ولی داده‌ی جدیدی پذیرفته نمی‌شود.`))
                        act.run(async () => { await del(`/api/admin/projects/${encodeURIComponent(p.key)}`); await refresh(); }, "حذف شد.");
                    }}>حذف</Button>
                  )}
                </div>
              )}
            </td>
          </tr>
        ))}
      </Table>
      {list.data?.length === 0 && <p className="mt-4 text-center text-sm text-ink-3">هنوز پروژه‌ای نیست. با «پروژه‌ی جدید» اولین سایت را اضافه کنید.</p>}

      {editing && (
        <ProjectDialog project={editing === "new" ? null : editing} onClose={() => setEditing(null)}
          onSaved={async (s) => { setEditing(null); if (s) setSecrets(s); await refresh(); }} />
      )}
      {secrets && <SecretsDialog s={secrets} onClose={() => setSecrets(null)} />}
    </>
  );
}

function ProjectDialog({ project, onClose, onSaved }: { project: Project | null; onClose: () => void; onSaved: (s: Secrets | null) => void }) {
  const s = project?.settings ?? { enabled: true };
  const act = useAction();
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

  const save = (e: React.FormEvent) => {
    e.preventDefault();
    const settings: Settings = {
      enabled,
      sampleRate: sample === "" ? null : Number(sample) / 100,
      webSampleRate: webSample === "" ? null : Number(webSample) / 100,
      trackApiCalls: fromTri(trackApi), autocapture: fromTri(autocapture), captureRequestBody: fromTri(body),
      ignorePathPrefixes: lines(ignore).length ? lines(ignore) : null,
      includePathPrefixes: lines(include).length ? lines(include) : null,
    };
    const input = { key, name, allowedOrigins: lines(origins), settings, isEnabled };
    act.run(async () => {
      if (project) { await put(`/api/admin/projects/${encodeURIComponent(project.key)}`, input); onSaved(null); }
      else {
        const r = await api<{ key: string; serverSecret: string; readKey: string }>("/api/admin/projects", { method: "POST", json: input });
        onSaved({ title: `کلیدهای پروژه‌ی «${name || r.key}»`, projectKey: r.key, serverSecret: r.serverSecret, readKey: r.readKey });
      }
    });
  };

  const triSelect = (label: string, v: string, set: (v: string) => void) => (
    <Field label={label}>
      <Select className="w-full" value={v} onChange={set}>
        <option value="">طبق تنظیمات خود نصب</option><option value="on">روشن</option><option value="off">خاموش</option>
      </Select>
    </Field>
  );

  return (
    <Modal open wide onClose={onClose} title={project ? `ویرایش «${project.name}»` : "پروژه‌ی جدید"}>
      <form onSubmit={save} className="space-y-5">
        <ErrorBox error={act.error} />
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="کلید پروژه" hint="انگلیسی کوچک، مثلاً werty. بعداً عوض نمی‌شود.">
            <input className={`${inputCls} ltr`} value={key} onChange={(e) => setKey(e.target.value)} disabled={!!project} required pattern="[a-z0-9][a-z0-9\-]{1,39}" />
          </Field>
          <Field label="نام نمایشی"><input className={inputCls} value={name} onChange={(e) => setName(e.target.value)} placeholder="فروشگاه ورتی" /></Field>
          <Field label="Originهای مجاز مرورگر" hint="هر خط یکی. فقط همین سایت‌ها می‌توانند رویداد مرورگر بفرستند.">
            <textarea className={`${inputCls} ltr`} rows={2} value={origins} onChange={(e) => setOrigins(e.target.value)} placeholder="https://wertyshop.ir" />
          </Field>
        </div>
        <div className="grid gap-2 md:grid-cols-2">
          <Toggle checked={isEnabled} onChange={setIsEnabled} label="پروژه فعال باشد" hint="غیرفعال: هیچ داده‌ای از این سایت پذیرفته نمی‌شود." />
          <Toggle checked={enabled} onChange={setEnabled} label="ثبت رویداد در همه‌ی نصب‌ها روشن باشد" hint="کلید خاموش/روشن از راه دور، بدون انتشار دوباره‌ی سایت." />
        </div>
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="درصد ثبت درخواست‌های API" hint="خالی = ۱۰۰"><input className={inputCls} type="number" min={0} max={100} step="any" value={sample} onChange={(e) => setSample(e.target.value)} /></Field>
          <Field label="درصد بازدیدکنندگانِ ردیابی‌شده" hint="خالی = ۱۰۰"><input className={inputCls} type="number" min={0} max={100} step="any" value={webSample} onChange={(e) => setWebSample(e.target.value)} /></Field>
          <div />
          {triSelect("ثبت خودکار APIها", trackApi, setTrackApi)}
          {triSelect("ثبت خودکار کلیک و فرم", autocapture, setAutocapture)}
          {triSelect("ذخیره‌ی بدنه‌ی درخواست API", body, setBody)}
          <Field label="مسیرهایی که ثبت نشوند" hint="هر خط یکی">
            <textarea className={`${inputCls} ltr`} rows={3} value={ignore} onChange={(e) => setIgnore(e.target.value)} placeholder="/api/internal" />
          </Field>
          <Field label="فقط این مسیرها ثبت شوند" hint="خالی = همه">
            <textarea className={`${inputCls} ltr`} rows={3} value={include} onChange={(e) => setInclude(e.target.value)} placeholder="/api" />
          </Field>
        </div>
        <div className="flex justify-end gap-2">
          <Button type="button" onClick={onClose}>انصراف</Button>
          <Button variant="primary" type="submit" busy={act.busy}>{project ? "ذخیره" : "ساخت پروژه"}</Button>
        </div>
      </form>
    </Modal>
  );
}

function SecretsDialog({ s, onClose }: { s: Secrets; onClose: () => void }) {
  const origin = typeof window === "undefined" ? "https://tracker.rhinox.ir" : window.location.origin;
  const backend = JSON.stringify({ Tracker: { ProjectKey: s.projectKey, CollectorUrl: origin, ...(s.serverSecret ? { ServerSecret: s.serverSecret } : {}), ...(s.readKey ? { ReadKey: s.readKey } : {}) } }, null, 2);
  return (
    <Modal open wide onClose={onClose} title={s.title}>
      <Notice tone="amber">این مقادیر <b>فقط همین یک بار</b> نمایش داده می‌شوند. ServerSecret و ReadKey را فقط در تنظیمات بک‌اند (User Secrets یا متغیر محیطی) بگذارید، هرگز در کد فرانت.</Notice>
      <SecretBox label="Tracker:ProjectKey" value={s.projectKey} />
      {s.serverSecret && <SecretBox label="Tracker:ServerSecret" value={s.serverSecret} />}
      {s.readKey && <SecretBox label="Tracker:ReadKey" value={s.readKey} />}
      <h3 className="mb-1 mt-5 text-sm font-bold">appsettings بک‌اند سایت</h3>
      <pre className="ltr overflow-x-auto rounded-2xl bg-surface-2 p-4 text-xs">{backend}</pre>
      <h3 className="mb-1 mt-5 text-sm font-bold">Next.js (app/layout.tsx)</h3>
      <pre className="ltr overflow-x-auto rounded-2xl bg-surface-2 p-4 text-xs">{`<TrackerProvider projectKey="${s.projectKey}" collectorUrl="${origin}">\n  {children}\n</TrackerProvider>`}</pre>
      <div className="mt-5 flex justify-end"><Button variant="primary" onClick={onClose}>ذخیره کردم، ببند</Button></div>
    </Modal>
  );
}
