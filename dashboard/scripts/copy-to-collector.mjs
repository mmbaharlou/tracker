// Copies the static export into the Collector's wwwroot so one IIS site serves everything.
import { cpSync, rmSync, existsSync } from "node:fs";
import { resolve } from "node:path";
const out = resolve("out");
const target = resolve("../src/Tracker.Collector/wwwroot");
if (!existsSync(out)) { console.error("Run `npm run build` first."); process.exit(1); }
rmSync(target, { recursive: true, force: true });
cpSync(out, target, { recursive: true });
console.log("Dashboard copied to", target);
