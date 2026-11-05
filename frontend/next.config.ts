import type { NextConfig } from "next";

const apiUrl = process.env.API_INTERNAL_URL ?? "http://localhost:5080";

const nextConfig: NextConfig = {
  distDir: process.env.NEXT_DIST_DIR ?? ".next",
  output: "standalone",
  poweredByHeader: false,
  // The browser talks only to this origin. /api (panel) and the ingestion paths are proxied to the ASP.NET Core API.
  // In production IIS sends the ingestion paths straight to the API; these rewrites make dev and simple setups work too.
  async rewrites() {
    return ["api", "v1", "health"].map((p) => ({ source: `/${p}/:path*`, destination: `${apiUrl}/${p}/:path*` }));
  },
  async headers() {
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "no-referrer" },
          { key: "X-Frame-Options", value: "DENY" },
          { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
          { key: "X-Robots-Tag", value: "noindex, nofollow" },
        ],
      },
    ];
  },
};

export default nextConfig;
