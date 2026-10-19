import copy, unittest
from unittest.mock import patch
import history_verify as verifier
from history_apply import apply

PID='615a4035c8c1d14e83366946'; EID='615a4035c8c1d14e83366959'; URL='https://www.xiaoyuzhoufm.com/episode/'+EID
EP={'id':EID,'title':'000《孤岛车谈》是什么？','url':URL,'published_at':'2021-05-28T11:23:35Z','duration_seconds':3447}
OBS={'http_status':200,'final_url':URL,'id':EID,'channel_id':PID,'channel_title':'孤岛车谈','title':EP['title'],'published_at':'2021-05-28T11:23:35.000Z','duration_seconds':3447}
ROW={'status':'verified','id':EID,'url':URL,'expected_channel_id':PID,'expected_title':EP['title'],'observed':OBS}
CAT={'schema':1,'updated_at':'old','channels':{PID:{'id':PID,'episodes':[],'last_success_at':'unchanged'}}}

class VerifyTests(unittest.TestCase):
    def test_normalized_punctuation(self): self.assertEqual(verifier.norm('001 标题：测试！'),verifier.norm('001标题:测试'))
    def test_actual_page_match(self):
        with patch.object(verifier,'fetch',return_value=OBS): self.assertEqual(verifier.check((PID,EP))['status'],'verified')
    def test_wrong_channel_rejected(self):
        o={**OBS,'channel_id':'5e280faf418a84a0461fbd39'}
        with patch.object(verifier,'fetch',return_value=o): self.assertEqual(verifier.check((PID,EP))['status'],'mismatch')
    def test_wrong_title_rejected(self):
        with patch.object(verifier,'fetch',return_value={**OBS,'title':'另外一个节目'}): self.assertEqual(verifier.check((PID,EP))['status'],'mismatch')
    def test_unavailable_not_verified(self):
        with patch.object(verifier,'fetch',side_effect=ValueError('not found')): self.assertEqual(verifier.check((PID,EP))['status'],'unavailable')
    def test_add_preserves_metadata(self):
        result,changes=apply(CAT,[ROW]);self.assertEqual(len(changes),1);self.assertEqual(result['channels'][PID]['last_success_at'],'unchanged');self.assertEqual(CAT['channels'][PID]['episodes'],[])
    def test_repeat_is_idempotent(self):
        once,_=apply(CAT,[ROW]);twice,changes=apply(once,[ROW]);self.assertEqual(once,twice);self.assertEqual(changes,[])
    def test_existing_dates_preserved_when_same_instant(self):
        cat=copy.deepcopy(CAT);cat['channels'][PID]['episodes']=[EP];result,changes=apply(cat,[ROW]);self.assertEqual(result,cat);self.assertEqual(changes,[])
    def test_wrong_channel_cannot_merge(self):
        r={**ROW,'observed':{**OBS,'channel_id':'5e280faf418a84a0461fbd39'}}
        with self.assertRaises(ValueError):apply(CAT,[r])
    def test_unverified_skipped(self): self.assertEqual(apply(CAT,[{**ROW,'status':'unavailable'}]),(CAT,[]))
if __name__=='__main__':unittest.main()
