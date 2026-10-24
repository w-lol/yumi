"""Install a verified optional local profile; retain the existing model and settings."""
import argparse
import json
from pathlib import Path
import shutil
import sys
import threading
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from install_assets import download
from yumi.paths import Paths
from yumi.settings import Settings
from yumi.inference import LocalModel


def upgrade(root):
    paths=Paths(root)
    settings=Settings(paths)
    catalog=json.loads((paths.root/'assets/conversation-model.json').read_text(encoding='utf-8'))
    folder=paths.models/'chat-profiles'/catalog['profile']
    folder.mkdir(parents=True,exist_ok=True)
    needed=sum(e['bytes'] for e in catalog['files'] if not (folder/e['name']).exists())
    if shutil.disk_usage(folder).free<needed+512*1024**2:raise RuntimeError('Not enough space for the optional 8B profile; existing model was preserved.')
    for entry in catalog['files']:
        if Path(entry['name']).name!=entry['name']:raise ValueError('Invalid catalog filename')
        download(entry['url'],folder/entry['name'],entry['sha256'],entry['bytes'])
    backup=paths.backups/('conversation-settings-'+str(time.time_ns())+'.json')
    if settings.path.exists():shutil.copy2(settings.path,backup)
    original=dict(settings.data)
    settings.data.update(chat_profile=catalog['profile'],context=max(settings['context'],8192))
    model=LocalModel(paths,settings,lambda kind,data:print(kind,data if kind=='error' else '',flush=True))
    try:
        print('Testing the local 8B profile before activating it. CPU-only systems can take several minutes.',flush=True)
        model.start()
        answer=''.join(model.stream([{'role':'user','content':'Reply with one short greeting.'}],threading.Event(),max_tokens=32))
        if not answer.strip():raise RuntimeError('Local model returned no text')
        settings.save()
        print('Activated the verified local 8B profile. Original model retained. Settings backup:',backup,flush=True)
    except BaseException:
        settings.data=original
        raise
    finally:model.stop()


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path(__file__).resolve().parents[1])
    upgrade(parser.parse_args().root)
