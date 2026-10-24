import tempfile
import unittest
from unittest.mock import MagicMock, patch
from yumi.paths import Paths
from yumi.unity_player import UnityPlayer


class UnityPlayerTests(unittest.TestCase):
    def test_only_owned_player_starts_once_and_closes(self):
        with tempfile.TemporaryDirectory(prefix='Player root spaces ') as tmp:
            paths=Paths(tmp)
            executable=paths.root/'Builds/YumiAvatar/YumiAvatar.exe'
            executable.parent.mkdir(parents=True);executable.touch()
            player=UnityPlayer(paths)
            process=MagicMock();process.poll.return_value=None
            with patch('yumi.unity_player.os.name','nt'), patch('yumi.unity_player.subprocess.STARTUPINFO',create=True), patch('yumi.unity_player.subprocess.STARTF_USESHOWWINDOW',1,create=True), patch('yumi.unity_player.subprocess.Popen',return_value=process) as launch:
                player.start();player.start()
                self.assertEqual(launch.call_count,1)
                args=launch.call_args.args[0]
                self.assertEqual(args[:3],[str(executable),'--yumi-root',str(paths.root)])
                self.assertNotIn('shell',launch.call_args.kwargs)
                player.close();player.close()
                process.terminate.assert_called_once();process.wait.assert_called_once()
                process.kill.assert_not_called()

    def test_missing_build_never_launches_an_unrelated_program(self):
        with tempfile.TemporaryDirectory() as tmp:
            player=UnityPlayer(Paths(tmp))
            with patch('yumi.unity_player.os.name','nt'), patch('yumi.unity_player.subprocess.Popen') as launch:
                with self.assertRaisesRegex(RuntimeError,'Build the original avatar'):player.start()
                launch.assert_not_called()
