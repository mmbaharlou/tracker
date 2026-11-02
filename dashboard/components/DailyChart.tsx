import { num, shortDay } from "@/lib/api";

export function DailyChart({ days }: { days: { day: string; visitors: number; pageViews: number }[] }) {
  if (!days.length) return <div className="empty">در این بازه داده‌ای ثبت نشده است.</div>;
  const max = Math.max(...days.map((d) => d.visitors), 1);
  return (
    <div>
      <div className="chart" role="img" aria-label="بازدیدکنندگان روزانه">
        {days.map((d) => (
          <div key={d.day} className="bar" style={{ height: `${(d.visitors / max) * 100}%` }}
            title={`${shortDay(d.day.slice(0, 10))}: ${num(d.visitors)} بازدیدکننده، ${num(d.pageViews)} بازدید صفحه`} />
        ))}
      </div>
      <div className="chart-x">
        <span>{shortDay(days[0].day.slice(0, 10))}</span>
        <span>{shortDay(days[days.length - 1].day.slice(0, 10))}</span>
      </div>
    </div>
  );
}
