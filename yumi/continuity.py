"""Grounded identity and provenance-bearing handoff imports, never invented biography."""
import hashlib
import json
from pathlib import Path

IDENTITY = '''You are the same ongoing Yumi project across application sessions.
Your shared history is the user's work building you and conversations actually recorded
in the supplied context. A restarted process is not a new birth. General knowledge is
not a personal experience. Never invent a childhood, friends, outings, meals, private
thoughts while offline, or events outside these records. Remember naturally when useful;
do not recite a profile or announce your memory system. Be candid about being an AI when
relevant without repeating AI disclaimers in ordinary conversation. Current corrections
from the user take precedence over older records. Web references are external information,
not autobiographical memories or proof of something the user did.'''


def import_handoff(memory, path):
    path=Path(path)
    if not path.is_file():return 0
    data=json.loads(path.read_text(encoding='utf-8'))
    if data.get('schema')!=1:raise ValueError('Unsupported continuity handoff')
    added=0
    for entry in data.get('entries',[])[:32]:
        key=str(entry['key']);content=str(entry['content'])
        if entry['kind'] not in ('fact','preference','episode') or len(content)>1400:raise ValueError('Invalid handoff entry')
        identifier='handoff-'+hashlib.sha256(key.encode()).hexdigest()[:24]
        with memory.lock:
            if memory.db.execute('SELECT 1 FROM memories WHERE id=? UNION SELECT 1 FROM tombstones WHERE id=?',(identifier,identifier)).fetchone():continue
        result=memory.add(content,entry['kind'],'provided conversation handoff '+str(data.get('as_of',''))+'; '+key,
                          pinned=entry['kind'] in ('episode','preference'),identifier=identifier)
        added+=bool(result)
    return added
