"""How well qkg/hadith/profile ranks the verse a hadith is about, when the hadith does not quote it.

The reference: the verses the English translators cite in brackets for a hadith whose Arabic does
not quote them (QuranQuote.Kind == "translator") — about 80 hadith in Bukhari and Muslim. The
detector cannot have found them, and shared words cannot simply find the quotation, so they test
the "closest verses" ranking. Reported: the share of those hadith with a cited verse in the top 10,
and the mean reciprocal rank within it, for a grid of the two weights.

    python3 tools/hadith-benchmark/evaluate.py            # against the local workspace (.env)

Reads WORKSPACE_ADMIN_USER / WORKSPACE_ADMIN_PASSWORD / CURIOSITY_URL from .env; prints no secret.
"""
import json, os, pathlib, sys, time, urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
env = {}
for line in (ROOT / ".env").read_text().splitlines():
    if "=" in line and not line.lstrip().startswith("#"):
        k, v = line.split("=", 1)
        env[k.strip()] = v.strip().strip('"')
URL = env.get("CURIOSITY_URL", "http://localhost:8080/").rstrip("/") + "/"

def post(path, body, token=None):
    req = urllib.request.Request(URL + path, data=json.dumps(body).encode(), headers={"Content-Type": "application/json", **({"Authorization": "Bearer " + token} if token else {})})
    for _ in range(60):
        with urllib.request.urlopen(req, timeout=120) as r:
            if r.status == 200:
                text = r.read().decode()
                return json.loads(text) if text.strip().startswith(("{", "[")) else text.strip().strip('"')
            key = r.headers.get("X-MSK-ENDPOINT-KEY")
            req.add_header("X-MSK-ENDPOINT-KEY", key or "")
            time.sleep(1)
    raise RuntimeError(path + ": no answer")

token = post("api/login/create", {"UserName": env["WORKSPACE_ADMIN_USER"], "Password": env["WORKSPACE_ADMIN_PASSWORD"]})
if isinstance(token, dict): token = token.get("Token") or token.get("token")

cases, skip = [], 0
while True:
    page = post("api/endpoints/run/qkg/hadith/list", {"Kind": "translator", "Skip": skip, "Take": 100}, token)
    cases += page["Rows"]; skip += 100
    if skip >= page["Total"]: break

def cited(uid):
    p = post("api/endpoints/run/qkg/hadith/profile", {"Uid": uid}, token)
    return {q["VerseKey"] for q in p["Quotes"] if q["Kind"] == "translator"}

refs = {c["Uid"]: cited(c["Uid"]) for c in cases}
print(f"{len(refs)} hadith whose translator cites a verse their Arabic does not quote")

for sem, lex in [(1, 0), (0, 1), (1, 1), (1, 0.5), (1, 0.25)]:
    hits, rr = 0, 0.0
    for uid, verses in refs.items():
        p = post("api/endpoints/run/qkg/hadith/profile", {"Uid": uid, "Semantic": sem, "Lexical": lex}, token)
        ranks = [i + 1 for i, v in enumerate(p["Closest"]) if v["VerseKey"] in verses]
        if ranks: hits += 1; rr += 1 / ranks[0]
    print(f"  semantic {sem:<4} lexical {lex:<4}  in the top 10: {hits / len(refs):5.1%}   MRR@10: {rr / len(refs):.3f}")
