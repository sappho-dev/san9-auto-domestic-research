"""Offline compatibility checks for the unchanged daily updater."""
import copy, json, os, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
import update

PID='615a4035c8c1d14e83366946'
OLD={'id':'615a4035c8c1d14e83366959','title':'000《孤岛车谈》是什么？','url':'https://www.xiaoyuzhoufm.com/episode/615a4035c8c1d14e83366959','published_at':'2021-05-28T11:23:35Z','duration_seconds':3447}
NEW={'id':'6ad4f1079554aaac00a10ca4','title':'212 召回','url':'https://www.xiaoyuzhoufm.com/episode/6ad4f1079554aaac00a10ca4','published_at':'2026-10-18T15:41:27Z','duration_seconds':5601}

class CompatibilityTests(unittest.TestCase):
    def test_recent_sync_preserves_history(self):
        current={'episodes':[copy.deepcopy(OLD)]}; fetched={'title':'孤岛车谈','author':'测试','image':'','episodes':[NEW]}
        self.assertEqual(update.merge_channel(current,fetched,'2026-10-19T00:00:00Z',PID),1)
        self.assertEqual({e['id'] for e in current['episodes']},{OLD['id'],NEW['id']})
    def test_repeated_sync_does_not_duplicate(self):
        current={'episodes':[copy.deepcopy(OLD)]};fetched={'title':'孤岛车谈','author':'测试','image':'','episodes':[NEW]}
        update.merge_channel(current,fetched,'2026-10-19T00:00:00Z',PID)
        self.assertEqual(update.merge_channel(current,fetched,'2026-10-19T00:01:00Z',PID),0)
        self.assertEqual(len(current['episodes']),2)
    def test_owner_can_add_channel_and_repeat_is_safe(self):
        with tempfile.TemporaryDirectory() as tmp:
            event=Path(tmp)/'event.json';channel=Path(tmp)/'channels.json';event.write_text(json.dumps({'issue':{'user':{'login':'sappho-dev'},'title':'[小宇宙收藏] 测试','body':'https://www.xiaoyuzhoufm.com/podcast/'+PID}}))
            config={'channels':[]}
            with patch.dict(os.environ,{'GITHUB_EVENT_NAME':'issues','GITHUB_EVENT_PATH':str(event),'GITHUB_REPOSITORY_OWNER':'sappho-dev'}),patch.object(update,'CHANNELS',channel):
                self.assertTrue(update.read_owner_issue(config));self.assertFalse(update.read_owner_issue(config));self.assertEqual(len(config['channels']),1)
                self.assertEqual(json.loads(channel.read_text())['channels'][0]['id'],PID)
    def test_nonowner_cannot_add_channel(self):
        with tempfile.TemporaryDirectory() as tmp:
            event=Path(tmp)/'event.json';event.write_text(json.dumps({'issue':{'user':{'login':'other'},'title':'[小宇宙收藏] 测试','body':'https://www.xiaoyuzhoufm.com/podcast/'+PID}}))
            config={'channels':[]}
            with patch.dict(os.environ,{'GITHUB_EVENT_NAME':'issues','GITHUB_EVENT_PATH':str(event),'GITHUB_REPOSITORY_OWNER':'sappho-dev'}):self.assertFalse(update.read_owner_issue(config))
            self.assertEqual(config['channels'],[])
if __name__=='__main__':unittest.main()
