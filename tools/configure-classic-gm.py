#!/usr/bin/env python3
"""Provision an optional independent GM secret offline; never starts services."""
import argparse,os,re,secrets
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--directory',type=Path,required=True);p.add_argument('--port',type=int,required=True);a=p.parse_args()
r=a.directory;env=r/'.env';token=r/'.secrets/gm_token'
if not (r/'compose.yaml').is_file() or not env.is_file():p.error('Configure the independent release first')
if not 1<=a.port<=65535:p.error('Invalid port')
text=env.read_text();ports=[int(x)for x in re.findall(r'^CLASSIC_(?:LOGIN|SELECT|GAME)_PORT=(\d+)\s*$',text,re.M)]
if a.port in ports or 'CLASSIC_GM_PORT='in text or token.exists():p.error('GM configuration exists or port conflicts; retain existing secrets')
temp=r/'.env.gm.tmp'
try:
 with token.open('x')as f:f.write(secrets.token_hex(32)+'\n')
 token.chmod(0o600)
 with temp.open('x')as f:f.write(text.rstrip()+'\nCLASSIC_GM_PORT='+str(a.port)+'\n')
 temp.chmod(0o600);os.replace(temp,env)
except BaseException:
 if temp.exists():temp.unlink()
 # Keep a newly generated secret on partial failure for manual review.
 raise
print('Optional classic GM secret provisioned. No service started or changed.')
