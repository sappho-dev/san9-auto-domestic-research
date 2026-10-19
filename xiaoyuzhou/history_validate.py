#!/usr/bin/env python3
"""Validate catalog entries against official-page evidence; no network or writes."""
import argparse, json, re
from collections import Counter
from pathlib import Path
from history_verify import norm, CHANNEL_NAMES
from history_apply import instant

def validate(catalog, rows, archive):
    errors=[]; all_ids=[]; totals={}; evidence={r['id']:r for r in rows}
    if catalog.get('schema')!=1 or not isinstance(catalog.get('channels'),dict): raise ValueError('unsupported schema')
    for pid,ch in catalog['channels'].items():
        eps=ch['episodes']; ids=[e['id'] for e in eps]; all_ids+=ids
        if len(ids)!=len(set(ids)):errors.append({'channel':pid,'reason':'duplicate ID'})
        if len({norm(e['title']) for e in eps})!=len(eps):errors.append({'channel':pid,'reason':'duplicate normalized title'})
        verified=0
        for e in eps:
            eid=e['id'];url='https://www.xiaoyuzhoufm.com/episode/'+eid;row=evidence.get(eid,{});o=row.get('observed',{})
            checks={'id_format':bool(re.fullmatch('[a-f0-9]{24}',eid)),'canonical_url':e['url']==url,'official_http_200':o.get('http_status')==200 and o.get('final_url')==url,'official_id':o.get('id')==eid,'official_channel':o.get('channel_id')==pid and o.get('channel_title')==CHANNEL_NAMES.get(pid),'official_title':norm(o.get('title',''))==norm(e['title']),'official_duration':o.get('duration_seconds')==e['duration_seconds']}
            try:checks['official_publication_second']=int(instant(o['published_at']).timestamp())==int(instant(e['published_at']).timestamp())
            except (KeyError,ValueError,TypeError):checks['official_publication_second']=False
            failed=[k for k,v in checks.items() if not v]
            if failed:errors.append({'id':eid,'channel':pid,'failed':failed})
            else:verified+=1
        expected=sorted(eps,key=lambda e:(instant(e['published_at']),e['id']),reverse=True)
        if eps!=expected:errors.append({'channel':pid,'reason':'publication sorting'})
        a=archive.get('channels',{}).get(pid);remaining=[]
        if a:
            remaining=[e['title'] for e in a['episodes'] if not e.get('xiaoyuzhou_eid')]
            if a['count']!=len(a['episodes']):errors.append({'channel':pid,'reason':'archive count'})
            if a['linked_to_native_count']!=len(a['episodes'])-len(remaining):errors.append({'channel':pid,'reason':'archive linkage count'})
            for item in a['episodes']:
                linked=item.get('xiaoyuzhou_eid')
                if not linked:continue
                row=evidence.get(linked,{})
                possible={norm(row.get('expected_title','')),norm(row.get('observed',{}).get('title',''))}
                if linked not in ids or norm(item['title']) not in possible:errors.append({'channel':pid,'reason':'invalid archive title linkage','title':item['title']})
        totals[pid]={'title':ch['title'],'catalog_count':len(eps),'official_page_verified':verified,'archive_remaining':len(remaining),'all_catalog_entries_verified':verified==len(eps)}
    if len(all_ids)!=len(set(all_ids)):errors.append({'reason':'same ID in multiple channels'})
    return {'catalog_quality_pass':not errors,'catalog_total':len(all_ids),'official_page_verified_total':sum(c['official_page_verified'] for c in totals.values()),'all_archive_targets_linked':all(c['archive_remaining']==0 for c in totals.values()),'channels':totals,'errors':errors,'publication_precision':'Existing whole-second RSS timestamps are retained when equal to official timestamp to the second; exact official timestamps are stored in the evidence ledger.'}

def main():
    p=argparse.ArgumentParser();p.add_argument('--catalog',default='episodes.json');p.add_argument('--ledger',default='history_verification.jsonl');p.add_argument('--archive',default='history_external.json');a=p.parse_args()
    result=validate(json.loads(Path(a.catalog).read_text()),[json.loads(l) for l in Path(a.ledger).read_text().splitlines() if l.strip()],json.loads(Path(a.archive).read_text()))
    print(json.dumps(result,ensure_ascii=False,indent=2));raise SystemExit(0 if result['catalog_quality_pass'] else 1)
if __name__=='__main__':main()
