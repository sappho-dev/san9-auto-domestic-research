#!/usr/bin/env python3
"""Build a separate title-only inventory; never change the clickable catalog."""
import argparse, csv, io, json
from pathlib import Path
from history_verify import norm

FIELDS = ['channel_id', 'channel_title', 'title', 'published_at',
          'duration_seconds', 'verification_status', 'official_eid',
          'official_url', 'title_source_type', 'title_source_url', 'checked_at']

def build(catalog, unresolved, evidence):
    proof = {r['id']: r for r in evidence}
    items = []
    for pid, ch in catalog['channels'].items():
        for e in ch['episodes']:
            r = proof[e['id']]
            items.append(dict(channel_id=pid, channel_title=ch['title'],
                title=e['title'], published_at=e['published_at'],
                duration_seconds=e['duration_seconds'], verification_status='official_verified',
                official_eid=e['id'], official_url=e['url'],
                title_source_type='official_episode_page', title_source_url=e['url'],
                checked_at=r['checked_at']))
        for e in unresolved.get(pid, {}).get('episodes', []):
            items.append(dict(channel_id=pid, channel_title=ch['title'],
                title=e['title'], published_at=e.get('published_at'),
                duration_seconds=e.get('duration_seconds'),
                verification_status='external_title_official_link_unconfirmed',
                official_eid=None, official_url=None,
                title_source_type='external_history_record', title_source_url=e.get('source_url'),
                checked_at=None))
    keys = [(r['channel_id'], norm(r['title'])) for r in items]
    if len(keys) != len(set(keys)): raise ValueError('duplicate normalized channel/title')
    official = [r['official_eid'] for r in items if r['official_eid']]
    if len(official) != len(set(official)): raise ValueError('duplicate official ID')
    items.sort(key=lambda r:(r['channel_id'], r['published_at'] or '', r['title']), reverse=True)
    return {'schema': 1, 'updated_at': catalog['updated_at'],
        'purpose': 'Display-ready title inventory, separate from episodes.json. Unconfirmed titles come from external history records and have no official link.',
        'total_titles': len(items), 'official_verified': len(official),
        'official_link_unconfirmed': len(items)-len(official), 'items': items}

def csv_text(inventory):
    out=io.StringIO(newline=''); writer=csv.DictWriter(out, fieldnames=FIELDS)
    writer.writeheader(); writer.writerows(inventory['items'])
    return '\ufeff'+out.getvalue()

def main():
    p=argparse.ArgumentParser(); p.add_argument('--directory',default=str(Path(__file__).resolve().parent)); a=p.parse_args(); d=Path(a.directory)
    catalog=json.loads((d/'episodes.json').read_text()); unresolved=json.loads((d/'history_unresolved.json').read_text())
    evidence=[json.loads(s) for s in (d/'history_verification.jsonl').read_text().splitlines() if s.strip()]
    inventory=build(catalog,unresolved,evidence)
    (d/'history_title_inventory.json').write_text(json.dumps(inventory,ensure_ascii=False,indent=2)+'\n')
    (d/'history_title_inventory.csv').write_text(csv_text(inventory),newline='')
    print(json.dumps({k:v for k,v in inventory.items() if k!='items'},ensure_ascii=False))

if __name__=='__main__': main()
