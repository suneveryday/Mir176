#!/usr/bin/env python3
import json,re,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
C=json.loads((ROOT/'content/catalog.json').read_text())
class ClassicContent(unittest.TestCase):
 def test_classic_book_and_skill_identity(self):
  skills={x['MagName'] for x in C['skills']};books={x['Name'] for x in C['items'] if x['StdMode']==4}
  self.assertEqual(33,len(skills));self.assertEqual(skills,books)
  self.assertFalse(skills&{'灭天火','寒冰掌','无极真气','气功波','狮子吼'})
 def test_sparse_ids_keep_upstream_identity(self):
  items={x['Id']:x['Name'] for x in C['items']}
  self.assertEqual('天魔神甲',items[115]);self.assertEqual('圣战宝甲',items[116]);self.assertNotIn(500,items)
  self.assertTrue(max(items)>len(items));self.assertEqual(len(items),len(C['items']))
 def test_heavy_armor_acquisition_and_routes(self):
  mappings={'暗之双头血魔':('T140','天魔神甲'),'暗之骷髅精灵':('T339','圣战宝甲'),'暗之双头金刚':('T219','天尊道袍'),'暗之沃玛教主':('T232','天师长袍'),'暗之黄泉教主':('T315','法神披风'),'暗之虹魔教主':('T218','霓裳羽衣')}
  for monster,(map_id,item) in mappings.items():
   self.assertTrue(any(s['map']==map_id and s['monster']==monster for s in C['spawns']))
   self.assertTrue(any(d['item']==item for d in C['drops'][monster]))
   self.assertTrue(any(r['to']==map_id for r in C['routes']))
 def test_references_and_probabilities(self):
  maps={x['id']:x for x in C['maps']};monsters={x['Name'] for x in C['monsters']};items={x['Name'] for x in C['items']}|{'金币'}
  self.assertFalse(any(x['title'].startswith(('幻境','圣域')) for x in maps.values()));self.assertIn('D616',maps)
  for r in C['routes']:
   for key,x,y in [(r['from'],r['x'],r['y']),(r['to'],r['tx'],r['ty'])]:
    self.assertTrue(0<=x<maps[key]['width'] and 0<=y<maps[key]['height'])
  for row in C['spawns']:self.assertIn(row['monster'],monsters);self.assertIn(row['map'],maps)
  for name,rows in C['drops'].items():
   self.assertIn(name,monsters)
   for row in rows:self.assertIn(row['item'],items);self.assertTrue(0<row['numerator']<=row['denominator'])
 def test_progression_and_no_login_boost(self):
  rules=json.loads((ROOT/'content/level-experience.json').read_text());xp=rules['experience_to_next_level']
  self.assertEqual(4000000,xp['35']);self.assertNotIn('48',xp)
  host=(ROOT/'server/ClassicHost/Program.cs').read_text();self.assertIn('TestLevel=1\\nTestGold=0',host)
 def test_audited_scripts(self):
  for merchant in C['merchants']:
   self.assertIn(merchant['service'],{'storage','shop','weapon-upgrade'})
   self.assertNotIn(merchant['name'],{'彩票员','月卡使者'})
  for file in (ROOT/'.build/content/GameSrv/Envir').rglob('*.txt'):
   text=file.read_bytes().decode('gb18030')
   self.assertFalse(re.search(r'(?i)autohunt|充值|月卡|假人|gmexecute',text),file.name)
if __name__=='__main__':unittest.main()
