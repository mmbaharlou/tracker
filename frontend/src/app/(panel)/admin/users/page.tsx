"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { del, post, put } from "@/lib/api";
import { ago, fullDate } from "@/lib/format";
import { Badge, Button, ErrorBox, Field, inputCls, Modal, Notice, PageTitle, SuccessBox, Table, Toggle, useAction, useData } from "@/components/ui";
import { roleLabel, useSession, type ProjectRef, type Role } from "@/components/session";

interface UserRow {
  id: number; username: string; displayName?: string; role: Role; allProjects: boolean; projects: string[];
  isActive: boolean; mustChangePassword: boolean; locked: boolean; lockedUntil?: string; failedLogins: number;
  lastLoginAt?: string; lastLoginIp?: string; createdAt: string; activeSessions: number;
}

export default function UsersPage() {
  const { me, isOwner } = useSession();
  const list = useData<{ users: UserRow[]; projects: ProjectRef[] }>("/api/admin/users");
  const act = useAction();
  const [edit, setEdit] = useState<UserRow | "new" | null>(null);
  const [reset, setReset] = useState<UserRow | null>(null);

  const canManage = (u: UserRow) => isOwner || u.role === "Viewer" || u.id === me.id;
  const projectNames = (keys: string[]) => keys.map((k) => list.data?.projects.find((p) => p.key === k)?.name ?? k).join("، ");

  return (
    <>
      <PageTitle title="کاربران پنل" sub="چه کسی وارد این پنل می‌شود و کدام پروژه‌ها را می‌بیند.">
        <Button variant="primary" onClick={() => setEdit("new")}><Plus className="size-4" /> کاربر جدید</Button>
      </PageTitle>
      <Notice>
        <b>بیننده</b> فقط پروژه‌هایی را که به او داده‌اید می‌بیند. <b>مدیر</b> همه‌ی پروژه‌ها را می‌بیند و پروژه‌ها و بیننده‌ها را مدیریت می‌کند.
        <b> مالک</b> همه‌کاره است و تنها کسی است که مدیر و مالک می‌سازد. هر کاربر جدید در اولین ورود باید رمزش را عوض کند.
      </Notice>
      <ErrorBox error={act.error ?? list.error} />
      <SuccessBox message={act.message} />
      <Table head={["کاربر", "نقش", "پروژه‌ها", "آخرین ورود", "وضعیت", ""]}>
        {(list.data?.users ?? []).map((u) => (
          <tr key={u.id}>
            <td>
              <div className="ltr text-start font-medium">{u.username}</div>
              {u.displayName && <div className="text-xs text-ink-3">{u.displayName}</div>}
            </td>
            <td><Badge tone={u.role === "Owner" ? "brand" : u.role === "Admin" ? "blue" : "gray"}>{roleLabel[u.role]}</Badge></td>
            <td className="max-w-64 text-xs text-ink-2">{u.role !== "Viewer" || u.allProjects ? "همه‌ی پروژه‌ها" : u.projects.length ? projectNames(u.projects) : <span className="text-danger">هیچ</span>}</td>
            <td className="text-xs">
              <div title={fullDate(u.lastLoginAt)}>{u.lastLoginAt ? ago(u.lastLoginAt) : "هرگز"}</div>
              {u.lastLoginIp && <div className="ltr text-end text-ink-3">{u.lastLoginIp}</div>}
            </td>
            <td>
              <div className="flex flex-wrap gap-1">
                {!u.isActive && <Badge tone="red">غیرفعال</Badge>}
                {u.locked && <Badge tone="red">قفل تا {fullDate(u.lockedUntil)}</Badge>}
                {u.mustChangePassword && <Badge tone="amber">رمز موقت</Badge>}
                {u.isActive && !u.locked && <Badge tone="green">{u.activeSessions ? `${u.activeSessions.toLocaleString("fa-IR")} نشست فعال` : "فعال"}</Badge>}
              </div>
            </td>
            <td className="whitespace-nowrap text-end">
              {canManage(u) && (
                <div className="flex justify-end gap-1">
                  <Button size="sm" variant="ghost" onClick={() => setEdit(u)}>ویرایش</Button>
                  {u.id !== me.id && <Button size="sm" variant="ghost" onClick={() => setReset(u)}>رمز موقت</Button>}
                  {u.locked && <Button size="sm" variant="ghost" onClick={() => act.run(async () => { await post(`/api/admin/users/${u.id}/unlock`); await list.reload(true); }, "قفل حساب باز شد.")}>باز کردن قفل</Button>}
                  {u.id !== me.id && u.activeSessions > 0 && (
                    <Button size="sm" variant="ghost" onClick={() => act.run(async () => { await post(`/api/admin/users/${u.id}/revoke-sessions`); await list.reload(true); }, `${u.username} از همه‌ی دستگاه‌ها خارج شد.`)}>خروج اجباری</Button>
                  )}
                  {u.id !== me.id && (
                    <Button size="sm" variant="ghost" className="text-danger" onClick={() => {
                      if (confirm(`حساب «${u.username}» حذف شود؟ دسترسی او همین الان قطع می‌شود.`))
                        act.run(async () => { await del(`/api/admin/users/${u.id}`); await list.reload(true); }, "حذف شد.");
                    }}>حذف</Button>
                  )}
                </div>
              )}
            </td>
          </tr>
        ))}
      </Table>

      {edit && list.data && (
        <UserDialog user={edit === "new" ? null : edit} projects={list.data.projects} isOwner={isOwner} isSelf={edit !== "new" && edit.id === me.id}
          onClose={() => setEdit(null)} onSaved={() => { setEdit(null); list.reload(true); }} />
      )}
      {reset && <ResetDialog user={reset} onClose={() => setReset(null)} onDone={() => { setReset(null); list.reload(true); act.setMessage(`رمز موقت ${reset.username} ثبت شد و از همه‌ی دستگاه‌ها خارج شد.`); }} />}
    </>
  );
}

