#!/usr/bin/env python3
"""Archive public RSS history for external-hosted podcasts, preserving provenance.

This archive is NOT used by the unchanged local HTML. We never fabricate
24-character Xiaoyuzhou episode IDs from external RSS links.
"""
import json,re,xml.etree.ElementTree as ET
from datetime import datetime,timezone
from email.utils import parsedate_to_datetime
from pathlib import Path
from urllib.request import Request,urlopen
ROOT=Path(__file__).resolve().parent
SOURCES=[
 ("615a4035c8c1d14e83366946","孤岛车谈","https://www.ximalaya.com/album/49896353.xml","www.ximalaya.com"),
 ("5e280faf418a84a0461fbd39","疯投圈","https://crazy.capital/feed","crazy.capital")
]
def val(e,name):
    for c in e:
        if c.tag.rsplit("}",1)[-1].lower()==name.lower():return (c.text or "").strip()
    return ""
def when(s):
    try:
        d=parsedate_to_datetime(s)
        if not d.tzinfo:d=d.replace(tzinfo=timezone.utc)
        return d.astimezone(timezone.utc).isoformat(timespec="seconds").replace("+00:00","Z")
    except Exception:return ""
def secs(s):
    try:
        v=[int(p) for p in s.split(":")]
        if not 1<=len(v)<=3:return None
        t=0
        for p in v:t=t*60+p
        return t
    except Exception:return None
def norm(s):
    return re.sub(r"[^\w]","",s.lower())
def main():
    original=json.loads((ROOT/"episodes.json").read_text(encoding="utf-8"))
    archive={"schema":1,"updated_at":datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00","Z"),
        "note":"External public RSS metadata archive. This file is not read by the unchanged local HTML. Missing Xiaoyuzhou episode IDs are deliberately left null.",
        "channels":{}}
    for pid,name,url,host in SOURCES:
        req=Request(url,headers={"User-Agent":"Mozilla/5.0 (PodcastHistoryArchive/1.0)"})
        with urlopen(req,timeout=40) as r:
            b=r.read(8_000_001)
            if len(b)>8_000_000:raise ValueError("RSS too large")
        root=ET.fromstring(b)
        rss_items=[e for e in root.iter() if e.tag.rsplit("}",1)[-1]=="item"]
        existing=original.get("channels",{}).get(pid,{}).get("episodes",[])
        known={norm(e["title"]):e["id"] for e in existing if e.get("title") and e.get("id")}
        episodes=[];seen=set()
        for item in rss_items:
            title=val(item,"title")
            link=val(item,"link")
            if not title or not link.startswith("https://"+host+"/") or link in seen:continue
            seen.add(link)
            xid=known.get(norm(title))
            episodes.append({"title":title[:350],"published_at":when(val(item,"pubDate")),
              "duration_seconds":secs(val(item,"duration")),"source_url":link,
              "xiaoyuzhou_eid":xid or None})
        archive["channels"][pid]={"title":name,"source_rss":url,"count":len(episodes),
            "linked_to_native_count":sum(bool(e["xiaoyuzhou_eid"]) for e in episodes),
            "episodes":episodes}
        print(f"ARCHIVE {name} {len(episodes)} external source episodes",flush=True)
    path=ROOT/"history_external.json"
    path.write_text(json.dumps(archive,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
if __name__=="__main__":main()
