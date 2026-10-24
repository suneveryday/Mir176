#!/usr/bin/env python3
"""Create new project-local secrets and a private Compose environment offline."""
import argparse,os,re,secrets
from pathlib import Path
ap=argparse.ArgumentParser();ap.add_argument('--address',required=True);ap.add_argument('--login-port',type=int,required=True);ap.add_argument('--select-port',type=int,required=True);ap.add_argument('--game-port',type=int,required=True);ap.add_argument('--directory',type=Path,default=Path(__file__).resolve().parent);a=ap.parse_args()
if not re.fullmatch(r'[A-Za-z0-9.-]{1,30}',a.address):ap.error('Address must fit the legacy client address field')
ports=[a.login_port,a.select_port,a.game_port]
if len(set(ports))!=3 or any(not 1<=p<=65535 for p in ports):ap.error('Choose three different unused ports')
r=a.directory
if not (r/'compose.yaml').is_file():ap.error('Run against an assembled release directory')
if (r/'.env').exists():ap.error('Private configuration already exists; review it instead of replacing secrets')
d=r/'.secrets';d.mkdir(mode=0o700,exist_ok=False)
for name in ('db_password','db_root_password'):
 p=d/name
 with p.open('x') as f:f.write(secrets.token_hex(32)+'\n')
 p.chmod(0o600)
e=r/'.env'
with e.open('x') as f:f.write(f'CLASSIC_PUBLIC_ADDRESS={a.address}\nCLASSIC_LOGIN_PORT={a.login_port}\nCLASSIC_SELECT_PORT={a.select_port}\nCLASSIC_GAME_PORT={a.game_port}\n')
e.chmod(0o600)
print('Created independent private configuration. No containers were started.')
