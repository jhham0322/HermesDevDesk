"""Metadata-only public payload checks; never print matching secret content."""
import argparse,hashlib,json,re,subprocess
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--app',type=Path,required=True);p.add_argument('--source',type=Path,required=True);a=p.parse_args()
forbidden_names={'auth.json','credentials.json','state.json','state.json.backup','.env','cookies','cookies-journal','login data','local state'}
forbidden_parts={'browser-profile','secrets','engine-home','mcp-home','local-app-data','.codex','.aws'}
forbidden_extensions={'.pfx','.p12','.pem','.key','.dpapi','.pdb','.jpg','.jpeg','.webp','.gif'}
secret=re.compile(rb'(?:gh[pousr]_[A-Za-z0-9]{24,}|github_pat_[A-Za-z0-9_]{35,}|sk-proj-[A-Za-z0-9_-]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)')
issues=[];checked=0
for f in a.app.rglob('*'):
 if not f.is_file():continue
 rel=f.relative_to(a.app);lower={x.lower() for x in rel.parts}
 if f.name.lower() in forbidden_names or f.name.lower().startswith('.env.') or lower&forbidden_parts or f.suffix.lower() in forbidden_extensions:issues.append(str(rel))
 if f.suffix.lower() in {'.json','.md','.txt','.xml','.config','.ps1','.yaml','.yml'} and f.stat().st_size<5_000_000:
  data=f.read_bytes()
  if secret.search(data) or re.search(rb'[A-Za-z]:\\\\Users\\\\[^\\\\]+', data) is not None:issues.append(str(rel)+': sensitive-pattern')
 checked+=1
manifest={}
for line in (a.app/'.hermes-manifest.tsv').read_text(encoding='utf-8-sig').splitlines():
 digest,name=line.split('\t');manifest[name.replace('\\','/')]=digest
actual={f.relative_to(a.app).as_posix():f for f in a.app.rglob('*') if f.is_file() and f.name!='.hermes-manifest.tsv'}
if set(manifest)!=set(actual):issues.append('manifest-file-set')
for name,f in actual.items():
 if hashlib.sha256(f.read_bytes()).hexdigest()!=manifest.get(name):issues.append(name+': hash-mismatch')
tracked=subprocess.run(['git','-C',str(a.source),'ls-files','--cached','--others','--exclude-standard'],capture_output=True,text=True,check=True).stdout.splitlines()
for name in tracked:
 f=a.source/name
 if not f.is_file():continue
 rel=Path(name);lower={x.lower() for x in rel.parts}
 if f.name.lower() in forbidden_names or lower&forbidden_parts or f.suffix.lower() in {'.pfx','.p12','.pem','.key','.dpapi','.pdb','.log'}:issues.append(name)
 if f.suffix.lower() in {'.cs','.ps1','.py','.yml','.yaml','.json','.md','.props','.csproj','.xaml'}:
  data=f.read_bytes()
  if secret.search(data) or re.search(rb'[A-Za-z]:\\\\Users\\\\[^\\\\]+', data) is not None:issues.append(name+': sensitive-pattern')
print(json.dumps({'payloadFilesChecked':checked,'sourceFilesChecked':len(tracked),'issues':sorted(set(issues))}))
raise SystemExit(bool(issues))
