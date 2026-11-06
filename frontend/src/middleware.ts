import { NextResponse, type NextRequest } from "next/server";

// Fast path only: without a session cookie the panel pages go straight to /login.
// The API still checks every request; this is not the security boundary.
const COOKIES = ["tk_session", "__Host-tk_session"];

export function middleware(req: NextRequest) {
  if (COOKIES.some((c) => req.cookies.has(c))) return NextResponse.next();
  // Behind IIS the request URL holds the internal address (127.0.0.1:port); IIS sends the public one in X-Forwarded-*.
  const host = req.headers.get("x-forwarded-host") ?? req.headers.get("host") ?? req.nextUrl.host;
  const proto = req.headers.get("x-forwarded-proto") ?? req.nextUrl.protocol.replace(":", "");
  const next = encodeURIComponent(req.nextUrl.pathname + req.nextUrl.search);
  return NextResponse.redirect(`${proto}://${host}/login?next=${next}`);
}

export const config = {
  // Everything except the login page, Next.js assets, files with an extension and the proxied API / ingestion paths.
  matcher: ["/((?!login|_next|api|v1|health|.*\\..*).*)"],
};
