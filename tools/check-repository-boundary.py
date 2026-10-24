#!/usr/bin/env python3
"""Keep PC client source and recovered resources out of the main publication."""
from pathlib import Path
import argparse,re,subprocess,sys

ROOT=Path(__file__).resolve().parents[1]

def forbidden(name):
    if name.startswith(('client/','resources/')):
        return name not in {'client/README.md','resources/README.md'}
    return (Path(name).suffix.lower() in {'.pas','.dfm','.wil','.wix','.map','.wav'}
            or name.startswith('provenance/client-build.')
            or name in {'provenance/upstream-client-provenance.json','provenance/client-resources.candidate.json'})

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--history',action='store_true')
    parser.add_argument('--ref',help='Exact commit or annotated tag object being pushed')
    args=parser.parse_args()
    if args.ref and not re.fullmatch(r'[0-9a-fA-F]{40}',args.ref):
        raise SystemExit('FAIL invalid publication object')
    history=args.history or args.ref is not None
    if history:
        refs=[args.ref] if args.ref else ['HEAD','--branches','--tags']
        if args.ref:
            subprocess.check_output(['git','-C',str(ROOT),'rev-parse','--verify',args.ref+'^{commit}'],stderr=subprocess.PIPE)
        revisions=subprocess.check_output(['git','-C',str(ROOT),'rev-list',*refs,'--']).decode().splitlines()
        paths=set()
        for revision in revisions:
            paths.update(subprocess.check_output(['git','-C',str(ROOT),'ls-tree','-r','-z','--name-only',revision]).decode().split('\0'))
    else:
        paths=set(subprocess.check_output(['git','-C',str(ROOT),'ls-files','-z']).decode().split('\0'))
    if any(forbidden(name) for name in paths if name):
        raise SystemExit('FAIL client/resource boundary: use the independent private Mir176Client repository')
    print('PASS main repository client/resource boundary'+(' including all published history' if history else ' (staged files)'))

if __name__=='__main__':
    main()
