# Tracker

سیستم رهگیری کاربران برای پروژه‌های ASP.NET Core + Next.js. فقط رفتار کاربر را ثبت می‌کند (مانیتورینگ نیست):
هر کاربر چه صفحه‌ای دید، روی چه چیزی کلیک کرد، کدام API را صدا زد، چه رویدادی ساخت، و همه این‌ها در یک تایم‌لاین.
همه‌ی سایت‌هایی که پکیج را نصب کرده‌اند از یک پنل مرکزی (`https://tracker.rhinox.ir`) مدیریت می‌شوند.

ساختار پروژه مثل بیمه‌یار است: بک‌اند ASP.NET Core 10 در `backend`، پنل Next.js 15 در `frontend`.

| بخش | مسیر | کار |
| --- | --- | --- |
| API مرکزی | `backend/Tracker.Api` | دریافت داده از سایت‌ها (`/v1/*`)، API پنل (`/api/*`)، ورود و کاربران، پروژه‌ها |
| پنل | `frontend` | پنل فارسی Next.js 15 + Tailwind 4 (همان سیستم طراحی بیمه‌یار) |
| پکیج بک‌اند | `backend/Tracker.AspNetCore` + `backend/Tracker.Core` | پکیج NuGet برای API سایت‌ها: ثبت خودکار همه endpointها + `Track()` |
| پکیج فرانت | `packages/tracker-next` | پکیج npm برای Next.js: بازدید صفحه، کلیک، فرم، identify، `track()` |
| تست‌ها | `backend/Tracker.Tests` | تست‌های ورود، دسترسی، دریافت داده و پکیج‌ها |
| نصب روی سرور | `deploy/windows` | سرویس‌های ویندوز + IIS + SSL |
| پکیج‌های آماده | `artifacts/` | فایل‌های `.nupkg` و `.tgz` برای نصب مستقیم |

```
مرورگر (@tracker/next) ──┐                       ┌── پنل Next.js (frontend) ── /api ──┐
                          ├─► IIS ── /v1 ──► Tracker.Api ◄─────────────────────────────┘
API (Tracker.AspNetCore) ─┘   HMAC + Rate limit       └── SQL Server (EF Core برای مدیریت، columnstore + SqlBulkCopy برای رویدادها)
```

## لایه‌ی داده

- **EF Core + migration** برای داده‌های مدیریتی: کاربران پنل، نشست‌ها، گزارش امنیتی و پروژه‌ها (`Data/AppDbContext.cs`). migrationها هنگام اجرا خودکار اعمال می‌شوند.
- **Dapper + SqlBulkCopy** برای رویدادهای پرحجم (`tracker.Events`، `Users`، `Aliases`). جدول‌ها و procedureها در `Data/Sql/events.sql` هستند و migration اولیه آن‌ها را می‌سازد.
- migration جدید: از `backend/Tracker.Api`:
  `dotnet tool restore` و بعد `dotnet ef migrations add <Name> -o Data/Migrations`

## ورود حرفه‌ای به پنل

- نام کاربری و رمز، با هش PBKDF2-SHA256 (۲۱۰ هزار دور).
- **نشست سمت سرور** در کوکی `HttpOnly` + `Secure` + `SameSite=Strict` (با پیشوند `__Host-` در production). فقط هش توکن در دیتابیس است.
  هر نشست بعد از ۸ ساعت بی‌کاری یا ۲۴ ساعت تمام می‌شود؛ «مرا به خاطر بسپار» تا ۳۰ روز.
