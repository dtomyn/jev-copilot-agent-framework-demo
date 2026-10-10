"""Scripted walkthrough of Jev.WebUi, captured as CDP screencast frames.

Usage: record.py <frames-dir>

Needs the UI already listening on DEMO_URL (default http://127.0.0.1:5088) and the TypeSafe key in
TYPESAFE_API_KEY. The key is typed into the UI's masked Config field, exactly as a presenter would,
and is never written to disk or shown on screen.

Writes <frames-dir>/NNNNN.jpg plus index.json ([{file, t, fast}]) for assemble.py.
"""
import asyncio
import base64
import json
import os
import platform
import shutil
import subprocess
import sys
import time
from html import escape
from pathlib import Path

from playwright.async_api import async_playwright

sys.stdout.reconfigure(encoding="utf-8")

URL = os.environ.get("DEMO_URL", "http://127.0.0.1:5088")
W, H = 1440, 900
AGENT_TIMEOUT_MS = 300_000

frames = []
fast = False  # frames captured while True are sped up by assemble.py

OVERLAY = r"""
(() => {
  if (window.__demo) return;
  const css = `
  #demo-cap{position:fixed;left:50%;bottom:22px;transform:translateX(-50%);max-width:1100px;
    background:rgba(15,23,42,.93);color:#f8fafc;font:600 19px/1.45 'Segoe UI',system-ui,sans-serif;
    padding:12px 22px;border-radius:12px;box-shadow:0 8px 30px rgba(0,0,0,.35);z-index:2147483646;
    text-align:center;transition:opacity .25s;opacity:0;pointer-events:none}
  #demo-cap.top{bottom:auto;top:70px}
  #demo-cap small{display:block;font-weight:400;font-size:15px;color:#cbd5e1;margin-top:2px}
  #demo-cap .ff{color:#fbbf24}
  #demo-cur{position:fixed;left:-40px;top:-40px;width:22px;height:22px;border-radius:50%;
    background:rgba(37,99,235,.35);border:2px solid #2563eb;z-index:2147483647;pointer-events:none;
    transform:translate(-50%,-50%);transition:left .45s ease,top .45s ease,opacity .3s}
  #demo-cur.click{animation:demo-pulse .35s ease}
  @keyframes demo-pulse{0%{box-shadow:0 0 0 0 rgba(37,99,235,.6)}100%{box-shadow:0 0 0 18px rgba(37,99,235,0)}}
  #demo-card{position:fixed;inset:0;z-index:2147483645;display:flex;align-items:center;justify-content:center;
    background:linear-gradient(135deg,#0f172a,#1e3a8a);color:#f8fafc;font-family:'Segoe UI',system-ui,sans-serif;
    transition:opacity .4s}
  #demo-card .in{max-width:1000px;padding:40px}
  #demo-card h1{font-size:46px;margin:0 0 12px}
  #demo-card p{font-size:22px;color:#cbd5e1;margin:6px 0}
  #demo-card ul{font-size:19px;color:#e2e8f0;line-height:1.7;margin:22px 0 0;padding-left:22px}
  #demo-card b{color:#93c5fd}`;
  const s = document.createElement('style'); s.textContent = css; document.documentElement.appendChild(s);
  const c = document.createElement('div'); c.id = 'demo-cap'; document.body.appendChild(c);
  const u = document.createElement('div'); u.id = 'demo-cur'; document.body.appendChild(u);
  window.__demo = {
    cap(html, top) { const c = document.getElementById('demo-cap'); if (!html) { c.style.opacity = 0; return; }
                     c.innerHTML = html; c.classList.toggle('top', !!top); c.style.opacity = 1; },
    move(x, y) { const u = document.getElementById('demo-cur'); u.style.display = 'block'; u.style.opacity = 1;
                 u.style.left = x + 'px'; u.style.top = y + 'px'; },
    hide() { document.getElementById('demo-cur').style.display = 'none'; },
    pulse() { const u = document.getElementById('demo-cur'); u.classList.remove('click'); void u.offsetWidth; u.classList.add('click'); },
    card(html) { let d = document.getElementById('demo-card');
      if (!html) { if (d) { d.style.opacity = 0; setTimeout(() => d.remove(), 450); } return; }
      if (!d) { d = document.createElement('div'); d.id = 'demo-card'; document.body.appendChild(d); }
      d.innerHTML = '<div class="in">' + html + '</div>'; d.style.opacity = 1; }
  };
})();
"""


