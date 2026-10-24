#!/usr/bin/env python3
"""Check source distributions before commit/push; never print matched values."""
from pathlib import Path
import json,re,subprocess,sys
ROOT=Path(__file__).resolve().parents[1]
RULES={
 'deployment-platform':re.compile(rb"(?<![A-Za-z0-9])n"+rb"as(?![A-Za-z0-9])|syno"+rb"logy",re.I),
 'private-network':re.compile(rb"(?<![0-9])(?:192[.]168[.]|10[.][0-9]{1,3}[.]|172[.](?:1[6-9]|2[0-9]|3[01])[.])[0-9]{1,3}[.][0-9]{1,3}(?![0-9])"),
 'personal-workspace':re.compile(rb"/(?:Us"+rb"ers|Vol"+rb"umes)/[A-Za-z0-9_]|/volume[0-9]+/|\b[DE]:[\\/]"),
 'private-document':re.compile(("no"+"tion").encode(),re.I),
 'private-key':re.compile(rb"-----BEGIN (?:OPENSSH|RSA|EC|DSA|ENCRYPTED)? ?PRIVATE KEY-----"),
 'access-token':re.compile(rb"gh[pousr]_[A-Za-z0-9]{25,}|github_pat_[A-Za-z0-9_]{30,}"),
}

PUBLICATION_IDENTITIES={
    (b'Mir2Classic176 Maintainers',b'maintainers@mir2classic176.invalid'),
    (b'GitHub',b'noreply@github.com'),
}

def git_output(args,**kwargs):
    return subprocess.check_output(args,stderr=subprocess.PIPE,**kwargs)

def known_local_rules():
    raw=git_output(['git','-C',str(ROOT),'rev-parse','--git-path','privacy-known-values.json'],text=True).strip()
    path=Path(raw)
    if not path.is_absolute():path=ROOT/path
    if not path.is_file():return []
    try:
        entries=json.loads(path.read_text())
        if not isinstance(entries,list):raise ValueError
        result=[]
        for entry in entries:
            if isinstance(entry,str):
                entry={'value':entry,'scope':'all' if len(entry)>=6 else 'identity'}
            if (not isinstance(entry,dict) or not isinstance(entry.get('value'),str)
                or not entry['value'] or entry.get('scope','all') not in {'all','identity'}):
                raise ValueError
            result.append((entry['value'],entry.get('scope','all')))
        return result
    except (ValueError,UnicodeError):
        raise ValueError('invalid private registry; values withheld') from None
try:LOCAL_RULES=known_local_rules()
except (ValueError,OSError,subprocess.CalledProcessError):
    print('FAIL private registry could not be loaded; details withheld',file=sys.stderr)
    raise SystemExit(1) from None
# Compatibility for importers; identity-only short words must not scan source.
LOCAL_VALUES=[value.encode() for value,scope in LOCAL_RULES if scope=='all']

def known_value_overlap():
    return max((len(value.encode(encoding))-1 for value,scope in LOCAL_RULES if scope=='all'
                for encoding in ('utf-8','utf-16le','utf-16be')),default=0)

def known_value_reasons(data,identity=False):
    for value,scope in LOCAL_RULES:
        if scope=='identity' and not identity:continue
        for encoding in ('utf-8','utf-16le','utf-16be'):
            if re.search(re.escape(value.encode(encoding)),data,re.I):
                return ['known-private-value']
        if not value.isascii():
            for encoding in ('utf-8','utf-16le','utf-16be'):
                for alignment in ((0,) if encoding=='utf-8' else (0,1)):
                    if value.casefold() in data[alignment:].decode(encoding,errors='ignore').casefold():
                        return ['known-private-value']
    return []

def identity_reasons(data):
    match=re.fullmatch(rb'(.*?) <([^<>]*)> -?\d+ [+-]\d{4}',data)
    if not match:return ['invalid-publication-identity']
    result=known_value_reasons(data,identity=True)
    if match.groups() not in PUBLICATION_IDENTITIES:
        result.append('unapproved-publication-identity')
    return result

def metadata_reasons(data,kind):
    header,_,message=data.partition(b'\n\n');result=[]
    for field in ((b'tagger',) if kind==b'tag' else (b'author',b'committer')):
        values=[line[len(field)+1:] for line in header.splitlines() if line.startswith(field+b' ')]
        result.extend(identity_reasons(values[0]) if len(values)==1 else ['invalid-publication-identity'])
    if kind==b'tag':
        for line in header.splitlines():
            if line.startswith(b'tag '):result.extend(known_value_reasons(line[4:]))
    result.extend(inspect('',message))
    return sorted(set(result))