- **قفل حساب** بعد از ۵ تلاش ناموفق (۱۵ دقیقه و هر بار دو برابر)، **مسدود شدن IP** بعد از ۲۰ تلاش ناموفق در ۱۵ دقیقه، و rate limit روی ورود.
- **سیاست رمز:** حداقل ۱۲ کاراکتر، سه نوع کاراکتر (یا ۱۶ کاراکتر به بالا)، بدون نام کاربری و الگوهای رایج.
- **رمز موقت:** هر کاربری که مدیر می‌سازد یا رمزش را ریست می‌کند، در اولین ورود باید رمزش را عوض کند؛ تا آن موقع هیچ API دیگری جواب نمی‌دهد.
- **حساب من:** تغییر رمز (بقیه‌ی دستگاه‌ها خارج می‌شوند)، لیست دستگاه‌های واردشده و خروج از هر کدام.
- **نقش‌ها:** بیننده (فقط پروژه‌های خودش)، مدیر (همه‌ی پروژه‌ها، مدیریت پروژه‌ها و بیننده‌ها)، مالک (همه‌کاره؛ فقط او مدیر و مالک می‌سازد). همیشه حداقل یک مالک فعال می‌ماند.
- **گزارش امنیتی:** همه‌ی ورودها، تلاش‌های ناموفق، تغییر رمزها و تغییرات مدیریتی با زمان و IP.
- **محافظ CSRF:** هر درخواست تغییردهنده باید هدر `X-Requested-With: fetch` داشته باشد؛ امکان محدود کردن پنل به IPهای خاص با `Auth:AllowedIps`.

## اجرای محلی

۱. `backend/Tracker.Api/appsettings.Development.example.json` را به `appsettings.Development.json` کپی و پر کنید (رشته‌ی اتصال و مالک اولیه). این فایل در git نیست.

۲. API (دیتابیس و جدول‌ها در اولین اجرا ساخته می‌شوند، و مالک اولیه از `Bootstrap` ساخته می‌شود):

```bash
cd backend/Tracker.Api
dotnet run --launch-profile http        # http://localhost:5080
```

۳. پنل:

```bash
cd frontend
npm install
npm run dev                              # http://localhost:3100
```

بعد از اولین ورود رمز را عوض کنید و `Bootstrap:OwnerPassword` را از تنظیمات پاک کنید.
بدون Bootstrap هم می‌شود مالک ساخت: `dotnet Tracker.Api.dll create-owner <username>` (و `reset-password <username>` برای فراموشی رمز).

## انتشار روی سرور ویندوز (tracker.rhinox.ir)

```powershell
sqlcmd -S . -i deploy\windows\create-database.sql          # فقط اگر دیتابیس ندارید؛ اول رمز را عوض کنید
dotnet publish backend\Tracker.Api -c Release -r win-x64 --self-contained false -o C:\temp\tracker\api
# appsettings.Production.json را از deploy\windows\appsettings.Production.example.json بسازید و کنار فایل‌های API بگذارید
cd frontend; $env:API_INTERNAL_URL="http://127.0.0.1:5086"; npm ci; npm run build; cd ..
.\deploy\windows\install.ps1 -Api C:\temp\tracker\api -Frontend .\frontend -Domain tracker.rhinox.ir -WinSW C:\tools\WinSW-x64.exe -Email you@example.com
```

- دو سرویس ویندوز نصب می‌شود: `tracker-api` (روی `127.0.0.1:5086`) و `tracker-web` (Next.js روی `127.0.0.1:3100`).
- IIS مسیرهای `/v1` و `/health` را مستقیم به API می‌فرستد و بقیه را به پنل (`deploy/windows/web.config`).
- `API_INTERNAL_URL` در زمان build در Next.js ثبت می‌شود؛ اگر پورت API را عوض کردید دوباره build بگیرید.

### ارتقا از نسخه‌ی ۱ (Tracker.Collector)

- همان دیتابیس را بدهید. migration اولیه جدول `tracker.Projects` و جدول‌های رویداد موجود را نگه می‌دارد و فقط جدول‌های جدید را می‌سازد؛ پروژه‌ها، کلیدها و داده‌ها دست نمی‌خورند.
  پوشه‌ی کلیدهای Data Protection نسخه‌ی قبل را هم نگه دارید (`Tracker:DataProtectionKeysPath`)، وگرنه کلیدهای سرور پروژه‌ها باز نمی‌شوند.
- کاربران `tracker.DashboardUsers` و `Tracker:DashboardUsers` با همان رمزها منتقل می‌شوند؛ اولین مدیر، مالک می‌شود.
- `ConnectionStrings:TrackerWriter` و `TrackerReader` هنوز کار می‌کنند، ولی اتصال اصلی حالا migration هم اجرا می‌کند و باید مالک schema باشد (`db_owner`).
- `install.ps1` سرویس قدیمی `tracker-collector` را متوقف و غیرفعال می‌کند. آدرس سایت‌ها (`CollectorUrl`) و کلیدهایشان عوض نمی‌شود.