def tool_version(*cmd):
    exe = shutil.which(cmd[0])
    if not exe:
        return None
    try:
        out = subprocess.run([exe, *cmd[1:]], capture_output=True, text=True, timeout=20).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    lines = out.strip().splitlines()
    return lines[0].strip() if lines else None


def machine_line():
    parts = [f"{platform.system()} {platform.release()}"]
    dotnet = tool_version("dotnet", "--version")
    if dotnet:
        parts.append(f".NET SDK {dotnet}")
    copilot = tool_version("copilot", "--version")
    if copilot:
        parts.append(copilot)
    return escape(" &middot; ".join(parts)).replace("&amp;middot;", "&middot;")


async def main(out: Path):
    global fast
    key = os.environ.get("TYPESAFE_API_KEY")
    if not key:
        sys.exit("Set TYPESAFE_API_KEY to the TypeSafe Jev key to record the live run.")

    out.mkdir(parents=True, exist_ok=True)
    for f in out.glob("*"):
        f.unlink()

    async with async_playwright() as p:
        browser = await p.chromium.launch()
        ctx = await browser.new_context(viewport={"width": W, "height": H}, device_scale_factor=1)
        pg = await ctx.new_page()
        cdp = await ctx.new_cdp_session(pg)

        async def on_frame(ev):
            name = f"{len(frames):05d}.jpg"
            (out / name).write_bytes(base64.b64decode(ev["data"]))
            frames.append({"file": name, "t": time.monotonic(), "fast": fast})
            try:
                await cdp.send("Page.screencastFrameAck", {"sessionId": ev["sessionId"]})
            except Exception:
                pass

        cdp.on("Page.screencastFrame", lambda ev: asyncio.ensure_future(on_frame(ev)))

        async def ui(js, *args):
            return await pg.evaluate(js, *args)

        async def cap(html, hold=0.0, top=False):
            await ui("([h,t]) => window.__demo.cap(h,t)", [html, top])
            if hold:
                await pg.wait_for_timeout(int(hold * 1000))

        async def click(loc, hold=0.6):
            loc = pg.locator(loc) if isinstance(loc, str) else loc
            await loc.scroll_into_view_if_needed()
            box = await loc.bounding_box()
            await ui("([x,y]) => window.__demo.move(x,y)", [box["x"] + box["width"] / 2, box["y"] + box["height"] / 2])
            await pg.wait_for_timeout(550)
            await ui("() => window.__demo.pulse()")
            await loc.click()
            await pg.wait_for_timeout(int(hold * 1000))

        async def scroll_to(selector, block="start", hold=0.9):
            await ui("() => window.__demo.hide()")
            await ui("([s,b]) => { const e = document.querySelector(s); if (b === 'start') {"
                     " window.scrollTo({top: e.getBoundingClientRect().top + window.scrollY - 72, behavior:'smooth'}); }"
                     " else { e.scrollIntoView({behavior:'smooth', block:b}); } }", [selector, block])
            await pg.wait_for_timeout(int(hold * 1000))

        async def scroll_top():
            await ui("() => window.scrollTo({top:0, behavior:'smooth'})")
            await pg.wait_for_timeout(700)

        async def level(n, caption):
            await scroll_top()
            await click(pg.locator(".rail-item").nth(n - 1), 0.9)
            await cap(caption, 2.4)

        async def wait_run_done(timeout=60):
            global fast
            fast = True
            await pg.wait_for_function(
                "() => !document.querySelector('#main .runbar .btn-run')?.textContent.includes('Running')"
                " && !/Calling the decision provider/.test(document.querySelector('#main .outcome')?.textContent || '')",
                timeout=timeout * 1000)
            fast = False

        async def run(button_name, out_hold=4.0, out_selector="#main .outcome", block="start"):
            await click(pg.get_by_role("button", name=button_name), 0.3)
            await wait_run_done()
            await pg.wait_for_timeout(300)
            await scroll_to(out_selector, block, out_hold)

        async def run_agent(caption):
            """Click 'Run the full agent', fast-forward until the child process exits, then show the tail."""
            global fast
            await click(pg.get_by_role("button", name="Run the full agent"), 0.4)
            fast = True
            await cap(f"<span class='ff'>&#9193; fast-forward</span> {caption}", top=True)
            await scroll_to("#main .outcome", "start", 0.5)
            await pg.wait_for_function(
                "() => /process exited with code/.test(document.getElementById('main').textContent)",
                timeout=AGENT_TIMEOUT_MS)
            fast = False
            await ui("() => { const m = [...document.querySelectorAll('#main pre')].pop();"
                     " m && m.scrollIntoView({behavior:'smooth', block:'end'}); }")

        await pg.goto(URL)
        await pg.wait_for_selector(".rail-item")
        await pg.evaluate(OVERLAY)
        await pg.wait_for_timeout(1200)
        await cdp.send("Page.startScreencast", {"format": "jpeg", "quality": 88, "maxWidth": W, "maxHeight": H, "everyNthFrame": 1})

        await ui("h => window.__demo.card(h)", f"""
          <h1>Ten Levels of Typed Decisions</h1>
          <p>.NET 10 &middot; Microsoft Agent Framework &middot; GitHub Copilot CLI</p>
          <p>A System-One decision engine, from one probability to a Copilot <code>preToolUse</code> gate.</p>
          <ul>
            <li>Recorded on: {machine_line()}</li>
            <li><b>Jev (TypeSafe, hosted)</b> runs <b>live</b> with an API key entered in the UI</li>
            <li>Every other provider stays on the deterministic mock</li>
          </ul>""")
        await pg.wait_for_timeout(6500)
        await ui("() => window.__demo.card(null)")
        await pg.wait_for_timeout(600)

        await cap("Each level shows its real source on the right and a Run panel on the left<small>Presenter notes are read straight out of docs/tutorial.html</small>", 4.0)
        await cap("The UI starts in Mock mode: no key, no container, no network<small>Live is greyed out until a provider is reachable</small>", 3.2)
        await click("#notes-toggle", 0.8)

        await cap("Config: paste the TypeSafe API key<small>Masked, held in memory only, never written to disk</small>")
        await click("#config-open", 0.6)
        await click("#jev-key", 0.2)
        await pg.locator("#jev-key").type(key[:24], delay=18)
        await pg.locator("#jev-key").fill(key)
        await pg.wait_for_timeout(500)
        await click("#jev-key-save", 1.8)
        await click("#config-cancel", 0.4)
        await cap("Switch the provider to Jev and the mode to Live")
        await click("[data-provider=jev]", 0.4)
        await click("[data-mode=live]", 1.6)

        await cap("Level 1: one Noul probability drives an ordinary C# if-statement<small>The 0.80 threshold is code in the repository, not a prompt</small>", 3.0)
        await scroll_to("#main .panes", "start", 1.0)
        await run("Run level 1", 5.0)
        await cap("Jev answers in a few hundred ms; the C# branch makes the call<small>Response metadata and the exact request body are shown too</small>", 3.5)

        await level(2, "Level 2: a bounded Choice replaces fuzzy routing<small>A probability for every option, not just the winner</small>")
        await run("Run level 2", 5.0)

        await level(3, "Level 3: a Score against an explicit, ordered rubric")
        await run("Run level 3", 5.0)

        await level(4, "Level 4: Noul, Choice and Score in one request against the same state")
        await run("Run level 4", 5.5)

        await level(5, "Level 5: code owns the policy<small>Deterministic hard rules answer first; only what survives becomes a model question</small>")
        await click(pg.get_by_role("button", name="curl | sh", exact=True), 0.4)
        await click(pg.get_by_role("button", name="read .env", exact=True), 0.4)
        await run("Evaluate every call", 4.0)
        await cap("ALLOW on the read-only fast path, ASK when Jev is not confident enough, DENY on hard rules<small>The \"Decided by\" column reports which gate layer fired</small>", 5.0)

        await level(6, "Level 6: the decision engine becomes an Agent Framework function tool<small>The sandbox calls jev_noul with the arguments an agent would pass</small>")
        await run("Call the tool", 4.5)

        await level(7, "Level 7: Copilot gets the whole toolbelt: jev_noul, jev_choice, jev_score")
        await cap("\"Run the full agent\" shells out to the real GitHub Copilot runtime via Agent Framework")
        await run_agent("Copilot is reasoning and calling Jev tools")
        await cap("Copilot framed its own bounded questions and called the Jev tools<small>The real agent output, unedited</small>", 6.5, top=True)

        await level(8, "Level 8: the same gate as a native Copilot CLI preToolUse hook<small>The UI starts the real hook executable and writes a payload to its stdin</small>")
        await pg.locator("#main select").first.select_option(label="force-push → deny")
        await pg.wait_for_timeout(1200)
        await run("Run the hook process", 5.0)
        await cap("Exit code 0 and exactly one JSON object: the contract Copilot CLI requires<small>The expected decision of the checked-in sample is verified against the real output</small>", 4.0)
        await scroll_top()
        await pg.locator("#main select").first.select_option(label="dotnet-format → ask")
        await pg.wait_for_timeout(900)
        await run("Run the hook process", 0.5)
        await cap("A model-decided sample: live Jev answers for itself<small>The checked-in expectation is what the mock run asserts in scripts/test-hook</small>", 5.0)

        await level(9, "Level 9: confidence-aware escalation<small>Ambiguity becomes Copilot's human \"ask\" flow instead of a guess</small>")
        await click(pg.get_by_role("button", name="git push --force-with-lease", exact=True), 0.4)
        await click(pg.get_by_role("button", name="decision tool + secret", exact=True), 0.4)
        await run("Evaluate every call", 5.5)

        await level(10, "Level 10: the agent decides for itself whether the decision engine is useful")
        await run_agent("Copilot decides whether a typed judgment fits")
        await cap("Whether to call Jev is the agent's own call, and it can differ from run to run<small>The system prompt tells it to skip Jev for facts it can compute</small>", 6.5, top=True)

        await cap(None)
        await pg.wait_for_timeout(300)
        await scroll_top()
        await cap("Presenter mode and a dark theme for the projector")
        await click("#theme-toggle", 0.8)
        await click("#present-toggle", 1.0)
        await click(pg.locator(".rail-item").nth(4), 2.5)
        await click("#present-toggle", 0.6)
        await click("#theme-toggle", 0.6)

        await cap(None)
        await ui("() => window.__demo.hide()")
        await ui("h => window.__demo.card(h)", """
          <h1>Ten Levels of Typed Decisions</h1>
          <p>The model advises. Code owns the policy.</p>
          <ul>
            <li>Start it: <code>./scripts/webui.ps1</code> &rarr; http://127.0.0.1:5088</li>
            <li>Providers: TypeSafe Jev, Laya, Decider, Cloudflare Clef, Microsoft, or the deterministic mock</li>
            <li>Walkthrough and presenter notes: <code>docs/tutorial.html</code></li>
          </ul>""")
        await pg.wait_for_timeout(5000)

        await cdp.send("Page.stopScreencast")
        await pg.wait_for_timeout(300)
        await browser.close()

    t0 = frames[0]["t"]
    for f in frames:
        f["t"] -= t0
    (out / "index.json").write_text(json.dumps(frames))
    print(f"{len(frames)} frames, {frames[-1]['t']:.1f}s real time")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    asyncio.run(main(Path(sys.argv[1])))
