// End-to-end check of the browser SDK in real Chromium: node test/browser.e2e.mjs
import http from "node:http";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
const { chromium } = createRequire(import.meta.url)("playwright");

const batches = [], apiHeaders = [];
let remote = { enabled: true, sampleRate: 1 };
const collector = http.createServer((req, res) => {
  let body = ""; req.on("data", (c) => (body += c));
  req.on("end", () => {
    if (req.url.startsWith("/v1/config")) { res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" }); return res.end(JSON.stringify(remote)); }
    if (req.url.startsWith("/v1/batch")) batches.push(JSON.parse(body)); res.writeHead(202); res.end();
  });
}).listen(5091);

const page = `<!doctype html><html><body>
<button id="buy" data-rahgir="خرید">Buy now</button>
<form id="login" name="login"><input name="email"><input name="password" type="password"><button type="submit">Go</button></form>
<a href="/docs?token=abc" id="link">Docs</a>
<script type="module">
  import { init, rahgir } from "/core.js";
  init({ projectKey: "shop", collectorUrl: "http://127.0.0.1:5091", flushIntervalMs: 300, respectDoNotTrack: false });
  window.rahgir = rahgir;
  document.getElementById("login").addEventListener("submit", (e) => e.preventDefault());
  document.getElementById("link").addEventListener("click", (e) => e.preventDefault());
</script></body></html>`;
const site = http.createServer((req, res) => {
  if (req.url === "/core.js") { res.writeHead(200, { "Content-Type": "text/javascript" }); return res.end(readFileSync("dist/core.js")); }
  if (req.url.startsWith("/api/")) { apiHeaders.push(req.headers); res.writeHead(200); return res.end("{}"); }
  res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" }); res.end(page);
}).listen(5090);

const browser = await chromium.launch();
const p = await browser.newPage();
await p.goto("http://127.0.0.1:5090/shop");
await p.click("#buy");
await p.fill("input[name=email]", "a@b.c");
await p.fill("input[name=password]", "hunter2");
await p.click("#link");
await p.click("form button");
await p.evaluate(() => fetch("/api/orders", { method: "POST" }));
await p.evaluate(() => { history.pushState({}, "", "/cart"); });
await p.evaluate(() => window.rahgir.identify("user-1", { name: "Ali", password: "x" }));
await p.evaluate(() => window.rahgir.track("AddToCart", { sku: "A1" }));
await p.waitForTimeout(1200);

// Switched off centrally: a fresh visitor sends nothing.
remote = { enabled: false, sampleRate: 1 };
const ctx2 = await browser.newContext();
const p2 = await ctx2.newPage();
const before = batches.length;
await p2.goto("http://127.0.0.1:5090/other");
await p2.click("#buy");
await p2.waitForTimeout(1200);
const offBatches = batches.length - before;
await browser.close(); collector.close(); site.close();

const events = batches.flatMap((b) => b.events);
const types = events.map((e) => `${e.type}:${e.name ?? e.path}`);
const check = (cond, msg) => { if (!cond) { console.error("FAIL:", msg, "\n", JSON.stringify(events, null, 1)); process.exit(1); } console.log("ok -", msg); };
check(events.some((e) => e.type === "pageview" && e.path === "/shop"), "initial page view");
check(events.some((e) => e.type === "pageview" && e.path === "/cart"), "client-side navigation page view");
check(events.some((e) => e.type === "click" && e.name === "خرید"), "click with data-rahgir name");
check(events.some((e) => e.type === "click" && e.props?.href === "/docs"), "link click without query string");
check(events.some((e) => e.type === "submit" && e.name === "login"), "form submit");
check(events.some((e) => e.type === "change" && e.name === "email"), "input change recorded");
check(!events.some((e) => e.type === "change" && e.name === "password"), "password field ignored");
check(!JSON.stringify(events).includes("hunter2") && !JSON.stringify(events).includes("a@b.c"), "no input values sent");
const ident = events.find((e) => e.type === "identify");
check(ident?.userId === "user-1" && ident.props.password === "***" && ident.props.name === "Ali", "identify masks traits");
check(events.find((e) => e.type === "custom")?.userId === "user-1", "events after identify carry userId");
check(new Set(events.map((e) => e.sessionId)).size === 1 && new Set(events.map((e) => e.anonId)).size === 1, "one session and visitor");
check(apiHeaders[0]?.["x-rahgir-session"] === events[0].sessionId, "same-origin API call carries session header");
check(offBatches === 0, "central off switch stops a new visitor's events");
console.log(`\nall good: ${events.length} events in ${batches.length} batches`);
