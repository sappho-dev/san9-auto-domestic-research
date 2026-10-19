#!/usr/bin/env python3
"""One-time public historical metadata backfill. No account login or audio download.

Only publishes a historical episode if its real Xiaoyuzhou episode ID can
be independently verified from the public RSS item GUID or official URL.
"""
import json
import re
import xml.etree.ElementTree as ET
from datetime import datetime,timezone
from email.utils import parsedate_to_datetime
from pathlib import Path
from urllib.request import Request,urlopen

ROOT=Path(__file__).resolve().parent
DATA=ROOT/"episodes.json"
SOURCES=[
 ("615a4035c8c1d14e83366946","孤岛车谈","https://www.ximalaya.com/album/49896353.xml"),
 ("5e280faf418a84a0461fbd39","疯投圈","https://crazy.capital/feed"),
 ("61358d971c5d56efe5bcb5d2","乱翻书","https://feed.xyzfm.space/yxuruh3f9mc4")
]
ID=re.compile(r"^[a-f0-9]{24}$")
LINK=re.compile(r"(?:https?://)?(?:www\.)?xiaoyuzhoufm\.com/episode/([a-f0-9]{24})(?:[/?#]|$)",re.I)
def child(parent,key):
    for e in parent:
        if e.tag.rsplit("}",1)[-1].lower()==key.lower():
            return (e.text or "").strip()
    return ""
def duration(s):
    try:
        parts=[int(p) for p in s.strip().split(":")]
        if not 1<=len(parts)<=3:return None
        t=0
        for p in parts:t=t*60+p
        return t
    except (ValueError,AttributeError):return None
def published(s):
    try:
        dt=parsedate_to_datetime(s)
        if not dt.tzinfo:dt=dt.replace(tzinfo=timezone.utc)
        return dt.astimezone(timezone.utc).isoformat(timespec="seconds").replace("+00:00","Z")
    except (ValueError,TypeError,IndexError):return ""
def load(url):
    req=Request(url,headers={"User-Agent":"Mozilla/5.0 (compatible; PodcastHistoryBackfill/1.0)","Accept":"application/rss+xml,application/xml,text/xml,*/*"})
    with urlopen(req,timeout=35) as r:
        b=r.read(8_000_001)
        if len(b)>8_000_000:raise ValueError("RSS超过8MB")
        return ET.fromstring(b)
def get_episode(item):
    title=child(item,"title").strip()
    guid=child(item,"guid")
    link=child(item,"link")
    match=LINK.search(link) or LINK.search(guid)
    if not match and ID.fullmatch(guid):eid=guid
    elif match:eid=match.group(1).lower()
    else:return None
    if not ID.fullmatch(eid) or not title:return None
    return {"id":eid,"title":title[:350],
            "url":"https://www.xiaoyuzhoufm.com/episode/"+eid,
            "published_at":published(child(item,"pubDate")),
            "duration_seconds":duration(child(item,"duration"))}
def main():
    data=json.loads(DATA.read_text(encoding="utf-8"))
    if data.get("schema")!=1 or not isinstance(data.get("channels"),dict):
        raise ValueError("现有节目数据格式不正确，停止回填")
    results=[]
    new_total=0
    for pid,name,url in SOURCES:
        rec={"channel":name,"source":url}
        try:
            root=load(url)
            items=[e for e in root.iter() if e.tag.rsplit("}",1)[-1]=="item"]
            rec["rss_items"]=len(items)
            incoming=[v for i in items if (v:=get_episode(i))]
            rec["items_with_verified_xiaoyuzhou_id"]=len(incoming)
            stored=data["channels"].get(pid)
            if not isinstance(stored,dict):
                raise ValueError("频道在原目录中不存在")
            old=stored.get("episodes",[])
            if not isinstance(old,list):raise ValueError("原节目数据不是数组")
            existing={e["id"]:e for e in old if isinstance(e,dict) and ID.fullmatch(str(e.get("id","")))}
            old_count=len(existing)
            for e in incoming:
                if e["id"] not in existing:
                    existing[e["id"]]=e
            stored["episodes"]=sorted(existing.values(),key=lambda e:(e.get("published_at") or "",e["id"]),reverse=True)
            rec.update({"before":old_count,"added":len(existing)-old_count,"after":len(existing)})
            new_total+=rec["added"]
            print(f"BACKFILL {name}: source={len(items)} nativeIDs={len(incoming)} {old_count}->{len(existing)}",flush=True)
        except Exception as exc:
            rec["error"]=str(exc)[:240]
            print(f"BACKFILL ERROR {name}: {exc}",flush=True)
        results.append(rec)
    if new_total:
        data["updated_at"]=datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00","Z")
        DATA.write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    summary={"generated_at":datetime.now(timezone.utc).isoformat(),"added_total":new_total,
             "source_rule":"Only verified official Xiaoyuzhou 24-character episode IDs are imported; no synthetic IDs or external link substitution.",
             "channels":results}
    (ROOT/"history_backfill_result.json").write_text(json.dumps(summary,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(summary,ensure_ascii=False),flush=True)
if __name__=="__main__": main()