def inspect(name,data):
 result=[]
 result.extend(known_value_reasons(data))
 if re.search(r"(^|/)(private|access|\.secrets)(/|$)|(^|/)\.env($|\.)|credentials\.json$",name):result.append('private-file')
 views=[data]
 if b'\0' in data:
  views.extend(data[offset:].decode(encoding,errors='ignore').encode()
               for encoding in ('utf-16le','utf-16be') for offset in (0,1))
 for label,pattern in RULES.items():
  if any(pattern.search(view) for view in views):result.append(label)
 return result

def main():
 history='--history' in sys.argv or '--ref' in sys.argv
 if '--ref' in sys.argv:
  index=sys.argv.index('--ref')+1
  if index>=len(sys.argv) or sys.argv[index].startswith('-'):raise ValueError('invalid publication revision')
 refs=([sys.argv[sys.argv.index('--ref')+1]] if '--ref' in sys.argv else ['--branches','--tags']) if history else ['HEAD']
 if history:
  rows=git_output(['git','-C',str(ROOT),'rev-list','--objects',*refs]).splitlines()
  objects=[r.partition(b' ')[::2] for r in rows]
  if '--ref' not in sys.argv:
   rows=git_output(['git','-C',str(ROOT),'for-each-ref','--format=%(objectname) %(refname)','refs/tags']).splitlines()
   objects.extend(r.split(b' ',1) for r in rows)
  else:
   oid=git_output(['git','-C',str(ROOT),'rev-parse','--verify',refs[0]]).strip()
   objects.append((oid,b''))
 else:
  rows=git_output(['git','-C',str(ROOT),'ls-files','--stage','-z']).split(b'\0')
  objects=[(r.split(b'\t',1)[0].split()[1],r.split(b'\t',1)[1]) for r in rows if r]
 failures=[];unique={};trees=set()
 for oid,name in objects:unique.setdefault(oid,set()).add(name)
 if not history:
  for field in ('GIT_AUTHOR_IDENT','GIT_COMMITTER_IDENT'):
   value=git_output(['git','-C',str(ROOT),'var',field]).strip()
   failures.extend(('prospective-identity',reason) for reason in identity_reasons(value))
 if unique:
  ids=list(unique)
  metadata=git_output(['git','-C',str(ROOT),'cat-file','--batch-check'],input=b''.join(x+b'\n' for x in ids))
  selected=[row.split()[0] for row in metadata.splitlines() if row.split()[1] in (b'blob',b'commit',b'tag')]
  payload=git_output(['git','-C',str(ROOT),'cat-file','--batch'],input=b''.join(x+b'\n' for x in selected));offset=0
  for oid in selected:
   end=payload.index(b'\n',offset);_,kind,raw_size=payload[offset:end].split();size=int(raw_size)
   data=payload[end+1:end+1+size];offset=end+size+2
   reasons=[]
   if kind==b'blob':
    for name in unique[oid]:
     reasons.extend(inspect(name.decode(errors='replace'),data));reasons.extend(known_value_reasons(name))
   else:
    reasons.extend(metadata_reasons(data,kind))
    if kind==b'commit':trees.add(data.split(b'\n',1)[0].split()[1])
   failures.extend((oid.decode(),reason) for reason in set(reasons))
 if history:
  # rev-list emits only one pathname for duplicate blobs. Enumerate each source
  # tree so aliases, renames and identical files cannot hide private filenames.
  for tree in trees:
   rows=git_output(['git','-C',str(ROOT),'ls-tree','-r','-z',tree.decode()]).split(b'\0')
   for row in rows:
    if not row:continue
    meta,name=row.split(b'\t',1);oid=meta.split()[2].decode()
    for reason in set(inspect(name.decode(errors='replace'),b'')+known_value_reasons(name)):
     failures.append((oid,reason))
  rows=(git_output(['git','-C',str(ROOT),'for-each-ref','--format=%(objectname) %(refname)','refs/heads','refs/tags']).splitlines()
        if '--ref' not in sys.argv else [])
  for row in rows:
   oid,name=row.split(b' ',1)
   for reason in known_value_reasons(name):failures.append((oid.decode(),reason))
 if failures:
  for identifier,reason in sorted(set(failures)):print(f'FAIL {reason}: object {identifier}',file=sys.stderr)
  return 1
 print('PASS publication privacy scan'+(' including Git history and identities' if history else ' (staged files and prospective identities)'))
 return 0
if __name__=='__main__':
 try:sys.exit(main())
 except (ValueError,OSError,subprocess.CalledProcessError):
  print('FAIL privacy scan could not complete; details withheld',file=sys.stderr);sys.exit(1)
