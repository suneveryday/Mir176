import importlib.util,json,unittest
from pathlib import Path
root=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('builder',root/'tools/build-classic-content.py')
b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)
c=json.loads((root/'content/catalog.json').read_text())
class MerchantPolicy(unittest.TestCase):
 def test_non_durable_merchants_never_offer_repair(self):
  for npc in c['merchants']:
   if npc['service']=='shop' and set(npc['item_modes'])<={0,3,4,40}:
    services=b.merchant_services(npc)
    self.assertNotIn('repair',services,npc['name']);self.assertNotIn('s_repair',services,npc['name'])
 def test_explicit_repair_scope(self):
  for npc in c['merchants']:
   services=b.merchant_services(npc)
   if 's_repair' in services:
    self.assertIn('repair',services);self.assertTrue(any(w in npc['name'] for w in ['铁匠','修理']))
   if npc['service']=='shop':
    self.assertIn('sell',services);self.assertEqual(bool(npc['goods']),'buy' in services)
 def test_storage_and_upgrade_remain_separate(self):
  for npc in c['merchants']:
   if npc['service']!='shop':self.assertEqual([],b.merchant_services(npc))
if __name__=='__main__':unittest.main()
