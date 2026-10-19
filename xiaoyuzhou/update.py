#!/usr/bin/env python3
"""Index only public Xiaoyuzhou podcast pages. No RSS, login or audio downloads."""
from __future__ import annotations

import json
import os
import re
import sys
import time
from datetime import datetime, timezone
from html.parser import HTMLParser
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parent
CHANNELS = ROOT / "channels.json"
DATA = ROOT / "episodes.json"
ID_RE = re.compile(r"[a-f0-9]{24}\Z")
LINK_RE = re.compile(r"https?://(?:www\.)?xiaoyuzhoufm\.com/podcast/[a-f0-9]{24}(?:/|\?[^\s<>)]*)?", re.I)


class NextDataParser(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=False)
        self.active = False
        self.chunks = []

    def handle_starttag(self, tag, attrs):
        if tag == "script" and dict(attrs).get("id") == "__NEXT_DATA__":
            self.active = True

    def handle_data(self, data):
        if self.active:
            self.chunks.append(data)

    def handle_endtag(self, tag):
        if tag == "script" and self.active:
            self.active = False


def podcast_id(url: str) -> str:
    parsed = urlparse(url.strip())
    if parsed.scheme != "https" or parsed.hostname not in {"xiaoyuzhoufm.com", "www.xiaoyuzhoufm.com"}:
        raise ValueError("仅允许小宇宙官方 HTTPS 频道网址")
    parts = parsed.path.strip("/").split("/")
    if len(parts) != 2 or parts[0] != "podcast" or not ID_RE.fullmatch(parts[1]):
        raise ValueError("网址必须指向 /podcast/频道ID")
    return parts[1]


def as_iso_date(value):
    if value is None or value == "":
        return ""
    if isinstance(value, (int, float)) or (isinstance(value, str) and value.isdigit()):
        n = float(value)
        if n > 1e12:
            n /= 1000
        try:
            return datetime.fromtimestamp(n, timezone.utc).isoformat().replace("+00:00", "Z")
        except (OverflowError, OSError, ValueError):
            return ""
    return str(value)[:50]


def extract_podcast(html: str, expected_id: str):
    parser = NextDataParser()
    parser.feed(html)
    if not parser.chunks:
        raise ValueError("公开页面未包含 __NEXT_DATA__；可能网页结构发生变化")
    data = json.loads("".join(parser.chunks))
    podcast = data.get("props", {}).get("pageProps", {}).get("podcast")
    if not isinstance(podcast, dict):
        raise ValueError("未找到公开频道元数据")
    if podcast.get("pid") and podcast["pid"] != expected_id:
        raise ValueError("页面返回的频道 ID 与请求不匹配")
    items = podcast.get("episodes")
    if not isinstance(items, list) or not items:
        raise ValueError("公开页面没有可读取的单集，保留此前目录")
    episodes = []
    seen = set()
    for e in items:
        if not isinstance(e, dict):
            continue
        eid = str(e.get("eid", ""))
        if not ID_RE.fullmatch(eid) or eid in seen:
            continue
        seen.add(eid)
        episodes.append({
            "id": eid,
            "title": str(e.get("title") or "未命名单集")[:350],
            "url": "https://www.xiaoyuzhoufm.com/episode/" + eid,
            "published_at": as_iso_date(e.get("pubDate")),
            "duration_seconds": e.get("duration") if isinstance(e.get("duration"), (int, float)) else None,
        })
    if not episodes:
        raise ValueError("单集元数据未通过格式校验，保留此前目录")
    image = podcast.get("image") if isinstance(podcast.get("image"), dict) else {}
    return {
        "title": str(podcast.get("title") or "")[:180],
        "author": str(podcast.get("author") or "")[:150],
        "image": str(image.get("smallPicUrl") or ""),
        "episodes": episodes,
    }


def fetch_channel(pid: str):
    url = "https://www.xiaoyuzhoufm.com/podcast/" + pid
    headers = {"User-Agent": "Mozilla/5.0 (compatible; PersonalPodcastIndex/1.0)",
               "Accept": "text/html,application/xhtml+xml", "Accept-Language": "zh-CN,zh;q=0.9"}
    last_error = None
    for attempt in range(2):
        try:
            with urlopen(Request(url, headers=headers), timeout=25) as response:
                payload = response.read(8 * 1024 * 1024 + 1)
                if len(payload) > 8 * 1024 * 1024:
                    raise ValueError("页面超过 8MB 上限")
                return extract_podcast(payload.decode("utf-8", "replace"), pid)
        except (HTTPError, URLError, TimeoutError, ValueError, json.JSONDecodeError) as exc:
            last_error = exc
            if attempt == 0:
                time.sleep(2)
    raise RuntimeError(f"访问公开页面失败: {last_error}")


