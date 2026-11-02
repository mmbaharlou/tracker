// Production: static export served by the Collector (same origin, no Node server needed).
// Development: `npm run dev` proxies /api to a locally running Collector.
const isProd = process.env.NODE_ENV === "production";
const collector = process.env.RAHGIR_COLLECTOR_URL || "http://localhost:5080";

/** @type {import('next').NextConfig} */
const nextConfig = {
  output: isProd ? "export" : undefined,
  trailingSlash: true,
  reactStrictMode: true,
  poweredByHeader: false,
  images: { unoptimized: true },
  ...(isProd ? {} : { async rewrites() { return [{ source: "/api/:path*", destination: `${collector}/api/:path*` }]; } }),
};
export default nextConfig;
