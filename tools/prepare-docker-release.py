#!/usr/bin/env python3
"""Assemble rebuilt binaries and content; never reads deployment/player data."""
import argparse,hashlib,json,re,shutil,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SERVICES=('LoginSrv','DBSrv','GameSrv','LoginGate','SelGate','GameGate')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--output',type=Path,required=True)
 ap.add_argument('--server-build',type=Path,default=ROOT/'.build/server')
 ap.add_argument('--host-build',type=Path,default=ROOT/'.build/host')
 ap.add_argument('--content',type=Path,default=ROOT/'.build/content')
 ap.add_argument('--map-resource-descriptor',type=Path,required=True,help='Separate Mir176Client server-maps.json descriptor; maps never enter the server archive')
 ap.add_argument('--component-version',help='Actual server component version when reusing a verified complete build')
 a=ap.parse_args();out=a.output
 version=a.component_version or (ROOT/'VERSION').read_text().strip()
 if not re.fullmatch(r'\d+\.\d+\.\d+',version):raise SystemExit('Invalid server component version')
 if out.exists() and any(out.iterdir()):raise SystemExit('Release output must be empty')
 out.mkdir(parents=True,exist_ok=True);content=a.content;built=a.server_build
 shared={}
 for name in SERVICES:
  source=built/name
  for required in (name+'.dll',name+'.deps.json',name+'.runtimeconfig.json','OpenMir2.dll'):
   if not(source/required).is_file():raise SystemExit('Missing complete build: '+name+'/'+required)
  target=out/'services'/name;shutil.copytree(source,target,ignore=shutil.ignore_patterns('*.pdb','Map'))
  shared[name]=digest(source/'OpenMir2.dll')
 plugin=built/'DBSvr.Storage.MySQL'
 if not(plugin/'DBSrv.Storage.MySQL.dll').is_file():raise SystemExit('Missing complete MySQL storage plugin')
 if digest(plugin/'OpenMir2.dll') not in shared.values() or len(set(shared.values()))!=1:raise SystemExit('Shared assemblies differ across rebuilt services')
 for file in plugin.glob('*'):
  if file.suffix in ('.dll','.json'):shutil.copy2(file,out/'services/DBSrv'/file.name)
 spec=importlib.util.spec_from_file_location('map_import',ROOT/'deployment/import-resources.py')
 helper=importlib.util.module_from_spec(spec);spec.loader.exec_module(helper)
 descriptor,map_files=helper.read_descriptor(a.map_resource_descriptor)
 actual={('Map/'+p.name):{'Path':'Map/'+p.name,'Size':p.stat().st_size,'Sha256':digest(p)} for p in (content/'GameSrv/Map').glob('*.map')}
 if actual!=map_files:raise SystemExit('Server content maps differ from the external resource descriptor')
 for directory in ('GameSrv',):
  source=content/directory
  shutil.copytree(source,out/'services'/directory,dirs_exist_ok=True,ignore=lambda current,names:['Map'] if Path(current)==source else [])
 shutil.copy2(a.map_resource_descriptor,out/'map-resources.json')
 shutil.copytree(content/'mysql-init',out/'mysql-init');shutil.copytree(a.host_build,out/'host',ignore=shutil.ignore_patterns('*.pdb'))
 shutil.copy2(content/'content-manifest.json',out/'content-manifest.json')
 for name in ('compose.yaml','compose.gm.example.yaml','Dockerfile','deploy.py','deploy.sh','import-resources.py','.dockerignore'):shutil.copy2(ROOT/'deployment'/name,out/name)
 shutil.copy2(ROOT/'tools/configure-classic-release.py',out/'configure.py')
 shutil.copy2(ROOT/'tools/configure-classic-gm.py',out/'configure-gm.py')
 shutil.copy2(ROOT/'docs/GM_TOOL.md',out/'GM_TOOL.md')
 shutil.copy2(ROOT/'deployment/README.md',out/'README.md')
 shutil.copy2(ROOT/'server/LICENSE',out/'LICENSE.txt')
 (out/'settings').mkdir()
 for name in ('catalog.json','level-experience.json'):
  shutil.copy2(ROOT/'content'/name,out/'settings'/name)
 if any(p.suffix.lower()=='.map' for p in out.rglob('*')):raise SystemExit('Map assets must remain in the independent resource package')
 manifest={str(p.relative_to(out)):digest(p) for p in sorted(out.rglob('*')) if p.is_file()}
 (out/'package-manifest.json').write_text(json.dumps({'project':'Mir2Classic176','version':version,'packaging_version':(ROOT/'VERSION').read_text().strip(),'status':'pending_runtime_validation','external_map_resources':descriptor['resource_set_id'],'shared_assembly_sha256':shared,'files':manifest},ensure_ascii=False,indent=2)+'\n')
 print(json.dumps({'services':6,'mysql_plugin':True,'shared_assemblies_equal':True,'files':len(manifest)}))
if __name__=='__main__':main()