## مدیریت مرکزی (چند سایت، یک پنل)

از منوی **مدیریت ← پروژه‌ها** (مدیرها):

- **پروژه‌ی جدید:** کلید (مثلاً `werty`)، نام و Originهای مجاز. `ServerSecret` و `ReadKey` فقط همان لحظه یک بار نمایش داده می‌شوند، همراه با تنظیمات آماده‌ی بک‌اند و Next.js.
  کلید سرور رمزنگاری‌شده (ASP.NET Data Protection) و از کلید خواندن فقط هش ذخیره می‌شود.
- **تنظیمات از راه دور:** روشن/خاموش کردن ثبت، درصد نمونه‌گیری API و بازدیدکننده، ثبت خودکار API و کلیک، ذخیره‌ی بدنه‌ی درخواست،
  مسیرهای نادیده یا فقط‌این‌مسیرها. هر نصب (بک‌اند هر دقیقه، مرورگر در هر بار باز شدن سایت) این‌ها را می‌خواند.
- **کلید جدید:** اگر کلیدی لو رفت، از همان‌جا کلید جدید بسازید؛ کلید قبلی فوراً از کار می‌افتد.
- **غیرفعال کردن پروژه:** هیچ داده‌ای از آن پذیرفته نمی‌شود (داده‌های قبلی می‌مانند).
- **کاربران پنل:** ساخت کاربر با رمز موقت، نقش، پروژه‌هایی که می‌بیند، غیرفعال کردن، باز کردن قفل، خروج اجباری از همه‌ی دستگاه‌ها.

## ۲. اضافه کردن به هر پروژه بک‌اند

```bash
# یک بار: فید محلی
dotnet nuget add source D:\track\Tracker\artifacts\nuget -n tracker
dotnet add package Tracker.AspNetCore
```

```csharp
// Program.cs
builder.Services.AddTracker(builder.Configuration);
...
app.UseRouting();
app.UseTracker();          // بعد از UseRouting، قبل از Map ها
```

```json
// appsettings.json  (ServerSecret را در User Secrets یا متغیر محیطی بگذارید)
"Tracker": { "ProjectKey": "shop", "CollectorUrl": "https://tracker.rhinox.ir", "ServerSecret": "..." }
```

همین. از این به بعد هر درخواست به API با کاربر (از Claim های `sub` / `NameIdentifier` / `userId`)، مسیر، نتیجه، IP و دستگاه ثبت می‌شود.

```csharp
[TrackAs("ثبت سفارش")]                 // اسم خوانا در داشبورد
[HttpPost("api/orders")] public IActionResult Create(...) { ... }

[NoTrack]                               // این اکشن ثبت نشود
[HttpGet("api/internal/ping")] ...

app.MapPost("/api/pay", ...).TrackAs("پرداخت");   // Minimal API

public class OrderService(ITracker tracker)
{
    public void Paid(Order o) => tracker.Track("OrderPaid", new { o.Id, o.Amount });
}
```

تنظیمات مهم `TrackerOptions`: `Enabled`، `SampleRate`، `IncludePathPrefixes` (مثلاً `["/api"]`)، `IgnorePathPrefixes`،
`CaptureRequestBody` (پیش‌فرض خاموش)، `AnonymizeIp`، `SensitiveKeys`.

## ۳. اضافه کردن به هر پروژه Next.js

```bash
npm i D:/track/Tracker/artifacts/npm/tracker-next-1.1.0.tgz
```

```tsx
// app/layout.tsx  (یا pages/_app.tsx)
import { TrackerProvider } from "@tracker/next";

<TrackerProvider projectKey="shop" collectorUrl="https://tracker.rhinox.ir"
                linkApiOrigins={["https://api.shop.ir"]}>
  {children}
</TrackerProvider>
```

