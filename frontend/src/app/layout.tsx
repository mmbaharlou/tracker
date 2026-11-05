import type { Metadata, Viewport } from "next";
import "@fontsource-variable/vazirmatn";
import "./globals.css";
import { app } from "@/lib/app";

export const metadata: Metadata = {
  title: { default: app.name, template: `%s | ${app.name}` },
  description: app.tagline,
  applicationName: app.name,
  robots: { index: false, follow: false },
  icons: { icon: "/icon.svg" },
};

export const viewport: Viewport = { themeColor: "#0f766e", width: "device-width", initialScale: 1 };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="fa" dir="rtl">
      <body className="min-h-dvh antialiased">{children}</body>
    </html>
  );
}
