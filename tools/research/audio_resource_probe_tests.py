"""Independent literal synthetic oracles for the research seam (MIT).

No original payloads. Tests exercise the real opt-in helpers and CLI; these
are not production audio, execution of the original or hardware/codec certification.
"""
import contextlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

import audio_resource_probe as p
import dat_probe as d


def chunk(tag, data):
    return tag + struct.pack('<I', len(data)) + data + (b'\0' if len(data) % 2 else b'')


def wave(extra=b''):
    body = b'WAVE' + chunk(b'fmt ', bytes.fromhex('01000100112b00002256000002001000'))
    body += extra + chunk(b'data', b'\0' * 8)
    return b'RIFF' + struct.pack('<I', len(body)) + body


def sector(version=0xaa0004, roof=1, lights=0, scripts=0):
    body = struct.pack('<i', lights) + bytes(48 * max(0, lights)) + bytes(16384)
    body += struct.pack('<i', roof) + (bytes(1024) if roof == 0 else b'')
    body += struct.pack('<i', version)
    if version > 0xaa0000:
        body += struct.pack('<i', scripts) + bytes(24 * max(0, scripts))
    if version >= 0xaa0002:
        body += bytes(12)
    if version >= 0xaa0003:
        body += bytes(12) + bytes.fromhex('000000080100000065000000')
    if version >= 0xaa0004:
        body += bytes(512)
    return body + bytes(4)