def load_json(path: Path, default: dict):
    if not path.exists():
        return default
    data = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"{path.name} 格式错误，不会覆盖旧数据")
    return data


def read_owner_issue(config: dict):
    if os.getenv("GITHUB_EVENT_NAME") != "issues":
        return False
    event_path = os.getenv("GITHUB_EVENT_PATH")
    if not event_path:
        raise ValueError("缺少 GitHub Issue 事件文件")
    issue = json.loads(Path(event_path).read_text(encoding="utf-8")).get("issue") or {}
    owner = os.getenv("GITHUB_REPOSITORY_OWNER", "")
    if not owner or (issue.get("user") or {}).get("login") != owner:
        print("忽略非仓库所有者的 Issue")
        return False
    if not str(issue.get("title", "")).startswith("[小宇宙收藏]"):
        print("忽略其他类别的 Issue")
        return False
    match = LINK_RE.search(str(issue.get("body") or ""))
    if not match:
        raise ValueError("收藏请求没有合法频道网址")
    pid = podcast_id(match.group(0).rstrip("/"))
    entries = config.setdefault("channels", [])
    if any(x.get("id") == pid for x in entries):
        print(f"频道已收藏，无需添加: {pid}")
        return False
    url = "https://www.xiaoyuzhoufm.com/podcast/" + pid
    entries.append({"id": pid, "url": url, "enabled": True})
    CHANNELS.write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"新增收藏: {url}")
    return True


def merge_channel(current: dict, fetched: dict, now: str, pid: str):
    known = {e["id"]: e for e in current.get("episodes", []) if isinstance(e, dict) and e.get("id")}
    added = 0
    for ep in fetched["episodes"]:
        if ep["id"] not in known:
            known[ep["id"]] = ep
            added += 1
        else:
            known[ep["id"]] = {**known[ep["id"]], **{k: v for k, v in ep.items() if v not in (None, "")}}
    current.update({
        "id": pid,
        "url": "https://www.xiaoyuzhoufm.com/podcast/" + pid,
        "title": fetched["title"] or current.get("title") or pid,
        "author": fetched["author"] or current.get("author", ""),
        "image": fetched["image"] or current.get("image", ""),
        "episodes": sorted(known.values(), key=lambda x: (x.get("published_at") or "", x.get("id") or ""), reverse=True),
        "last_checked_at": now,
        "last_success_at": now,
        "last_error": None,
        "consecutive_failures": 0,
    })
    return added


def main():
    now = datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
    config = load_json(CHANNELS, {"channels": []})
    if not isinstance(config.get("channels"), list):
        raise ValueError("channels.json 中 channels 必须是数组")
    read_owner_issue(config)
    catalog = load_json(DATA, {"schema": 1, "updated_at": None, "channels": {}})
    stored = catalog.setdefault("channels", {})
    if not isinstance(stored, dict):
        raise ValueError("episodes.json 的 channels 结构不合法")
    failures = 0
    enabled = set()
    for channel in config["channels"]:
        pid = podcast_id(channel.get("url", ""))
        if channel.get("id") != pid:
            raise ValueError(f"频道 ID 与 URL 不一致: {channel.get('id')}")
        if channel.get("enabled") is False:
            continue
        enabled.add(pid)
        current = stored.setdefault(pid, {"id": pid, "episodes": []})
        current["enabled"] = True
        try:
            fetched = fetch_channel(pid)
            added = merge_channel(current, fetched, now, pid)
            print(f"OK  {fetched['title']} 新增 {added} 期 / 现有 {len(current['episodes'])} 期")
        except Exception as exc:
            failures += 1
            current["url"] = "https://www.xiaoyuzhoufm.com/podcast/" + pid
            current["title"] = current.get("title") or channel.get("name") or pid
            current["last_checked_at"] = now
            current["last_error"] = str(exc)[:300]
            current["consecutive_failures"] = current.get("consecutive_failures", 0) + 1
            print(f"FAIL {pid}: {exc}", file=sys.stderr)
    for pid, current in stored.items():
        if pid not in enabled:
            current["enabled"] = False
    catalog.update({"schema": 1, "updated_at": now, "channels": stored})
    DATA.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"同步完成：{len(enabled)} 个频道，失败 {failures} 个")
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as e:
        print(f"无法同步: {e}", file=sys.stderr)
        sys.exit(1)