```ts
import { tracker } from "@tracker/next";
tracker.identify(user.id, { name: user.fullName });   // بعد از لاگین
tracker.track("AddToCart", { productId: 12 });
tracker.reset();                                      // بعد از خروج
axios.get(url, { headers: tracker.headers() });       // اگر axios دارید (fetch خودکار است)
```

- `data-tracker="اسم"` روی هر المان، اسم کلیک را مشخص می‌کند. `data-tracker-ignore` یا کلاس `tracker-ignore` آن بخش را ثبت نمی‌کند.
- مقدار input ها هرگز ارسال نمی‌شود. فیلدهای password کاملاً نادیده گرفته می‌شوند.
- برای اینکه درخواست‌های API به همان بازدیدکننده وصل شوند، در CORS بک‌اند هدرهای
  `X-Tracker-Session` و `X-Tracker-Anon` را مجاز کنید:
  `policy.WithHeaders("Content-Type", "Authorization", "X-Tracker-Session", "X-Tracker-Anon")`.

## نمایش داده‌ها داخل خود پروژه (مثلاً پنل ادمین ورتی)

بک‌اند پروژه (کلید خواندن فقط در سرور می‌ماند و احراز هویت خود پروژه از آن محافظت می‌کند):

```json
"Tracker": { "ProjectKey": "verti", "CollectorUrl": "https://tracker.rhinox.ir", "ServerSecret": "...", "ReadKey": "rk_..." }
```

```csharp
app.MapTrackerInsights("/api/admin/tracker", "AdminOnly");   // اسم Policy احراز هویت خود پروژه؛ اجباری است
```

فرانت پروژه:

```tsx
import { TrackerUserActivity, TrackerOverview, TrackerRecentEvents } from "@tracker/next/insights";

<TrackerOverview baseUrl="/api/admin/tracker" days={30} />
<TrackerUserActivity baseUrl="/api/admin/tracker" userId={user.id}
                    headers={{ Authorization: `Bearer ${token}` }} />   // اگر API با JWT است
```

برای UI دلخواه: `useTrackerInsights(source, "users" | "user" | "events" | "overview" | "session", params)`.
API مستقیم Collector هم در دسترس است: `GET /v1/read/{overview|events|users|user|session}` با هدر `Authorization: Bearer rk_...`.

## عملکرد

- در مسیر درخواست فقط یک رکورد در صف حافظه نوشته می‌شود (بدون I/O). صف پر شود، رکورد دور ریخته می‌شود و برنامه هرگز منتظر نمی‌ماند.
- ارسال دسته‌ای (۵۰۰ تایی یا هر ۲ ثانیه)، فشرده (gzip) و در پس‌زمینه. Collector پایین باشد، برنامه شما هیچ خطایی نمی‌بیند.
- در مرورگر ارسال با `sendBeacon` / `fetch keepalive` در زمان بیکاری مرورگر؛ بدون وابستگی.

## امنیت

- ورود داده از مرورگر فقط از Originهای مجاز هر پروژه، با Rate limit و سقف حجم. مرورگر نمی‌تواند رویداد API جعل کند.
- ورود داده از سرور با امضای HMAC-SHA256 روی هر بسته و محدودیت زمانی ۵ دقیقه.
- فقط کوئری پارامتری (Dapper، SqlBulkCopy و EF Core).
- پنل: بخش «ورود حرفه‌ای» بالا، به‌علاوه‌ی `X-Frame-Options: DENY`، `nosniff`، `no-store` روی همه‌ی پاسخ‌های API و noindex.
- ماسک خودکار password، token، کارت، CVV، کد ملی، شبا و هدر Authorization در SDK و دوباره در API.
- حذف کامل داده‌ی یک کاربر از پنل (مدیرها) یا `EXEC tracker.ForgetUser`.

## توسعه

```bash
dotnet test Tracker.slnx                              # تست‌های API، ورود و پکیج‌ها (SQLite در حافظه، SQL Server لازم نیست)
cd frontend && npm run lint && npm run build
cd packages/tracker-next && npm run build && node test/browser.e2e.mjs
```

ذخیره‌سازی رویدادها از طریق `IEventStore` است؛ برای حجم خیلی بالا می‌شود پیاده‌سازی ClickHouse اضافه کرد بدون تغییر در SDKها.
