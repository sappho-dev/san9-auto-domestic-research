#!/usr/bin/env python3
"""Read public official episode pages; store minimal provenance, never audio."""
import argparse, concurrent.futures, gzip, hashlib, json, re, threading, time, unicodedata
from datetime import datetime, timezone
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError

ROOT = Path(__file__).resolve().parent
CHANNEL_NAMES = {'615a4035c8c1d14e83366946': '孤岛车谈', '5e280faf418a84a0461fbd39': '疯投圈', '61358d971c5d56efe5bcb5d2': '乱翻书'}
lock = threading.Lock()
last_request = 0.0

def norm(s):
    return ''.join(c for c in unicodedata.normalize('NFKC', s).casefold() if c.isalnum())

def fetch(eid):
    global last_request
    if not re.fullmatch(r'[a-f0-9]{24}', eid): raise ValueError('invalid episode ID')
    url = 'https://www.xiaoyuzhoufm.com/episode/' + eid
    with lock:
        time.sleep(max(0, 1.2 - (time.monotonic() - last_request)))
        last_request = time.monotonic()
    req = Request(url, headers={'User-Agent': 'Mozilla/5.0 (compatible; PublicPodcastMetadataAudit/1.0)', 'Accept': 'text/html'})
    with urlopen(req, timeout=35) as r:
        payload = r.read(8_000_001)
        if len(payload) > 8_000_000: raise ValueError('page exceeds 8MB')
        status, final_url = r.status, r.url
    if payload[:2] == b'\x1f\x8b': payload = gzip.decompress(payload)
    html = payload.decode('utf-8')
    m = re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', html, re.S)
    if not m: raise ValueError('official page has no structured metadata')
    props = json.loads(m.group(1))['props']['pageProps']
    ep = props.get('episode')
    if not ep: raise ValueError('official page has no episode; statusCode=' + str(props.get('statusCode')))
    return {'http_status': status, 'final_url': final_url, 'page_sha256': hashlib.sha256(payload).hexdigest(),
            'id': ep.get('eid'), 'channel_id': ep.get('pid'), 'channel_title': ep.get('podcast', {}).get('title'),
            'title': ep.get('title'), 'published_at': ep.get('pubDate'), 'duration_seconds': ep.get('duration'),
            'linked_episode_ids': sorted(set(re.findall(r'xiaoyuzhoufm\.com/episode/([a-f0-9]{24})', html)) - {eid})}

def check(item):
    pid, ep = item
    result = {'checked_at': datetime.now(timezone.utc).isoformat(timespec='seconds').replace('+00:00','Z'),
              'expected_channel_id': pid, 'expected_title': ep['title'], 'id': ep['id'], 'url': ep['url']}
    try:
        observed = fetch(ep['id'])
        result['observed'] = observed
        result['status'] = ('verified' if observed['id'] == ep['id'] and observed['channel_id'] == pid and
                            observed['channel_title'] == CHANNEL_NAMES.get(pid) and
                            norm(observed['title']) == norm(ep['title']) else 'mismatch')
    except Exception as exc:
        result.update(status='unavailable', error=type(exc).__name__ + ': ' + str(exc))
    return result

def main():
    p=argparse.ArgumentParser(); p.add_argument('--catalog', default=str(ROOT/'episodes.json')); p.add_argument('--output',default=str(ROOT/'history_verification.jsonl')); p.add_argument('--workers',type=int,choices=(1,2),default=2); a=p.parse_args()
    data=json.loads(Path(a.catalog).read_text()); out=Path(a.output)
    done={json.loads(l)['id'] for l in out.read_text().splitlines()} if out.exists() else set()
    items=[(pid,ep) for pid,ch in data['channels'].items() for ep in reversed(ch['episodes']) if ep['id'] not in done]
    with concurrent.futures.ThreadPoolExecutor(max_workers=a.workers) as pool, out.open('a') as f:
        for r in pool.map(check,items):
            f.write(json.dumps(r,ensure_ascii=False)+'\n'); f.flush(); print(r['status'],r['id'],r['expected_title'],r.get('error',''),flush=True)
if __name__=='__main__': main()
