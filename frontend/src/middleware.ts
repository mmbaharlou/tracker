import { NextResponse, type NextRequest } from "next/server";

// Fast path only: without a session cookie the panel pages go straight to /login.
// The API still checks every request; this is not the security boundary.
const COOKIES = ["tk_session", "__Host-tk_session"];

export function middleware(req: NextRequest) {
  if (COOKIES.some((c) => req.cookies.has(c))) return NextResponse.next();
  const url = req.nextUrl.clone();
  url.pathname = "/login";
  url.search = `?next=${encodeURIComponent(req.nextUrl.pathname + req.nextUrl.search)}`;
  return NextResponse.redirect(url);
}

export const config = {
  // Everything except the login page, Next.js assets, files with an extension and the proxied API / ingestion paths.
  matcher: ["/((?!login|_next|api|v1|health|.*\\..*).*)"],
};
