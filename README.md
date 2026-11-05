# Tracker

سیستم رهگیری کاربران برای پروژه‌های ASP.NET Core + Next.js. فقط رفتار کاربر را ثبت می‌کند (مانیتورینگ نیست):
هر کاربر چه صفحه‌ای دید، روی چه چیزی کلیک کرد، کدام API را صدا زد، چه رویدادی ساخت، و همه این‌ها در یک تایم‌لاین.

| بخش | مسیر | کار |
| --- | --- | --- |
| `Tracker.AspNetCore` | `src/Tracker.AspNetCore` | پکیج NuGet برای API شما: ثبت خودکار همه endpoint ها + `Track()` |
| `@tracker/next` | `packages/tracker-next` | پکیج npm برای Next.js: بازدید صفحه، کلیک، فرم، identify، `track()` |
| `Tracker.Collector` | `src/Tracker.Collector` | سرویس دریافت داده + API و صفحات داشبورد (یک سایت IIS) |
| داشبورد | `dashboard` | سورس داشبورد فارسی (Next.js، خروجی استاتیک داخل Collector) |
| دیتابیس | `db/schema.sql` | جداول SQL Server |
| پکیج‌های آماده | `artifacts/` | فایل‌های `.nupkg` و `.tgz` برای نصب مستقیم |

```
مرورگر (@tracker/next) ──┐                      ┌── داشبورد فارسی (کاربران، تایم‌لاین، رویدادها)
                          ├─► Tracker.Collector ─┤
API (Tracker.AspNetCore) ─┘   HMAC + Rate limit   └── SQL Server (columnstore)
```

- **نسخه‌ی در حال اجرا:** `https://tracker.rhinox.ir` (روی سرور ویندوز، پشت IIS، با SSL رایگان win-acme). اولین پروژه‌ی وصل‌شده: دزمو (`desmo`).
- **پیش‌نیاز:** .NET 8 به بالا (روی سرورهایی که فقط .NET 10 دارند با `DOTNET_ROLL_FORWARD=Major` اجرا می‌شود)، SQL Server 2016 به بالا، Node 20 به بالا فقط برای بیلد داشبورد.

## ۱. راه‌اندازی یک‌باره (Collector + دیتابیس)

1. یک دیتابیس خالی بسازید و `db/schema.sql` را روی آن اجرا کنید:
   ```sql
   CREATE DATABASE Tracker;
   ```
2. دو لاگین با کمترین دسترسی بسازید (نمونه در انتهای `schema.sql`):
   `tracker_collector` عضو `tracker_writer` و `tracker_dashboard` عضو `tracker_reader`.
3. برای هر پروژه یک کلید سرور و برای خودتان رمز داشبورد بسازید:
   ```bash
   cd src/Tracker.Collector
   dotnet run -- new-secret      # کلید سرور (۶۴ کاراکتر)
   dotnet run -- hash-password   # هش رمز داشبورد (حداقل ۱۰ کاراکتر)
   ```
4. فایل `src/Tracker.Collector/appsettings.Local.json` بسازید (در git نمی‌رود):
   ```json
   {
     "ConnectionStrings": {
       "TrackerWriter": "Server=.;Database=Tracker;User Id=tracker_collector;Password=...;TrustServerCertificate=True",
       "TrackerReader": "Server=.;Database=Tracker;User Id=tracker_dashboard;Password=...;TrustServerCertificate=True"
     },
     "Tracker": {
       "Projects": [
         { "Key": "shop", "Name": "فروشگاه", "ServerSecret": "<new-secret>", "AllowedOrigins": [ "https://shop.ir" ] }
       ],
       "DashboardUsers": [
         { "Username": "mehdi", "PasswordHash": "<hash-password>", "Projects": [ "*" ], "IsAdmin": true }
       ]
     }
   }
   ```
5. منتشر و روی IIS (یا هر جای دیگر) با HTTPS اجرا کنید:
   ```bash
   dotnet publish src/Tracker.Collector -c Release -o publish/collector
   ```
   داشبورد روی همان آدرس باز می‌شود، مثلاً `https://track.mydomain.ir`.
   اگر پشت IIS ARR / nginx / Cloudflare است، `TrustForwardedHeaders` و `KnownProxies` را تنظیم کنید.

### نصب روی سرور ویندوز به‌صورت سرویس (همان روشی که tracker.rhinox.ir نصب شده)

