#!/usr/bin/env python3
"""Check whether public web search indexes official historical episode URLs."""
import json,html,re
from datetime import datetime,timezone
from pathlib import Path
from urllib.request import Request,urlopen
from urllib.error import HTTPError
from urllib.parse import quote,unquote
root=Path(__file__).resolve().parent
qs=['site:www.xiaoyuzhoufm.com/episode/ "疯投圈" "134"',
    'site:www.xiaoyuzhoufm.com/episode/ "孤岛车谈" "196"']
engines={"bing":"https://www.bing.com/search?q={q}&count=30",
         "google":"https://www.google.com/search?q={q}&num=30",
         "duckduckgo":"https://html.duckduckgo.com/html/?q={q}"}
results=[]
for q in qs:
    for engine,base in engines.items():
        item={"engine":engine,"query":q}
        try:
            url=base.format(q=quote(q))
            req=Request(url,headers={"User-Agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36"})
            with urlopen(req,timeout=12) as r:
                body=r.read(1000000).decode("utf-8","replace")
                item["http_status"]=r.status
                item["body_length"]=len(body)
                normalized=html.unescape(unquote(unquote(body)))
                item["found_ids"]=list(dict.fromkeys(re.findall(r'(?:(?:www\.)?xiaoyuzhoufm\.com)/episode/([a-f0-9]{24})',normalized,re.I)))[:25]
                item["sample"]=re.sub(r"\s+"," ",normalized[:180])
        except Exception as exc:
            item["error"]=str(exc)[:240]
        results.append(item)
        print(engine,q,"ids",item.get("found_ids"),"error",item.get("error"),flush=True)
(root/"search_probe.json").write_text(json.dumps({"updated_at":datetime.now(timezone.utc).isoformat(),"results":results},ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
