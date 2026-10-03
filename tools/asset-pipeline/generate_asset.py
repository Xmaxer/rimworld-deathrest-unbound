"""Single-shot Gemini 3 Pro Image ("Nano Banana Pro") asset generator.

Deliberately does ONE generation per invocation — no loops, no batching.
Run it manually, look at the result, decide what to do next.

Every run is kept: outputs land in drafts/<asset>/vNN.png with a sidecar
vNN.json recording the exact prompt/refs/model used, so history is never lost
or overwritten.

Usage:
    python generate_asset.py --asset compounder_east \
        --ref reference-mods/github/VanillaFurnitureExpanded-Power/Textures/Things/Building/GasMachines/GasPoweredRefinery_east.png
"""
import argparse
import os
import base64
import datetime
import json
import re
import sys
import urllib.request
import urllib.error
from pathlib import Path

GEN_ROOT = Path(__file__).resolve().parent / "drafts"
MODEL = "models/gemini-3-pro-image"
API_URL = f"https://generativelanguage.googleapis.com/v1beta/{MODEL}:generateContent"

DEFAULT_PROMPT = """2D top-down game asset sprite in the style of RimWorld / Prison Architect \
— flat orthographic top-down projection, camera looking almost straight down with only a very \
slight forward tilt for readability. NOT an isometric render, NOT a 3D perspective render — no \
vanishing point, no converging edges, no visible side walls in true 3D perspective, no \
beveled/chamfered edges. Shade every surface using flat cel-shading technique: each rounded form \
(like a cylindrical tank) is built from exactly 2-3 discrete flat color bands of the same hue at \
different brightness (a base tone, one darker band for the shadowed side, optionally one lighter \
band for the lit side), with a hard or barely-feathered edge between bands — like a children's \
picture-book illustration or a simple mobile game icon. Do not blend, gradient, or smoothly \
interpolate between the bands. No small bright highlight dots or streaks anywhere. A single thin, \
uniform dark outline separating color regions.

Industrial gas compounder machine: steel housing, pipe fittings, pressure gauges, valves, canister \
docking ports. Muted industrial palette (grey/steel, dark metal accents, small colored indicator \
lights). Keep the design SIMPLE and uncluttered: no more than 3-4 distinct functional details total \
(e.g. one gauge, one valve wheel, one or two pipe junctions) — this is a small stylized game icon, \
not a technical blueprint. Avoid dense pipe networks, avoid repeating the same small greeble \
multiple times, avoid excessive small parts. Prioritize a clean, simple, easily-readable silhouette \
with a few large clear shapes over lots of tiny mechanical detail.

East/side view — viewed from the machine's side profile, showing its long axis (the machine is 3 \
tiles long, 1 tile wide) running vertically top-to-bottom in frame, with the narrow 1-tile depth as \
the horizontal width. This is the side of the machine, not the front control panel or back panel. \
The machine itself should be a tall, narrow vertical shape, much taller than it is wide, roughly a \
1:2 width-to-height ratio.

The object fills the canvas edge-to-edge with minimal empty margin — crop tight to the silhouette, \
no wasted transparent space around it. Must read clearly as a simple flat icon at small size. \
Transparent background. No text, no logo, no watermark."""


def load_api_key() -> str:
    """Reads GEMINI_API_KEY from the environment. Never print or log the value."""
    key = os.environ.get("GEMINI_API_KEY", "").strip()
    if not key:
        sys.exit("GEMINI_API_KEY is not set in the environment")
    return key


def build_parts(prompt: str, ref_paths: list[str]) -> list[dict]:
    parts = [{"text": prompt}]
    for ref in ref_paths:
        ref_path = Path(ref)
        if not ref_path.exists():
            sys.exit(f"Reference image not found: {ref_path}")
        data = base64.b64encode(ref_path.read_bytes()).decode("ascii")
        parts.append({"inlineData": {"mimeType": "image/png", "data": data}})
    return parts


def next_version_path(asset: str) -> tuple[Path, int]:
    if not re.fullmatch(r"[a-z0-9_]+", asset):
        sys.exit("--asset must be lowercase letters/digits/underscores only")
    asset_dir = GEN_ROOT / asset
    asset_dir.mkdir(parents=True, exist_ok=True)
    existing = sorted(asset_dir.glob("v*.png"))
    versions = [int(p.stem[1:]) for p in existing if p.stem[1:].isdigit()]
    next_v = (max(versions) + 1) if versions else 1
    return asset_dir / f"v{next_v:02d}.png", next_v


def main():
    ap = argparse.ArgumentParser(description="Single-shot Gemini 3 Pro image generation")
    ap.add_argument("--asset", required=True, help="Asset slug, e.g. compounder_east — output goes to drafts/<asset>/vNN.png")
    ap.add_argument("--prompt", default=None, help="Override the default prompt")
    ap.add_argument("--ref", action="append", default=[], help="Reference image path (repeatable)")
    args = ap.parse_args()

    out_path, version = next_version_path(args.asset)

    key = load_api_key()
    prompt = args.prompt or DEFAULT_PROMPT
    parts = build_parts(prompt, args.ref)

    payload = {
        "contents": [{"parts": parts}],
        "generationConfig": {"responseModalities": ["IMAGE"]},
    }

    req = urllib.request.Request(
        f"{API_URL}?key={key}",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    print(f"Requesting {MODEL} for asset='{args.asset}' v{version:02d} with {len(args.ref)} reference image(s)...")
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            data = json.loads(resp.read())
    except urllib.error.HTTPError as e:
        body = e.read().decode(errors="replace")
        sys.exit(f"HTTP {e.code}: {body[:1000]}")

    found_image = False
    for candidate in data.get("candidates", []):
        for part in candidate.get("content", {}).get("parts", []):
            inline = part.get("inlineData") or part.get("inline_data")
            if inline and inline.get("data"):
                img_bytes = base64.b64decode(inline["data"])
                out_path.write_bytes(img_bytes)
                print(f"Saved image: {out_path} ({len(img_bytes)} bytes)")
                found_image = True
            elif "text" in part:
                print("Model text response:", part["text"][:500])

    if not found_image:
        print("No image returned. Full response:")
        print(json.dumps(data, indent=2)[:3000])
        sys.exit(1)

    meta_path = out_path.with_suffix(".json")
    meta_path.write_text(json.dumps({
        "asset": args.asset,
        "version": version,
        "model": MODEL,
        "timestamp": datetime.datetime.now().isoformat(timespec="seconds"),
        "prompt": prompt,
        "refs": args.ref,
    }, indent=2))
    print(f"Saved metadata: {meta_path}")


if __name__ == "__main__":
    main()
