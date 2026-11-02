import "./globals.css";
import type { Metadata } from "next";

export const metadata: Metadata = { title: "رهگیر", robots: { index: false, follow: false } };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="fa" dir="rtl">
      <body>{children}</body>
    </html>
  );
}