1. Publish: `dotnet publish src/Tracker.Collector -c Release -r win-x64 --self-contained false -p:RollForward=Major -o C:\apps\tracker`
2. `appsettings.Local.json` را کنار فایل‌ها بگذارید (ConnectionStringها با `Server=127.0.0.1`، و `"TrustForwardedHeaders": true, "KnownProxies": ["127.0.0.1", "::1"]`).
3. سرویس ویندوز با [WinSW](https://github.com/winsw/winsw): فایل `tracker-collector.xml` کنار exe:
   ```xml
   <service>
     <id>tracker-collector</id><name>Tracker Collector</name>
     <executable>C:\apps\tracker\Tracker.Collector.exe</executable>
     <env name="ASPNETCORE_URLS" value="http://127.0.0.1:5085"/>
     <env name="DOTNET_gcServer" value="0"/><env name="DOTNET_GCConserveMemory" value="7"/>
     <startmode>Automatic</startmode><onfailure action="restart" delay="10 sec"/>
     <logpath>C:\apps\tracker\logs</logpath>
   </service>
   ```
   سپس `tracker-collector.exe install` و `Start-Service tracker-collector`.
4. سایت IIS برای دامنه با URL Rewrite + ARR که همه‌ی درخواست‌ها را به `http://127.0.0.1:5085` بفرستد (و `HTTP_X_FORWARDED_PROTO` = `https`)، و گرفتن SSL با win-acme.

> **نکته:** کاربر `tracker_writer` علاوه بر `INSERT` باید روی `tracker.Events` دسترسی `SELECT` هم داشته باشد، چون `SqlBulkCopy` قبل از نوشتن ساختار جدول را می‌خواند. `db/schema.sql` این دسترسی را می‌دهد. اگر دیتابیس را با نسخه‌ی قدیمی‌تر ساخته‌اید، این را اجرا کنید:
> `GRANT SELECT ON tracker.Events TO tracker_writer;`

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
"Tracker": { "ProjectKey": "shop", "CollectorUrl": "https://track.mydomain.ir", "ServerSecret": "..." }
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

<TrackerProvider projectKey="shop" collectorUrl="https://track.mydomain.ir"
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

## مدیریت مرکزی (چند پروژه، یک داشبورد)

یک Collector مرکزی (مثلاً `https://tracker.rhinox.ir`) همه پروژه‌ها را نگه می‌دارد. از منوی **مدیریت** در داشبورد (فقط مدیرها):

- **پروژه جدید:** کلید (مثلاً `verti`)، نام و Origin های مجاز را وارد کنید. `ServerSecret` و `ReadKey` فقط همان لحظه یک بار نمایش داده می‌شوند.
  کلید سرور رمزنگاری‌شده (ASP.NET Data Protection) و از کلید خواندن فقط هش ذخیره می‌شود.
- **تنظیمات از راه دور:** روشن/خاموش کردن ثبت، درصد نمونه‌گیری API و بازدیدکننده، ثبت خودکار API و کلیک، ذخیره بدنه درخواست،
  مسیرهای نادیده یا فقط‌این‌مسیرها. هر نصب (بک‌اند هر دقیقه، مرورگر در هر بار باز شدن سایت) این‌ها را می‌خواند؛ نیازی به انتشار دوباره نیست.
- **کلید جدید:** اگر کلیدی لو رفت، از همان‌جا کلید جدید بسازید؛ کلید قبلی فوراً از کار می‌افتد.
- **غیرفعال کردن پروژه:** هیچ داده‌ای از آن پذیرفته نمی‌شود (داده‌های قبلی می‌مانند).
- **کاربران داشبورد:** ساخت کاربر، تعیین پروژه‌هایی که می‌بیند و نقش مدیر. حذف کاربر دسترسی او را فوراً قطع می‌کند.

پروژه‌ها و کاربرانی که در `appsettings` تعریف شده‌اند هم کار می‌کنند و در این صفحه «از فایل تنظیمات» و فقط‌خواندنی نمایش داده می‌شوند.
پوشه کلیدهای Data Protection (پیش‌فرض `App_Data/keys`، قابل تغییر با `Tracker:DataProtectionKeysPath`) را خصوصی نگه دارید و بکاپ بگیرید؛
روی ویندوز با DPAPI ماشین هم رمز می‌شود.

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

- ورود داده از مرورگر فقط از Origin های مجاز هر پروژه، با Rate limit و سقف حجم. مرورگر نمی‌تواند رویداد API جعل کند.
- ورود داده از سرور با امضای HMAC-SHA256 روی هر بسته و محدودیت زمانی ۵ دقیقه.
- فقط کوئری پارامتری (Dapper و SqlBulkCopy)؛ کاربرهای SQL جدا برای نوشتن و خواندن.
- داشبورد: رمز PBKDF2، کوکی HttpOnly + Secure + SameSite=Strict، قفل بعد از ۵ تلاش ناموفق، محافظ CSRF،
  CSP سخت‌گیرانه با هش اسکریپت‌ها، `X-Frame-Options: DENY`، امکان محدود کردن داشبورد به IP های خاص (`DashboardAllowedIps`).
- ماسک خودکار password، token، کارت، CVV، کد ملی، شبا و هدر Authorization در SDK و دوباره در Collector.
- SDK هیچ endpoint یا پورتی در پروژه شما باز نمی‌کند و هیچ Exception ای به برنامه شما نمی‌دهد.
- حذف کامل داده یک کاربر از داشبورد (ادمین) یا `EXEC tracker.ForgetUser`.
- نکته: `userId` ای که مرورگر با `identify` می‌فرستد از سمت کاربر قابل تغییر است (مثل همه ابزارهای مشابه)؛
  رویدادهای API که از سمت سرور و از روی Claim ها ثبت می‌شوند قابل جعل نیستند.

## توسعه

```bash
dotnet test                                   # تست‌های بک‌اند و Collector
cd packages/tracker-next && npm run build && node test/browser.e2e.mjs   # تست مرورگر (Playwright)
cd dashboard && npm run dev                   # داشبورد روی 3100، API از Collector روی 5080
cd dashboard && npm run build && npm run publish-to-collector
```

ذخیره‌سازی از طریق `IEventStore` است؛ برای حجم خیلی بالا می‌شود پیاده‌سازی ClickHouse اضافه کرد بدون تغییر در SDK ها.
