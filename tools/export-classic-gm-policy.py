#!/usr/bin/env python3
"""Export the public classic identity catalog, without maps or runtime data."""
import argparse,hashlib,json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
 p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 c=json.loads((ROOT/'content/catalog.json').read_text())
 policy={'schema_version':1,'project':c['project'],'scope':c['scope'],
         'items':[{'id':x['Id'],'name':x['Name']}for x in c['items']],
         'skills':[{'id':x['MagID'],'name':x['MagName']}for x in c['skills']],
         'maps':[x['id']for x in c['maps']],'monsters':[x['Name']for x in c['monsters']],
         'monster_display_names':c['monster_display_names']}
 data=(json.dumps(policy,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
 a.output.parent.mkdir(parents=True,exist_ok=True)
 with a.output.open('xb')as f:f.write(data)
 print('Classic policy SHA256: '+hashlib.sha256(data).hexdigest())
if __name__=='__main__':main()
