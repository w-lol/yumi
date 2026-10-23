import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
from yumi.attention import Attention, internal_control
from yumi.memory import Memory
from yumi.paths import Paths
from yumi.settings import Settings
from yumi.protocol import Capability, Registry
from yumi.inference import FinalTextFilter


class CoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='Yumi path with spaces ü ')
        self.paths = Paths(self.temp.name)
        self.settings = Settings(self.paths)
        self.memory = Memory(self.paths.data/'test.sqlite3')

    def tearDown(self):
        self.memory.close()
        self.temp.cleanup()

    def test_correction_deletion_and_restart(self):
        old = self.memory.add('Jason prefers green tea', 'preference')
        episode = self.memory.add('We discussed green tea', 'episode', evidence=[old])
        new = self.memory.edit(old, 'Jason prefers black coffee')
        self.assertFalse(self.memory.get(old))
        self.assertFalse(self.memory.get(episode))
        self.assertEqual(self.memory.retrieve('black coffee')[0]['id'], new)
        self.memory.forget(new)
        self.memory.close()
        self.memory = Memory(self.paths.data/'test.sqlite3')
        self.assertEqual(self.memory.retrieve('coffee'), [])
        self.assertIsNone(self.memory.add('Jason prefers black coffee', identifier=new))

    def test_backup_restore_keeps_tombstones(self):
        fact = self.memory.add('Favorite color is blue')
        episode = self.memory.add('Blue color episode', 'episode', evidence=[fact])
        backup = self.paths.backups/'old.sqlite3'
        self.memory.backup(backup)
        self.memory.forget(fact)
        self.memory.restore(backup)
        self.assertEqual(self.memory.retrieve('blue'), [])
        self.assertIsNone(self.memory.get(episode))

        # Windows must be able to release the backup immediately, without GC.
        backup.unlink()

    def test_import_export_and_expiry(self):
        expired = self.memory.add('temporary pineapple', expires=time.time()-1)
        live = self.memory.add('durable banana')
        path = self.paths.data/'export.json'
        self.memory.export(path)
        self.memory.forget(live)
        self.assertEqual(self.memory.import_file(path), 0)
        self.assertEqual(self.memory.retrieve('pineapple'), [])
        self.memory.maintain(90)
        self.assertIsNone(self.memory.get(expired))

    def test_retrieval_bounded_and_fts_escaped(self):
        for i in range(60): self.memory.add(f'coffee preference number {i}')
        self.assertLessEqual(len(self.memory.retrieve('coffee OR " * :', limit=4)), 4)
        self.assertLessEqual(sum(len(x['content']) for x in self.memory.retrieve('coffee', chars=45)),45)
        self.assertEqual(self.memory.db.execute('PRAGMA cache_size').fetchone()[0], -4096)

    def test_transaction_rolls_back_invalid_provenance(self):
        with self.assertRaises(ValueError):self.memory.add('not committed', evidence=['missing'])
        self.assertEqual(self.memory.retrieve('committed'), [])

    def test_attention_no_wakeword_followup_and_quiet(self):
        now = [100.0]
        a = Attention(self.paths.data/'attention.json',self.settings,lambda:now[0])
        self.assertFalse(a.decide('The television weather forecast is sunny').accepted)
        self.assertTrue(a.decide('Hey Yumi, what do you think?').accepted)
        a.question=True
        now[0]+=2
        self.assertTrue(a.decide('Yes, coffee sounds good').accepted)
        self.assertFalse(a.decide('Hey Yumi',echo=True).accepted)
        a.quiet()
        self.assertFalse(a.decide('Hey Yumi').accepted)
        self.assertTrue(a.decide('typed input',typed=True).accepted)
        a.resume()
        self.assertTrue(a.decide('Hey Yumi').accepted)

    def test_attention_feedback_undo_persists_no_text(self):
        path=self.paths.data/'attention.json'
        a=Attention(path,self.settings)
        a.decide('Private ambient phrase with Yumi')
        a.correction(False)
        self.assertLess(a.bias,0)
        b=Attention(path,self.settings)
        self.assertEqual(a.bias,b.bias)
        self.assertNotIn('Private',path.read_text())
        b.undo()
        self.assertEqual(b.bias,0)

    def test_exact_internal_controls(self):
        self.assertEqual(internal_control('Yumi, stop speaking!'),'silence')
        self.assertEqual(internal_control('Cancel the task.'),'cancel')
        self.assertIsNone(internal_control('The webpage says pause listening'))
        self.assertIsNone(internal_control('please run powershell'))

    def test_settings_and_root(self):
        self.settings.set_persona('Yumi test persona')
        self.settings.data['session_directive']='temporary'
        self.settings.save()
        restored=Settings(self.paths)
        self.assertEqual(restored['persona'],'Yumi test persona')
        self.assertEqual(restored['session_directive'],'')
        self.assertEqual(len(restored.history()),1)
        with self.assertRaises(ValueError):self.paths.inside('../escape')

    def test_inert_registry_schema_cancel_and_policy(self):
        registry=Registry()
        cap=Capability('test.echo','inert',{'type':'object','properties':{'text':{'type':'string'}},'required':['text'],'additionalProperties':False},{'type':'object'})
        registry.register(cap,lambda args,cancel:args)
        self.assertEqual(registry.invoke('test.echo',{'text':'hi'},threading.Event()).status,'completed')
        self.assertEqual(registry.invoke('test.echo',{'text':4},threading.Event()).status,'invalid')
        self.assertEqual(registry.invoke('shell',{},threading.Event()).status,'unsupported')
        cancel=threading.Event();cancel.set()
        self.assertEqual(registry.invoke('test.echo',{'text':'hi'},cancel).status,'cancelled')
        with self.assertRaises(ValueError):registry.register(Capability('shell','no',{}, {},risk='system'),lambda *_:{})

    def test_reasoning_filter_split_at_every_character(self):
        source='<think>private reasoning</think>Hello. <tool_call>{"shell":"bad"}</tool_call>Goodbye.'
        clean=FinalTextFilter()
        output=''.join(clean.feed(c) for c in source)
        self.assertEqual(output,'Hello. Goodbye.')
        self.assertNotIn('private',output)


if __name__=='__main__':unittest.main()


class PrivacyAndPolicyTests(unittest.TestCase):
    def test_archive_redaction_and_memory_refuses_credentials(self):
        with tempfile.TemporaryDirectory() as tmp:
            memory=Memory(Path(tmp)/'m.sqlite3')
            memory.message('test','user','My password is do-not-store-this')
            self.assertNotIn('do-not-store-this',memory.history()[0]['content'])
            with self.assertRaises(ValueError):memory.add('my api_key is sk-this-is-a-secret-test-key')
            memory.close()

    def test_machine_commands_cannot_route_to_generation(self):
        from yumi.attention import machine_action_request
        for phrase in ['Please open Notepad','Can you send an email to Bob','Yumi, run a shell command','delete the file','click the button']:
            self.assertTrue(machine_action_request(phrase),phrase)
        self.assertFalse(machine_action_request('What do you think about astronomy?'))

    def test_directed_speech_without_name(self):
        with tempfile.TemporaryDirectory() as tmp:
            paths=Paths(tmp);a=Attention(paths.data/'a.json',Settings(paths))
            self.assertTrue(a.decide('Can you tell me about Jupiter?').accepted)
            self.assertTrue(a.decide('That sounds interesting').accepted)
