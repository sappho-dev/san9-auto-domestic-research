import unittest
from history_title_inventory import build,csv_text

class InventoryTests(unittest.TestCase):
    def setUp(self):
        self.catalog={'updated_at':'2026-10-19T00:00:00Z','channels':{'p':{'title':'频道','episodes':[{'id':'e','title':'已核实','url':'https://www.xiaoyuzhoufm.com/episode/e','published_at':'2026-10-18T00:00:00Z','duration_seconds':60}]}}}
        self.unresolved={'p':{'episodes':[{'title':'待核实','published_at':'2026-10-17T00:00:00Z','duration_seconds':61,'source_url':'https://example.com/title'}]}}
        self.evidence=[{'id':'e','checked_at':'2026-10-19T00:00:00Z'}]
    def test_unconfirmed_never_gets_official_link(self):
        v=build(self.catalog,self.unresolved,self.evidence); self.assertEqual((v['total_titles'],v['official_verified'],v['official_link_unconfirmed']),(2,1,1)); pending=v['items'][1]; self.assertIsNone(pending['official_url']); self.assertIsNone(pending['official_eid']); self.assertEqual(pending['title_source_url'],'https://example.com/title')
    def test_csv_and_no_catalog_mutation(self):
        import copy
        original=copy.deepcopy(self.catalog); v=build(self.catalog,self.unresolved,self.evidence); self.assertTrue(csv_text(v).startswith('\ufeffchannel_id,')); self.assertEqual(self.catalog,original)
    def test_duplicate_title_rejected(self):
        self.unresolved['p']['episodes'][0]['title']='已核实';
        with self.assertRaises(ValueError): build(self.catalog,self.unresolved,self.evidence)

if __name__=='__main__': unittest.main()