function UserDialog({ user, projects, isOwner, isSelf, onClose, onSaved }:
  { user: UserRow | null; projects: ProjectRef[]; isOwner: boolean; isSelf: boolean; onClose: () => void; onSaved: () => void }) {
  const act = useAction();
  const [username, setUsername] = useState(user?.username ?? "");
  const [displayName, setDisplayName] = useState(user?.displayName ?? "");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState<Role>(user?.role ?? "Viewer");
  const [allProjects, setAll] = useState(user?.allProjects ?? false);
  const [keys, setKeys] = useState<string[]>(user?.projects ?? []);
  const [isActive, setActive] = useState(user?.isActive ?? true);

  const save = (e: React.FormEvent) => {
    e.preventDefault();
    const body = { username, displayName, password, role, allProjects, projects: keys, isActive };
    act.run(async () => { if (user) await put(`/api/admin/users/${user.id}`, body); else await post("/api/admin/users", body); onSaved(); });
  };

  return (
    <Modal open onClose={onClose} title={user ? `ویرایش ${user.username}` : "کاربر جدید"}>
      <form onSubmit={save} className="space-y-4">
        <ErrorBox error={act.error} />
        {!user && (
          <Field label="نام کاربری" hint="حروف انگلیسی، عدد، نقطه، خط تیره یا زیرخط.">
            <input className={inputCls} dir="ltr" required value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" />
          </Field>
        )}
        <Field label="نام نمایشی (اختیاری)">
          <input className={inputCls} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </Field>
        {!user && (
          <Field label="رمز موقت" hint="کاربر در اولین ورود باید آن را عوض کند. حداقل ۱۲ کاراکتر.">
            <input className={inputCls} dir="ltr" type="text" required value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" />
          </Field>
        )}
        <Field label="نقش">
          <select className={inputCls} value={role} onChange={(e) => setRole(e.target.value as Role)} disabled={isSelf}>
            <option value="Viewer">بیننده</option>
            {isOwner && <option value="Admin">مدیر</option>}
            {isOwner && <option value="Owner">مالک</option>}
          </select>
        </Field>
        {role === "Viewer" && (
          <div>
            <Toggle checked={allProjects} onChange={setAll} label="همه‌ی پروژه‌ها" hint="پروژه‌هایی که بعداً اضافه شوند را هم می‌بیند." />
            {!allProjects && (
              <div className="mt-2 max-h-56 space-y-0.5 overflow-y-auto rounded-2xl border border-line p-3">
                {projects.length === 0 && <p className="text-sm text-ink-3">هنوز پروژه‌ای تعریف نشده.</p>}
                {projects.map((p) => (
                  <label key={p.key} className="flex cursor-pointer items-center gap-2 py-1 text-sm">
                    <input type="checkbox" className="size-4 accent-[var(--brand)]" checked={keys.includes(p.key)}
                      onChange={(e) => setKeys(e.target.checked ? [...keys, p.key] : keys.filter((k) => k !== p.key))} />
                    {p.name} <span className="ltr text-xs text-ink-3">{p.key}</span>
                  </label>
                ))}
              </div>
            )}
          </div>
        )}
        {user && !isSelf && <Toggle checked={isActive} onChange={setActive} label="حساب فعال است" hint="با غیرفعال کردن، کاربر همین الان از همه‌ی دستگاه‌ها خارج می‌شود." />}
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" onClick={onClose}>انصراف</Button>
          <Button variant="primary" type="submit" busy={act.busy}>ذخیره</Button>
        </div>
      </form>
    </Modal>
  );
}

function ResetDialog({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const act = useAction();
  const [password, setPassword] = useState("");
  return (
    <Modal open onClose={onClose} title={`رمز موقت برای ${user.username}`}>
      <form onSubmit={(e) => { e.preventDefault(); act.run(async () => { await post(`/api/admin/users/${user.id}/reset-password`, { password }); onDone(); }); }} className="space-y-4">
        <ErrorBox error={act.error} />
        <p className="text-sm leading-7 text-ink-2">کاربر از همه‌ی دستگاه‌ها خارج می‌شود و در ورود بعدی باید این رمز را عوض کند. رمز را از راهی امن به او بدهید.</p>
        <Field label="رمز موقت">
          <input className={inputCls} dir="ltr" required value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" />
        </Field>
        <div className="flex justify-end gap-2">
          <Button type="button" onClick={onClose}>انصراف</Button>
          <Button variant="primary" type="submit" busy={act.busy}>ثبت رمز موقت</Button>
        </div>
      </form>
    </Modal>
  );
}
