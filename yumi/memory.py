"""Disk-backed, bounded retrieval with provenance and durable deletion tombstones."""
from __future__ import annotations
import json
import re
import sqlite3
import threading
import time
import uuid
from pathlib import Path
from .privacy import redact, contains_secret


class Memory:
    def __init__(self, path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.lock = threading.RLock()
        self.encoder = None
        self.max_bytes = 1024 * 1024 * 1024
        self.db = sqlite3.connect(self.path, check_same_thread=False)
        self.db.row_factory = sqlite3.Row
        self.db.executescript('''
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA cache_size=-4096;
            PRAGMA secure_delete=ON;
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT);
            INSERT OR IGNORE INTO meta VALUES('schema','1');
            CREATE TABLE IF NOT EXISTS memories(
                id TEXT PRIMARY KEY, kind TEXT NOT NULL, content TEXT NOT NULL,
                source TEXT NOT NULL, created REAL NOT NULL, updated REAL NOT NULL,
                confidence REAL NOT NULL, privacy TEXT NOT NULL DEFAULT 'local',
                expires REAL, supersedes TEXT, pinned INTEGER DEFAULT 0);
            CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(id UNINDEXED,content);
            CREATE TABLE IF NOT EXISTS links(derived TEXT,source TEXT,PRIMARY KEY(derived,source));
            CREATE TABLE IF NOT EXISTS tombstones(id TEXT PRIMARY KEY,deleted REAL);
            CREATE TABLE IF NOT EXISTS messages(
                id TEXT PRIMARY KEY,session TEXT,role TEXT,content TEXT,created REAL);
            CREATE INDEX IF NOT EXISTS message_session ON messages(session,created);
        ''')
        if self.db.execute("SELECT value FROM meta WHERE key='schema'").fetchone()[0] not in ('1','2'):
            raise ValueError('Unsupported memory schema')
        self._semantic_schema()

    def add(self, content, kind='fact', source='explicit user', confidence=1.0,
            expires=None, supersedes=None, pinned=False, identifier=None, evidence=()):
        self._check_capacity()
        if contains_secret(content):raise ValueError('Do not store credentials as conversational memory')
        if not content.strip() or len(content) > 16000:
            raise ValueError('Memory must contain 1–16000 characters')
        if kind not in ('fact', 'preference', 'episode', 'inference', 'task', 'observation'):
            raise ValueError('Invalid memory type')
        identifier = identifier or uuid.uuid4().hex
        timestamp = time.time()
        with self.lock, self.db:
            if self.db.execute('SELECT 1 FROM tombstones WHERE id=?', (identifier,)).fetchone():
                return None
            if supersedes:
                self._forget(supersedes)
            self.db.execute('INSERT INTO memories VALUES(?,?,?,?,?,?,?,?,?,?,?)',
                            (identifier, kind, content.strip(), source, timestamp, timestamp,
                             max(0, min(1, confidence)), 'local', expires, supersedes, int(pinned)))
            self.db.execute('INSERT INTO search VALUES(?,?)', (identifier, content.strip()))
            for parent in evidence:
                if not self.get(parent):
                    raise ValueError('Unknown source memory')
                self.db.execute('INSERT INTO links VALUES(?,?)', (identifier, parent))
        return identifier

    def _forget(self, identifier, visited=None):
        visited = visited if visited is not None else set()
        if identifier in visited:
            return
        visited.add(identifier)
        children = self.db.execute('SELECT derived FROM links WHERE source=?', (identifier,)).fetchall()
        for row in children:
            self._forget(row[0], visited)
        source_row=self.db.execute('SELECT source FROM memories WHERE id=?',(identifier,)).fetchone()
        if source_row and source_row[0].startswith('message:'):
            self.db.execute('DELETE FROM messages WHERE id=?',(source_row[0][8:],))
        self.db.execute('INSERT OR REPLACE INTO tombstones VALUES(?,?)', (identifier, time.time()))
        self.db.execute('DELETE FROM search WHERE id=?', (identifier,))
        self.db.execute('DELETE FROM vectors WHERE id=?', (identifier,))
        self.db.execute('DELETE FROM buckets WHERE id=?', (identifier,))
        self.db.execute('DELETE FROM memories WHERE id=?', (identifier,))
        self.db.execute('DELETE FROM links WHERE derived=? OR source=?', (identifier, identifier))

    def forget(self, identifier):
        with self.lock, self.db:
            self._forget(identifier)
        self.checkpoint()
        if self.encoder:self.encoder.clear()

    def edit(self, identifier, content):
        old = self.get(identifier)
        if not old:
            raise ValueError('Memory no longer exists')
        return self.add(content, old['kind'], 'explicit correction', supersedes=identifier,
                        pinned=bool(old['pinned']))

    def get(self, identifier):
        with self.lock:
            row = self.db.execute('SELECT * FROM memories WHERE id=?', (identifier,)).fetchone()
            return dict(row) if row else None

    def pin(self, identifier, value):
        with self.lock, self.db:
            self.db.execute('UPDATE memories SET pinned=?,updated=? WHERE id=?', (int(value), time.time(), identifier))

    def retrieve(self, query, limit=8, chars=6000):
        semantic = self._semantic_candidates(query) if query and self.encoder else []
        words = list(dict.fromkeys(re.findall(r'\w{2,}', query.lower())))[:24]
        with self.lock:
            if words:
                expression = ' OR '.join('"' + w + '"' for w in words)
                rows = self.db.execute('''SELECT m.*,bm25(search) AS score FROM search
                    JOIN memories m ON m.id=search.id WHERE search MATCH ?
                    AND (expires IS NULL OR expires>?) ORDER BY pinned DESC,score,updated DESC LIMIT ?''',
                    (expression, time.time(), min(limit, 100))).fetchall()
            else:
                rows = self.db.execute('''SELECT * FROM memories WHERE expires IS NULL OR expires>?
                    ORDER BY pinned DESC,updated DESC LIMIT ?''', (time.time(), min(limit, 100))).fetchall()
        # Reciprocal-rank fusion keeps lexical and semantic relevance comparable.
        ranked, scores = {}, {}
        for sequence in ([dict(row) for row in rows], semantic):
            for rank, row in enumerate(sequence):
                ranked[row['id']] = row
                scores[row['id']] = scores.get(row['id'],0) + 1/(30+rank) + (0.02 if row['pinned'] else 0)
        rows = [ranked[key] for key in sorted(scores,key=scores.get,reverse=True)[:min(limit,100)]]
        result, remaining = [], min(chars, 100000)
        for row in rows:
            if len(row['content']) <= remaining:
                result.append(dict(row))
                remaining -= len(row['content'])
        return result

    def message(self, session, role, content):
        self._check_capacity()
        identifier=uuid.uuid4().hex
        with self.lock, self.db:
            self.db.execute('INSERT INTO messages VALUES(?,?,?,?,?)',
                            (identifier, session, role, redact(content[:16000]), time.time()))
        return identifier

    def history(self, query='', limit=100):
        with self.lock:
            rows = self.db.execute('SELECT * FROM messages WHERE content LIKE ? ORDER BY created DESC LIMIT ?',
                                   ('%' + query + '%', min(limit, 500))).fetchall()
            return [dict(r) for r in rows]

    def clear_history(self):
        with self.lock, self.db:
            self.db.execute('DELETE FROM messages')
        self.checkpoint()

    def maintain(self, retention_days):
        with self.lock, self.db:
            expired = self.db.execute('SELECT id FROM memories WHERE expires<? LIMIT 100', (time.time(),)).fetchall()
            for row in expired:
                self._forget(row[0])
            self.db.execute('DELETE FROM messages WHERE created<?', (time.time() - retention_days * 86400,))

    def export(self, path):
        with self.lock:
            rows = [dict(r) for r in self.db.execute('SELECT * FROM memories')]
            links = [dict(r) for r in self.db.execute('SELECT * FROM links')]
        from .paths import atomic_json
        atomic_json(Path(path), {'schema': 1, 'memories': rows, 'links': links})

    def import_file(self, path):
        if Path(path).stat().st_size > 20 * 1024 * 1024:
            raise ValueError('Import exceeds 20 MB')
        data = json.loads(Path(path).read_text(encoding='utf-8'))
        if data.get('schema') != 1 or len(data.get('memories', [])) > 10000:
            raise ValueError('Unsupported import')
        count = 0
        for row in data['memories']:
            if not self.get(row['id']):
                result = self.add(row['content'], row['kind'], row['source'], row['confidence'],
                                  row.get('expires'), pinned=row.get('pinned', False), identifier=row['id'])
                count += bool(result)
        with self.lock, self.db:
            for link in data.get('links', []):
                if self.get(link['derived']) and self.get(link['source']):
                    self.db.execute('INSERT OR IGNORE INTO links VALUES(?,?)', (link['derived'], link['source']))
        return count

    def backup(self, path):
        with self.lock, sqlite3.connect(path) as dest:
            self.db.backup(dest)

    def restore(self, path):
        with self.lock:
            tombstones = list(self.db.execute('SELECT id,deleted FROM tombstones'))
            with sqlite3.connect(f'file:{Path(path).resolve().as_posix()}?mode=ro', uri=True) as source:
                if source.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                    raise ValueError('Backup failed integrity check')
                if source.execute("SELECT value FROM meta WHERE key='schema'").fetchone()[0] not in ('1','2'):
                    raise ValueError('Unsupported backup')
                source.backup(self.db)
            self._semantic_schema()
            if self.encoder:self.encoder.clear()
            with self.db:
                for identifier, _ in tombstones:
                    self._forget(identifier)
            self.checkpoint()

    def storage_bytes(self):
        return sum(p.stat().st_size for p in (self.path, Path(str(self.path)+'-wal')) if p.exists())

    def _check_capacity(self):
        if self.storage_bytes() >= self.max_bytes:
            raise RuntimeError('Memory storage limit reached. Export or delete records, or increase the limit; no important facts were discarded.')

    def _semantic_schema(self):
        self.db.executescript("""
            CREATE TABLE IF NOT EXISTS vectors(id TEXT PRIMARY KEY,version TEXT,embedding BLOB);
            CREATE TABLE IF NOT EXISTS buckets(id TEXT,part INTEGER,value INTEGER,PRIMARY KEY(id,part));
            CREATE INDEX IF NOT EXISTS bucket_lookup ON buckets(part,value);
            UPDATE meta SET value='2' WHERE key='schema';
        """)

    def index_batch(self, limit=4):
        if not self.encoder:return 0
        with self.lock:
            rows=self.db.execute("""SELECT m.id,m.content FROM memories m LEFT JOIN vectors v ON m.id=v.id
                WHERE v.id IS NULL OR v.version!=? LIMIT ?""",(self.encoder.version,limit)).fetchall()
        count=0
        for row in rows:
            vector=self.encoder.encode(row['content'])
            buckets=self.encoder.buckets(vector)
            with self.lock,self.db:
                if not self.get(row['id']):continue
                self.db.execute('INSERT OR REPLACE INTO vectors VALUES(?,?,?)',(row['id'],self.encoder.version,vector.tobytes()))
                self.db.execute('DELETE FROM buckets WHERE id=?',(row['id'],))
                self.db.executemany('INSERT INTO buckets VALUES(?,?,?)',[(row['id'],i,v) for i,v in enumerate(buckets)])
                count+=1
        return count

    def _semantic_candidates(self, query):
        import numpy as np
        vector=self.encoder.encode(query)
        keys=self.encoder.buckets(vector)
        condition=' OR '.join('(part=? AND value IN (?,?,?,?,?,?,?,?,?))' for _ in keys)
        arguments=[]
        for part,key in enumerate(keys):arguments.extend([part,key]+[key^(1<<bit) for bit in range(8)])
        with self.lock:
            rows=self.db.execute(f"""SELECT m.*,v.embedding FROM memories m JOIN vectors v ON m.id=v.id
                WHERE v.version=? AND (m.expires IS NULL OR m.expires>?) AND m.id IN
                (SELECT id FROM buckets WHERE {condition} GROUP BY id ORDER BY COUNT(*) DESC LIMIT 256)
                LIMIT 256""",[self.encoder.version,time.time()]+arguments).fetchall()
        scored=[]
        for row in rows:
            embedded=np.frombuffer(row['embedding'],dtype=np.float32)
            if embedded.shape!=vector.shape:continue
            score=float(embedded @ vector)
            if score>=0.35:
                record=dict(row);record.pop('embedding');scored.append((score,record))
        return [r for _,r in sorted(scored,key=lambda pair:pair[0],reverse=True)[:16]]

    def checkpoint(self):
        with self.lock:
            self.db.execute('PRAGMA wal_checkpoint(TRUNCATE)')

    def close(self):
        self.checkpoint()
        self.db.close()
