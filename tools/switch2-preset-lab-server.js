const http = require("http");
const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

const args = parseArgs(process.argv.slice(2));
const port = Number(args.port || process.env.SWITCH2_PRESET_LAB_PORT || 8787);
const adbPath = args.adb || process.env.ADB_PATH || "adb.exe";
let deviceSerial = args.serial || process.env.ADB_SERIAL || "";

const htmlPath = path.join(__dirname, "switch2-preset-lab.html");
const stopHex = "0a910102000800000000000000000000";
const knownTargets = new Set(["cmd", "649d", "3dac", "4147", "fdf", "abfdf", "cc48"]);

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    const item = argv[i];
    if (!item.startsWith("--")) continue;
    const key = item.slice(2);
    const value = argv[i + 1] && !argv[i + 1].startsWith("--") ? argv[++i] : "true";
    out[key] = value;
  }
  return out;
}

function json(res, status, body) {
  const text = JSON.stringify(body, null, 2);
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "cache-control": "no-store",
  });
  res.end(text);
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let body = "";
    req.on("data", (chunk) => {
      body += chunk;
      if (body.length > 1_000_000) {
        reject(new Error("request body too large"));
        req.destroy();
      }
    });
    req.on("end", () => {
      if (!body) {
        resolve({});
        return;
      }
      try {
        resolve(JSON.parse(body));
      } catch (err) {
        reject(new Error("invalid JSON body"));
      }
    });
  });
}

function run(exe, argv, options = {}) {
  return new Promise((resolve) => {
    const child = spawn(exe, argv, {
      cwd: path.join(__dirname, ".."),
      windowsHide: true,
      ...options,
    });
    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (data) => {
      stdout += data.toString("utf8");
    });
    child.stderr.on("data", (data) => {
      stderr += data.toString("utf8");
    });
    child.on("error", (err) => {
      resolve({ code: -1, stdout, stderr: String(err) });
    });
    child.on("close", (code) => {
      resolve({ code, stdout, stderr });
    });
  });
}

async function adb(argv) {
  return run(adbPath, argv);
}

async function detectSerial() {
  if (deviceSerial) return deviceSerial;
  const result = await adb(["devices", "-l"]);
  if (result.code !== 0) {
    throw new Error(result.stderr || result.stdout || "adb devices failed");
  }
  const lines = result.stdout.split(/\r?\n/);
  for (const line of lines) {
    const marker = " device ";
    const idx = line.indexOf(marker);
    if (idx > 0) {
      deviceSerial = line.slice(0, idx).trim();
      return deviceSerial;
    }
  }
  throw new Error("no online adb device found");
}

async function adbShellRoot(command) {
  const serial = await detectSerial();
  const result = await adb(["-s", serial, "shell", "su", "-c", command]);
  if (result.code !== 0) {
    throw new Error((result.stderr || result.stdout || `adb exited ${result.code}`).trim());
  }
  return result;
}

function normalizeTarget(value) {
  const target = String(value || "cmd").trim().toLowerCase();
  if (knownTargets.has(target)) return target;
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(target)) {
    return target;
  }
  throw new Error("invalid BLE target");
}

function normalizeHex(value) {
  const hex = String(value || "").replace(/[^0-9a-fA-F]/g, "").toLowerCase();
  if (!hex || hex.length % 2 !== 0) throw new Error("hex must contain whole bytes");
  if (hex.length > 512) throw new Error("hex payload too long");
  return hex;
}

function bytesToHex(bytes) {
  if (!Array.isArray(bytes)) throw new Error("bytes must be an array");
  return bytes.map((value) => {
    const byte = Number(value);
    if (!Number.isInteger(byte) || byte < 0 || byte > 255) {
      throw new Error("each byte must be 0..255");
    }
    return byte.toString(16).padStart(2, "0");
  }).join("");
}

function clampHoldMs(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return 260;
  return Math.max(0, Math.min(5000, Math.round(n)));
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function sendLine(target, hex) {
  const command = `echo ${target} ${hex} > /data/local/tmp/switch2_ble_write.txt`;
  return adbShellRoot(command);
}

async function sendPayload(payload) {
  const target = normalizeTarget(payload.target);
  const hex = payload.hex ? normalizeHex(payload.hex) : bytesToHex(payload.bytes);
  const holdMs = clampHoldMs(payload.holdMs);
  const autoStop = payload.autoStop !== false;

  const active = await sendLine(target, hex);
  if (autoStop && hex !== stopHex) {
    await delay(holdMs);
    await sendLine("cmd", stopHex);
  }
  return {
    ok: true,
    serial: await detectSerial(),
    target,
    hex,
    holdMs,
    autoStop,
    stdout: active.stdout.trim(),
    stderr: active.stderr.trim(),
  };
}

async function status() {
  const devices = await adb(["devices", "-l"]);
  let serial = "";
  let tail = "";
  let flags = "";
  try {
    serial = await detectSerial();
    const log = await adbShellRoot("tail -n 10 /data/local/tmp/switch2_ble_bridge.log 2>/dev/null");
    tail = log.stdout;
    const flagResult = await adbShellRoot("ls -1 /data/local/tmp/switch2_haptic_* 2>/dev/null || true");
    flags = flagResult.stdout;
  } catch (err) {
    tail = String(err.message || err);
  }
  return {
    ok: devices.code === 0,
    adbPath,
    serial,
    devices: devices.stdout,
    flags,
    tail,
  };
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host}`);

    if (req.method === "GET" && url.pathname === "/") {
      const html = fs.readFileSync(htmlPath);
      res.writeHead(200, {
        "content-type": "text/html; charset=utf-8",
        "cache-control": "no-store",
      });
      res.end(html);
      return;
    }

    if (req.method === "GET" && url.pathname === "/api/status") {
      json(res, 200, await status());
      return;
    }

    if (req.method === "POST" && url.pathname === "/api/send") {
      json(res, 200, await sendPayload(await readBody(req)));
      return;
    }

    if (req.method === "POST" && url.pathname === "/api/stop") {
      json(res, 200, await sendPayload({ target: "cmd", hex: stopHex, autoStop: false, holdMs: 0 }));
      return;
    }

    json(res, 404, { ok: false, error: "not found" });
  } catch (err) {
    json(res, 500, { ok: false, error: String(err.message || err) });
  }
});

server.listen(port, "127.0.0.1", () => {
  console.log(`Switch 2 preset lab: http://127.0.0.1:${port}/`);
  console.log(`ADB: ${adbPath}`);
});
