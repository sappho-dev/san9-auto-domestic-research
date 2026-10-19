#!/usr/bin/env python3
"""Offline, additive-only catalog merge from actual official-page evidence.

Read a fresh remote catalog before each batch. Publish the result using the
GitHub branch-head lease, never force-push. This script does not publish.
"""
import argparse, copy, json, re
from datetime import datetime, timezone
from pathlib import Path
from history_verify import norm, CHANNEL_NAMES

def instant(value):
    d=datetime.fromisoformat(value.replace('Z','+00:00'))
    if d.tzinfo is None: raise ValueError('date must have timezone')
    return d

def apply(data, rows):
    if data.get('schema')!=1 or not isinstance(data.get('channels'),dict): raise ValueError('invalid catalog schema')
    result=copy.deepcopy(data); changes=[]
    for r in rows:
        o=r.get('observed',{}); pid=r.get('expected_channel_id'); eid=r.get('id','')
        variant=r.get('resolution',{}).get('match_rule')=='complete_title_after_number_prefix'
        if r.get('status')!='verified' and not variant: continue
        if not re.fullmatch('[a-f0-9]{24}',eid): raise ValueError('invalid episode ID')
        url='https://www.xiaoyuzhoufm.com/episode/'+eid
        if o.get('http_status')!=200 or r.get('url')!=url or o.get('final_url')!=url: raise ValueError('missing successful official-page evidence')
        if o.get('id')!=eid or o.get('channel_id')!=pid or o.get('channel_title')!=CHANNEL_NAMES.get(pid): raise ValueError('wrong episode or channel')
        if variant:
            expected=re.sub(r'^\d+\s*[✪.、]\s*','',r['expected_title'])
        else: expected=r['expected_title']
        if norm(expected)!=norm(o.get('title','')): raise ValueError('title mismatch')
        instant(o['published_at'])
        ch=result['channels'][pid]; old={e['id']:e for e in ch['episodes']}
        if len(old)!=len(ch['episodes']): raise ValueError('preexisting duplicate ID')
        if eid in old:
            if norm(old[eid]['title']) not in {norm(r['expected_title']),norm(o['title'])}: raise ValueError('concurrent title change: re-audit before replacing')
            ep=old[eid].copy()
        else:
            if any(norm(e['title'])==norm(o['title']) for e in old.values()): raise ValueError('different ID already has this title; investigate')
            ep={'id':eid}
        ep.update(title=o['title'],url=url,duration_seconds=o['duration_seconds'])
        # Existing RSS records use whole seconds. Keep that valid precision;
        # retain the exact official timestamp in the evidence ledger.
        if not ep.get('published_at') or int(instant(ep['published_at']).timestamp())!=int(instant(o['published_at']).timestamp()): ep['published_at']=o['published_at']
        if old.get(eid)!=ep: changes.append({'id':eid,'channel_id':pid,'action':'updated' if eid in old else 'added','before':old.get(eid),'after':ep})
        old[eid]=ep; ch['episodes']=sorted(old.values(),key=lambda e:(instant(e['published_at']),e['id']),reverse=True)
    if changes: result['updated_at']=datetime.now(timezone.utc).isoformat(timespec='seconds').replace('+00:00','Z')
    return result,changes

def main():
    p=argparse.ArgumentParser(); p.add_argument('--catalog',required=True);p.add_argument('--ledger',required=True);p.add_argument('--output',required=True);p.add_argument('--changes',required=True);a=p.parse_args()
    data=json.loads(Path(a.catalog).read_text());rows=[json.loads(l) for l in Path(a.ledger).read_text().splitlines() if l.strip()]
    result,changes=apply(data,rows)
    Path(a.output).write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n');Path(a.changes).write_text(json.dumps(changes,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps({'added':sum(c['action']=='added' for c in changes),'updated':sum(c['action']=='updated' for c in changes)},ensure_ascii=False))
if __name__=='__main__':main()
