#!/usr/bin/env python3
"""One-time public history source probe. No authentication or audio access."""
import json, re, time
from datetime import datetime, timezone
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parent
CH = [
 ("孤岛车谈","615a4035c8c1d14e83366946",
  ["https://www.ximalaya.com/album/49896353.xml",
   "https://rsshub.app/xiaoyuzhou/podcast/615a4035c8c1d14e83366946?limit=500"]),
 ("疯投圈","5e280faf418a84a0461fbd39",
  ["https://crazy.capital/feed",
   "https://rsshub.app/xiaoyuzhou/podcast/5e280faf418a84a0461fbd39?limit=500"]),
 ("乱翻书","61358d971c5d56efe5bcb5d2",
  ["https://feed.xyzfm.space/yxuruh3f9mc4",
   "https://rsshub.app/xiaoyuzhou/podcast/61358d971c5d56efe5bcb5d2?limit=500",
   "https://rsshub.bestblogs.dev/xiaoyuzhou/podcast/61358d971c5d56efe5bcb5d2?limit=500"])
]
def request(url, body=None):
    headers={"User-Agent":"Mozilla/5.0 (compatible; podcast-public-backfill/1.0)",
             "Accept":"application/rss+xml,application/xml,text/xml,text/html,application/json,*/*"}
    if body is not None:headers["Content-Type"]="application/json"
    with urlopen(Request(url,body,headers),timeout=19) as r:
        result=r.read(9_000_000)
        return result.decode("utf-8","replace"),r.status,r.geturl()
def item_info(item):
    def get(k):
        for el in item:
            if el.tag.rsplit('}',1)[-1].lower()==k:
                return (el.text or "").strip()
        return ""
    title=get("title")
    link=get("link")
    guid=get("guid") or get("id")
    page=guid+" "+link
    mid=re.search(r'(?:xiaoyuzhoufm\.com/episode/|[?&]eid=)([0-9a-f]{24})',page,re.I)
    if not mid and re.fullmatch(r"[0-9a-f]{24}",guid or ""):mid=re.match(r"[0-9a-f]{24}",guid)
    return {"title":title[:180], "guid":guid[:150], "link":link[:220],"native_eid":mid.group(1) if mid else None}
report={"generated_at":datetime.now(timezone.utc).isoformat(),"sources":[],"api_probe":[]}
for name,pid,urls in CH:
    for url in urls:
        record={"name":name,"url":url}
        try:
            body,status,final=request(url)
            record.update({"status":status,"final":final,"bytes":len(body.encode()),"start":body[:140]})
            root=ET.fromstring(body)
            items=[el for el in root.iter() if el.tag.rsplit('}',1)[-1] in ("item","entry")]
            parsed=[item_info(x) for x in items]
            record.update({"count":len(items),"native_ids":sum(bool(x["native_eid"]) for x in parsed),
                "samples":parsed[:2]+(parsed[-2:] if len(parsed)>2 else [])})
        except Exception as exc:
            record["error"]=str(exc)[:350]
        report["sources"].append(record)
        print(name,record.get("count"),"native",record.get("native_ids"),record.get("error",""),flush=True)
        time.sleep(.3)
    body=json.dumps({"pid":pid,"limit":15,"order":"desc"}).encode()
    for base in ["https://api.xiaoyuzhoufm.com/v1/episode/list"]:
        rec={"name":name,"url":base}
        try:
            data,status,final=request(base,body)
            rec.update({"status":status,"sample":data[:240]})
        except HTTPError as exc:
            rec.update({"status":exc.code,"sample":exc.read(300).decode("utf-8","replace")})
        except Exception as exc:rec["error"]=str(exc)[:200]
        report["api_probe"].append(rec)
        print("API",name,rec,flush=True)
(ROOT/"history_probe.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
