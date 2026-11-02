# رهگیر (Rahgir)

سیستم رهگیری کاربران برای پروژه‌های ASP.NET Core + Next.js. فقط رفتار کاربر را ثبت می‌کند (مانیتورینگ نیست):
هر کاربر چه صفحه‌ای دید، روی چه چیزی کلیک کرد، کدام API را صدا زد، چه رویدادی ساخت، و همه این‌ها در یک تایم‌لاین.

| بخش | مسیر | کار |
| --- | --- | --- |
| `Rahgir.AspNetCore` | `src/Rahgir.AspNetCore` | پکیج NuGet برای API شما: ثبت خودکار همه endpoint ها + `Track()` |
| `@rahgir/next` | `packages/rahgir-next` | پکیج npm برای Next.js: بازدید صفحه، کلیک، فرم، identify، `track()` |
| `Rahgir.Collector` | `src/Rahgir.Collector` | سرویس دریافت داده + API و صفحات داشبورد (یک سایت IIS) |
| داشبورد | `dashboard` | سورس داشبورد فارسی (Next.js، خروجی استاتیک داخل Collector) |
| دیتابیس | `db/schema.sql` | جداول SQL Server |
| پکیج‌های آماده | `artifacts/` | فایل‌های `.nupkg` و `.tgz` برای نصب مستقیم |

## ۱. راه‌اندازی یک‌باره (Collector + دیتابیس)

1. یک دیتابیس خالی بسازید و `db/schema.sql` را روی آن اجرا کنید:
   ```sql
   CREATE DATABASE Rahgir;
   ```
2. دو لاگین با کمترین دسترسی بسازید (نمونه در انتهای `schema.sql`):
   `rahgir_collector` عضو `rahgir_writer` و `rahgir_dashboard` عضو `rahgir_reader`.
3. برای هر پروژه یک کلید سرور و برای خودتان رمز داشبورد بسازید:
   ```bash
   cd src/Rahgir.Collector
   dotnet run -- new-secret      # کلید سرور (۶۴ کاراکتر)
   dotnet run -- hash-password   # هش رمز داشبورد (حداقل ۱۰ کاراکتر)
   ```
4. فایل `src/Rahgir.Collector/appsettings.Local.json` بسازید (در git نمی‌رود):
   ```json
   {
     "ConnectionStrings": {
       "RahgirWriter": "Server=.;Database=Rahgir;User Id=rahgir_collector;Password=...;TrustServerCertificate=True",
       "RahgirReader": "Server=.;Database=Rahgir;User Id=rahgir_dashboard;Password=...;TrustServerCertificate=True"
     },
     "Rahgir": {
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
   dotnet publish src/Rahgir.Collector -c Release -o publish/collector
   ```
   داشبورد روی همان آدرس باز می‌شود، مثلاً `https://track.mydomain.ir`.
   اگر پشت IIS ARR / nginx / Cloudflare است، `TrustForwardedHeaders` و `KnownProxies` را تنظیم کنید.

## ۲. اضافه کردن به هر پروژه بک‌اند

```bash
# یک بار: فید محلی
dotnet nuget add source D:\track\Rahgir\artifacts\nuget -n rahgir
dotnet add package Rahgir.AspNetCore
```

```csharp
// Program.cs
builder.Services.AddRahgir(builder.Configuration);
...
app.UseRouting();
app.UseRahgir();          // بعد از UseRouting، قبل از Map ها
```

```json
// appsettings.json  (ServerSecret را در User Secrets یا متغیر محیطی بگذارید)
"Rahgir": { "ProjectKey": "shop", "CollectorUrl": "https://track.mydomain.ir", "ServerSecret": "..." }
```

همین. از این به بعد هر درخواست به API با کاربر (از Claim های `sub` / `NameIdentifier` / `userId`)، مسیر، نتیجه، IP و دستگاه ثبت می‌شود.

```csharp
[TrackAs("ثبت سفارش")]                 // اسم خوانا در داشبورد
[HttpPost("api/orders")] public IActionResult Create(...) { ... }

[NoTrack]                               // این اکشن ثبت نشود
[HttpGet("api/internal/ping")] ...

app.MapPost("/api/pay", ...).TrackAs("پرداخت");   // Minimal API

public class OrderService(IRahgirTracker tracker)
{
    public void Paid(Order o) => tracker.Track("OrderPaid", new { o.Id, o.Amount });
}
```

تنظیمات مهم `RahgirOptions`: `Enabled`، `SampleRate`، `IncludePathPrefixes` (مثلاً `["/api"]`)، `IgnorePathPrefixes`،
`CaptureRequestBody` (پیش‌فرض خاموش)، `AnonymizeIp`، `SensitiveKeys`.

## ۳. اضافه کردن به هر پروژه Next.js

```bash
npm i D:/track/Rahgir/artifacts/npm/rahgir-next-1.0.0.tgz
```

```tsx
// app/layout.tsx  (یا pages/_app.tsx)
import { RahgirProvider } from "@rahgir/next";

<RahgirProvider projectKey="shop" collectorUrl="https://track.mydomain.ir"
                linkApiOrigins={["https://api.shop.ir"]}>
  {children}
</RahgirProvider>
```

```ts
import { rahgir } from "@rahgir/next";
rahgir.identify(user.id, { name: user.fullName });   // بعد از لاگین
rahgir.track("AddToCart", { productId: 12 });
rahgir.reset();                                      // بعد از خروج
axios.get(url, { headers: rahgir.headers() });       // اگر axios دارید (fetch خودکار است)
```

- `data-rahgir="اسم"` روی هر المان، اسم کلیک را مشخص می‌کند. `data-rahgir-ignore` یا کلاس `rahgir-ignore` آن بخش را ثبت نمی‌کند.
- مقدار input ها هرگز ارسال نمی‌شود. فیلدهای password کاملاً نادیده گرفته می‌شوند.
- برای اینکه درخواست‌های API به همان بازدیدکننده وصل شوند، در CORS بک‌اند هدرهای
  `X-Rahgir-Session` و `X-Rahgir-Anon` را مجاز کنید:
  `policy.WithHeaders("Content-Type", "Authorization", "X-Rahgir-Session", "X-Rahgir-Anon")`.

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
- حذف کامل داده یک کاربر از داشبورد (ادمین) یا `EXEC rahgir.ForgetUser`.
- نکته: `userId` ای که مرورگر با `identify` می‌فرستد از سمت کاربر قابل تغییر است (مثل همه ابزارهای مشابه)؛
  رویدادهای API که از سمت سرور و از روی Claim ها ثبت می‌شوند قابل جعل نیستند.

## توسعه

```bash
dotnet test                                   # تست‌های بک‌اند و Collector
cd packages/rahgir-next && npm run build && node test/browser.e2e.mjs   # تست مرورگر (Playwright)
cd dashboard && npm run dev                   # داشبورد روی 3100، API از Collector روی 5080
cd dashboard && npm run build && npm run publish-to-collector
```

ذخیره‌سازی از طریق `IEventStore` است؛ برای حجم خیلی بالا می‌شود پیاده‌سازی ClickHouse اضافه کرد بدون تغییر در SDK ها.
