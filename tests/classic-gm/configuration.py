#!/usr/bin/env python3
"""Offline provisioning regressions; temporary release fixtures only."""
import os,re,subprocess,sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
class Configuration(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory(prefix='classic-gm-config-');self.home=Path(self.temp.name)
  (self.home/'.secrets').mkdir();(self.home/'compose.yaml').write_text('services: {}\n')
  self.original='CLASSIC_LOGIN_PORT=17000\nCLASSIC_SELECT_PORT=17100\nCLASSIC_GAME_PORT=17200\n'
  (self.home/'.env').write_text(self.original)
 def tearDown(self):self.temp.cleanup()
 def run_config(self,port):return subprocess.run([sys.executable,str(ROOT/'tools/configure-classic-gm.py'),'--directory',str(self.home),'--port',str(port)],capture_output=True)
 def test_independent_secret_and_port(self):
  result=self.run_config(17300);self.assertEqual(result.returncode,0)
  token=self.home/'.secrets/gm_token';self.assertRegex(token.read_text(),r'^[0-9a-f]{64}\n$')
  self.assertEqual(token.stat().st_mode&0o777,0o600);self.assertEqual((self.home/'.env').read_text(),self.original+'CLASSIC_GM_PORT=17300\n')
  self.assertNotIn(token.read_bytes().strip(),result.stdout+result.stderr)
 def test_repeat_preserves_secret_and_configuration(self):
  self.assertEqual(self.run_config(17300).returncode,0);token=(self.home/'.secrets/gm_token').read_bytes();env=(self.home/'.env').read_bytes()
  self.assertNotEqual(self.run_config(17301).returncode,0);self.assertEqual((self.home/'.secrets/gm_token').read_bytes(),token);self.assertEqual((self.home/'.env').read_bytes(),env)
 def test_game_port_conflicts_make_no_changes(self):
  for port in (17000,17100,17200):self.assertNotEqual(self.run_config(port).returncode,0)
  self.assertFalse((self.home/'.secrets/gm_token').exists());self.assertEqual((self.home/'.env').read_text(),self.original)
 def test_invalid_ports_make_no_changes(self):
  for port in (0,65536,-1):self.assertNotEqual(self.run_config(port).returncode,0)
  self.assertFalse((self.home/'.secrets/gm_token').exists())
 def test_unconfigured_release_rejected(self):
  (self.home/'.env').unlink();self.assertNotEqual(self.run_config(17300).returncode,0);self.assertFalse((self.home/'.secrets/gm_token').exists())
if __name__=='__main__':unittest.main()