class ProbeTests(unittest.TestCase):
    def reject(self, code, action):
        with self.assertRaises(p.AudioProbeError) as caught:
            action()
        self.assertEqual(str(caught.exception), code)

    def test_path_normalization_and_negative_matrix(self):
        self.assertEqual(p.resource_key('Sound\\Fixture.WAV'), 'sound/fixture.wav')
        for name, code in [('../a', 'TraversalPath'), ('sound/../a', 'TraversalPath'),
                           ('sound//a', 'TraversalPath'), ('sound/./a', 'TraversalPath'),
                           ('/a', 'AbsolutePath'), ('C:\\a', 'AbsolutePath'),
                           ('a\0b', 'PathEncoding'), ('é.wav', 'PathEncoding'),
                           ('x' * 241, 'PathLimit'), ('', 'PathLimit')]:
            with self.subTest(name=name):
                self.reject(code, lambda: p.resource_key(name))

    def test_mes_identity_and_negative_matrix(self):
        self.assertEqual(p.parse_mes(b'// {8}{ignored}\n{9}{fixture.wav}\n{10}{other.wav}'),
                         {9: 'fixture.wav', 10: 'other.wav'})
        self.assertEqual(p.parse_mes(b'\xa7 unknown prose\n{9}{fixture.wav}'), {9: 'fixture.wav'})
        self.assertEqual(p.parse_mes(b'/* ignored {1}{bad} */{9}{a/*literal*/b}'), {9: 'a/*literal*/b'})
        self.reject('MalformedTableComment', lambda: p.parse_mes(b'/* unterminated {9}{x}'))
        self.reject('TableEntryLimit', lambda: p.parse_mes(b''.join(f'{{{i}}}{{x}}'.encode() for i in range(4097))))
        for data, code in [(b'{1}{x}{1}{y}', 'DuplicateTableId'), (b'{x}{y}', 'TableKey'),
                           (b'{1}{x', 'MalformedTable'), (b'{1}{x}}', 'MalformedTable'),
                           (b'{2147483648}{x}', 'TableFieldLimit'), (b'{1}{\xff}', 'TableEncodingUnknown'),
                           (b'x' * 65537, 'TableLimit'), (b'{1}{' + b'x'*1025 + b'}', 'TableFieldLimit'),
                           (b'no records', 'EmptyTable')]:
            with self.subTest(code=code):
                self.reject(code, lambda: p.parse_mes(data))

    def test_sfx_order_user_fallback_missing_and_traversal(self):
        self.assertEqual(p.resolve_sfx(9, [{9: 'first.wav'}, {9: 'second.wav'}], {9: 'user.wav'}), 'sound/first.wav')
        self.assertEqual(p.resolve_sfx(10, [{9: 'first.wav'}], {10: 'user.wav'}), 'sound/user.wav')
        self.assertIsNone(p.resolve_sfx(11, [{}], {}))
        self.reject('TraversalPath', lambda: p.resolve_sfx(9, [{9: '../bad.wav'}], {}))

    def test_speech_literal_identity_fallback_and_missing(self):
        # Literal 0x002a0011 binds directory42/line17, independent of producer packing.
        self.assertEqual(p.speech_candidates(0x002a0011, False),
                         ('sound/speech/00042/v17_f.mp3', 'sound/speech/00042/v17_m.mp3'))
        self.assertEqual(p.speech_candidates(0x7fffffff, True), ('sound/speech/32767/v65535_m.mp3',))
        available = {'sound/speech/00042/v17_m.mp3'}
        self.assertEqual(p.resolve_speech(0x002a0011, False, available), 'sound/speech/00042/v17_m.mp3')
        self.assertIsNone(p.resolve_speech(0x002a0011, True, set()))
        self.assertEqual(p.speech_candidates(-1, False), ())
        self.assertEqual(p.speech_candidates(0, True), ('sound/speech/00000/v0_m.mp3',))
        for identity in (-2, 0x80000000, True):
            self.reject('SpeechIdentity', lambda: p.speech_candidates(identity, True))

    def test_scheme_literal_metadata(self):
        entry = p.parse_scheme('#9 /freq:10 /time:5-19 /vol:20,30 /bal:40,60')
        self.assertEqual(entry, p.SchemeEntry('#9', 'ambient', (5, 19), 10, (20, 30), (40, 60)))
        self.assertEqual(p.parse_scheme('music/fixture.mp3 /loop /time:5-19 /vol:50'),
                         p.SchemeEntry('music/fixture.mp3', 'loop', (5, 19), 5, (50, 50), (50, 50)))
        self.assertEqual(p.parse_scheme('fixture.wav /scatter:21').volume_percent, (79, 100))
        self.assertEqual(p.parse_scheme('fixture.wav /scatter:21').balance_percent, (40, 60))
        self.assertEqual(p.parse_scheme('fixture.mp3 /anchor').mode, 'anchor')
        self.assertEqual(p.parse_scheme('fixture.mp3 /over').mode, 'over')

    def test_scheme_hour_and_frequency_literal_oracles(self):
        entry = p.parse_scheme('#9 /freq:10 /time:5-19')
        for hour, draw, expected in [(4, 0, False), (5, 9, True), (5, 10, False), (19, 9, True), (20, 0, False)]:
            self.assertEqual(entry.eligible(hour, draw), expected)
        self.assertFalse(p.parse_scheme('#9 /freq:0').eligible(12, 0))
        self.assertTrue(p.parse_scheme('#9 /freq:1000').eligible(12, 999))
        self.reject('HourRange', lambda: entry.eligible(24, 0))
        self.reject('AudioDrawRange', lambda: entry.eligible(12, 1000))

    def test_scheme_index_holes_zero_limits_unknowns(self):
        index, listing = {7: 'synthetic #100'}, {100: '#9 /freq:10', 102: 'fixture.mp3 /loop'}
        self.assertEqual([i for i, _ in p.scheme_entries(7, index, listing)], [100, 102])
        self.assertEqual(p.scheme_entries(0, {}, {}), [])
        self.reject('MissingSchemeId', lambda: p.scheme_entries(8, index, listing))
        self.reject('SchemeTextLimit', lambda: p.parse_scheme(' '))
        self.reject('SchemeBase', lambda: p.scheme_entries(7, {7: 'no base'}, listing))
        self.reject('SchemeSoundLimit', lambda: p.scheme_entries(7, index, {i: '#9' for i in range(100, 126)}))
        for text, code in [('x /time:22-4', 'UnsupportedHourWindow'), ('x /freq:1001', 'FrequencyRange'),
                           ('x /vol:101', 'PercentRange'), ('x /loop /over', 'AmbiguousSchemeMode'),
                           ('x /freq:1 /freq:2', 'DuplicateSchemeOption'), ('x /unknown', 'UnsupportedSchemeOption'),
                           ('x /loop /freq:1', 'UnsupportedSongOption'), ('x /over /time:5', 'UnsupportedSongTime'),
                           ('#x', 'SchemeReference'), ('#0', 'SchemeReference'),
                           ('#2147483648', 'SchemeReference'), ('../x', 'TraversalPath')]:
            self.reject(code, lambda: p.parse_scheme(text))

    def test_wav_literal_header_unknown_chunks_and_loops(self):
        expected = {'codec': 'PCM16', 'channels': 1, 'sample_rate': 11025, 'alignment': 2,
                    'data_bytes': 8, 'frames': 4, 'smpl_loops': [], 'proof': 'header-only; not decoded or played'}
        self.assertEqual(p.wav_header(wave(chunk(b'JUNK', b'abc'))), expected)
        smpl = bytes(28) + struct.pack('<II', 1, 0) + struct.pack('<6I', 9, 0, 1, 3, 0, 0)
        result = p.wav_header(wave(chunk(b'smpl', smpl)))
        self.assertEqual(result['smpl_loops'], [{'type': 0, 'start': 1, 'end_inclusive': 3, 'fraction': 0, 'repeat': 0}])

    def test_wav_negative_matrix(self):
        good = wave()
        bad_format = bytearray(good); bad_format[20] = 3
        bad_geometry = bytearray(good); bad_geometry[28] = 0
        for data, code in [(good[:-1], 'WavRiffLength'), (b'bad' * 4, 'WavSignature'),
                           (wave(chunk(b'fmt ', bytes(16))), 'WavFormatChunk'),
                           (bytes(bad_format), 'UnsupportedWavClass'), (bytes(bad_geometry), 'WavPcmGeometry'),
                           (wave(chunk(b'data', bytes(2))), 'DuplicateWavData'),
                           (wave(chunk(b'smpl', bytes(35))), 'WavSmplHeader')]:
            self.reject(code, lambda: p.wav_header(data))
        bad_loop = bytes(28) + struct.pack('<II', 1, 0) + struct.pack('<6I', 0, 0, 0, 4, 0, 0)
        self.reject('WavLoopBounds', lambda: p.wav_header(wave(chunk(b'smpl', bad_loop))))
        self.reject('WavLoopLimit', lambda: p.wav_header(wave(chunk(b'smpl', bytes(28) + struct.pack('<II', 17, 0)))))
        # Repeated smpl chunks cannot each spend the entire aggregate loop budget.
        sixteen_loops = bytes(28) + struct.pack('<II', 16, 0) + struct.pack('<6I', 0, 0, 0, 3, 0, 0) * 16
        one_loop = bytes(28) + struct.pack('<II', 1, 0) + struct.pack('<6I', 0, 0, 0, 3, 0, 0)
        self.assertEqual(len(p.wav_header(wave(chunk(b'smpl', sixteen_loops)))['smpl_loops']), 16)
        self.reject('WavLoopLimit', lambda: p.wav_header(wave(chunk(b'smpl', sixteen_loops) + chunk(b'smpl', one_loop))))
        self.reject('WavChunkLimit', lambda: p.wav_header(wave(chunk(b'JUNK', b'') * 127)))
        body = b'WAVE' + b'JUNK' + struct.pack('<I', 0xffffffff)
        self.reject('WavChunkBounds', lambda: p.wav_header(b'RIFF' + struct.pack('<I', len(body)) + body))

    def test_mp3_literal_frames_id3_and_negative_matrix(self):
        frame = bytes.fromhex('fffb90c0') + bytes(413)
        result = p.mp3_header(frame * 2)
        self.assertEqual({k: result[k] for k in ('mpeg_version', 'layer', 'sample_rate', 'bitrate_kbps', 'channels', 'first_frame_offset')},
                         {'mpeg_version': '1', 'layer': 3, 'sample_rate': 44100, 'bitrate_kbps': 128, 'channels': 1, 'first_frame_offset': 0})
        self.assertEqual(p.mp3_header(b'ID3\x03\0\0\0\0\0\0' + frame*2)['first_frame_offset'], 10)
        for data, code in [(frame, 'NoBoundedMp3FramePair'), (bytes(40), 'NoBoundedMp3FramePair'),
                           (bytes(65537), 'Mp3PrefixLimit'), (b'ID3\x02\0\0\0\0\0\0', 'UnsupportedId3'),
                           (b'ID3\x03\0\0\x80\0\0\0', 'Id3Synchsafe'),
                           (b'ID3\x04\0\0\x7f\x7f\x7f\x7f', 'Id3BeyondPrefix')]:
            self.reject(code, lambda: p.mp3_header(data))

    def test_sector_literal_framing_and_negative_matrix(self):
        self.assertEqual(p.sector_sound(sector()), {'version': 0xaa0004, 'offset': 16424, 'bytes': 12,
                         'flags_raw': 0x08000000, 'music': 1, 'ambient': 101})
        self.assertEqual(p.sector_sound(sector(roof=0, lights=2, scripts=3))['offset'], 17616)
        for version in (0xaa0000, 0xaa0001, 0xaa0002):
            self.assertIsNone(p.sector_sound(sector(version))['sound'])
        self.assertEqual(p.sector_sound(sector(0xaa0003))['offset'], 16424)
        self.reject('SectorLightLimit', lambda: p.sector_sound(struct.pack('<i', -1)))
        self.reject('SectorTruncated', lambda: p.sector_sound(sector()[:16435]))
        self.reject('SectorRoof', lambda: p.sector_sound(sector(roof=2)))
        self.reject('SectorVersion', lambda: p.sector_sound(sector(0xaa0005)))
        bad = bytearray(sector()); struct.pack_into('<i', bad, 16396, 4097)
        self.reject('SectorScriptLimit', lambda: p.sector_sound(bytes(bad)))

    def test_listener_camera_not_actor_and_isometric_oracle(self):
        self.assertEqual(p.positional_distance(30, 20), 50)
        self.assertEqual(p.positional_distance(0, 50), 100)
        # Source fixed at(30,20), actor unmoved; camera listener moves to(30,20).
        self.assertEqual(p.positional_distance(0, 0), 0)
        self.reject('CoordinateLimit', lambda: p.positional_distance(1000001, 0))

    def test_owner_cancel_reset_stale_completion_and_budget(self):
        s = p.OwnershipScenario()
        old = s.play('dialog'); unrelated = s.play('map')
        self.assertEqual(old, (1, 1))
        s.close_owner('dialog'); s.cancel(old); s.cancel(old)
        self.assertEqual(s.active, {unrelated: 'map'})
        s.reset(); new = s.play('dialog'); s.cancel(old)
        self.assertEqual(s.active, {(2, 3): 'dialog'})
        for _ in range(31):
            s.play('map')
        self.reject('VoiceBudget', lambda: s.play('map'))

    def test_repeated_equal_zero_scheme_idempotence(self):
        s = p.OwnershipScenario()
        self.assertTrue(s.set_schemes(7, 8))
        self.assertFalse(s.set_schemes(7, 8))
        self.assertTrue(s.set_schemes(7, 7)); self.assertEqual(s.schemes, (7, 0))
        self.assertTrue(s.set_schemes(0, 0)); self.assertFalse(s.set_schemes(0, 0))
        self.reject('SchemeIdentity', lambda: s.set_schemes(-1, 0))

    def test_selected_dat_cli_real_seam_duplicates_caps_zlib(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); archive = root / 'fixture.dat'
            payload = wave()
            archive.write_bytes(d.make_archive([(b'Sound\\Fixture.wav', 2, len(payload), zlib.compress(payload))]))
            before = d.hash_file(archive)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(p.main(['--dat', str(archive), '--entry', 'sound/fixture.wav', '--kind', 'wav']), 0)
            report = json.loads(output.getvalue())
            self.assertEqual(report['shape']['sample_rate'], 11025)
            self.assertEqual(report['archive_sha256_before'], before)
            self.assertEqual(d.hash_file(archive), before)
            self.reject('MissingOrDuplicateResource', lambda: p.selected_dat(archive, 'sound/missing.wav'))
            archive.write_bytes(d.make_archive([(b'Sound\\Fixture.wav', 1, 1, b'x'), (b'sound/fixture.WAV', 1, 1, b'y')]))
            self.reject('MissingOrDuplicateResource', lambda: p.selected_dat(archive, 'sound/fixture.wav'))
            archive.write_bytes(d.make_archive([(b'sound/fixture.wav', 2, 1048577, zlib.compress(b'x'))]))
            with self.assertRaises(d.DatProbeError):
                p.selected_dat(archive, 'sound/fixture.wav')
            archive.write_bytes(d.make_archive([(b'sound/fixture.wav', 2, 1, zlib.compress(b'xx'))]))
            with self.assertRaises(d.DatProbeError):
                p.selected_dat(archive, 'sound/fixture.wav')
            archive.write_bytes(d.make_archive([(b'sound/fixture.wav', 0x800, 1, b'x')]))
            self.reject('UnsupportedDatFlags', lambda: p.selected_dat(archive, 'sound/fixture.wav'))
            archive.write_bytes(b'short')
            self.reject('ArchiveLimit', lambda: p.selected_dat(archive, 'sound/fixture.wav'))
            archive.write_bytes(d.U32.pack(200001) + d.FOOTER.pack(bytes(16), b'1TAD', 0, 32))
            self.reject('IndexEntryLimit', lambda: p.selected_dat(archive, 'sound/fixture.wav'))

    def test_loose_prefix_cli_symlink_and_size_limits(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); file = root / 'fixture.mp3'
            frame = bytes.fromhex('fffb90c0') + bytes(413)
            file.write_bytes(frame*2 + bytes(3000000))
            data, report = p.selected_loose(root, 'fixture.mp3')
            self.assertEqual(len(data), 65536)
            self.assertEqual(report['file_bytes'], 3000834)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(p.main(['--loose-root', str(root), '--entry', 'fixture.mp3', '--kind', 'mp3']), 0)
            self.assertTrue(json.loads(output.getvalue())['shape']['two_frames'])
            link = root / 'link.mp3'; link.symlink_to(file)
            self.reject('LooseSymlink', lambda: p.selected_loose(root, 'link.mp3'))
            with file.open('wb') as stream:
                stream.truncate(8388609)
            self.reject('LooseSizeLimit', lambda: p.selected_loose(root, 'fixture.mp3'))


def run():
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(ProbeTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():
        raise SystemExit(1)


if __name__ == '__main__':
    run()
