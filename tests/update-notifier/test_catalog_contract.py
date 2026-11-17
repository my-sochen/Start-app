"""Offline synthetic contract exploration only. No network or real approvals."""
import copy
from datetime import datetime, timezone, timedelta
import importlib.util
import json
from pathlib import Path
import sys
import unittest
ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / 'tools/update-notifier/design/catalog_contract.py'
spec = importlib.util.spec_from_file_location('catalog_contract', PATH)
c = importlib.util.module_from_spec(spec);sys.modules[spec.name] = c;spec.loader.exec_module(c)

class CatalogTests(unittest.TestCase):
    def setUp(self):
        self.doc = json.loads((PATH.parent / 'catalog.synthetic.example.json').read_text())
        self.now = datetime(2026, 11, 17, 12, tzinfo=timezone.utc)
    def parse(self, doc=None, raw=None, **policy):
        opts = dict(now=self.now, max_bytes=65536, max_records=128,
                    max_age=timedelta(days=1), max_lifetime=timedelta(days=1))
        opts.update(policy)
        return c.parse_catalog(raw if raw is not None else json.dumps(self.doc if doc is None else doc).encode(), **opts)
    def test_explicit_revision_floor(self):
        self.assertEqual(len(self.parse(minimum_revision=1)),1)
        with self.assertRaises(c.InvalidCatalog):self.parse(minimum_revision=2)
    def test_shared_golden_fixtures(self):
        f=json.loads((PATH.parent / 'version-selection.fixtures.json').read_text())
        for pair in f['ordering']:
            self.assertGreater(c.version_key(pair['newer']),c.version_key(pair['older']))
        for v in f['invalid_versions']:
            with self.assertRaises(c.InvalidCatalog):c.version_key(v)
        for case in f['selection']:
            records=[c.Record(v,'a'*40,'b'*64,123,1,i+1,'synthetic') for i,v in enumerate(case['eligible'])]
            selected=c.select_newer(records,case['installed'])
            self.assertEqual(None if selected is None else selected.version,case['expected'])
    def test_valid_synthetic_shape_is_not_approval_proof(self):
        self.assertEqual(self.parse()[0].approval_record_id, 'synthetic-approval-not-real')
    def test_version_lines_beat_global_prototype_counter(self):
        self.assertGreater(c.version_key('0.2.0-proto.1'), c.version_key('0.1.0-proto.99'))
    def test_stable_sorts_after_same_line_prototype(self):
        self.assertGreater(c.version_key('1.0.0'), c.version_key('1.0.0-proto.99'))
    def test_patch_is_update(self):
        self.assertGreater(c.version_key('1.0.1-proto.1'), c.version_key('1.0.0-proto.99'))
    def test_invalid_versions(self):
        for v in ['01.0.0', '1.0', 'v1.0.0', '1.0.0-beta.1', '1.0.0-proto.01', '1.0.0\n', True]:
            with self.subTest(v=v), self.assertRaises(c.InvalidCatalog):c.version_key(v)
    def test_expired_future_and_excessive_age(self):
        for now in [datetime(2026,11,18,tzinfo=timezone.utc), datetime(2026,11,16,tzinfo=timezone.utc)]:
            with self.assertRaises(c.InvalidCatalog):self.parse(now=now)
        with self.assertRaises(c.InvalidCatalog):self.parse(max_age=timedelta(hours=1))
    def test_lifetime_policy_is_explicit(self):
        with self.assertRaises(c.InvalidCatalog):self.parse(max_lifetime=timedelta(hours=1))
    def test_empty_catalog_honestly_means_none(self):
        self.doc['releases'] = [];self.assertEqual(self.parse(), ())
    def test_removed_release_is_no_longer_offered(self):
        self.assertIsNotNone(c.select_newer(self.parse(), '0.1.0-proto.1'))
        self.doc['releases'] = [];self.assertIsNone(c.select_newer(self.parse(), '0.1.0-proto.1'))
    def test_equal_older_and_stable_installation_not_offered(self):
        for v in ['0.2.0-proto.1', '0.3.0-proto.1', '0.1.0']:
            self.assertIsNone(c.select_newer(self.parse(), v))
    def test_duplicate_versions_and_assets_rejected(self):
        self.doc['releases'].append(copy.deepcopy(self.doc['releases'][0]))
        with self.assertRaises(c.InvalidCatalog):self.parse()
    def test_forged_approved_flag_not_a_contract_field(self):
        self.doc['releases'][0]['approved'] = True
        with self.assertRaises(c.InvalidCatalog):self.parse()
    def test_unknown_and_missing_envelope_fields(self):
        for key in ['schema', 'expires_utc', 'releases']:
            doc=copy.deepcopy(self.doc);del doc[key]
            with self.assertRaises(c.InvalidCatalog):self.parse(doc)
    def test_wrong_identity_types_and_metadata(self):
        for key,value in [('commit','x'),('sha256','a'*63),('size',True),('release_id',0),('asset_id',-1),('layout','legacy'),('tag','v9.9.9'),('approval_record_id','https://evil.invalid/')]:
            doc=copy.deepcopy(self.doc);doc['releases'][0][key]=value
            with self.subTest(key=key),self.assertRaises(c.InvalidCatalog):self.parse(doc)
    def test_schema_and_channel(self):
        for key,value in [('schema',True),('schema',2),('channel','stable'),('revision',False)]:
            doc=copy.deepcopy(self.doc);doc[key]=value
            with self.assertRaises(c.InvalidCatalog):self.parse(doc)
    def test_duplicate_json_properties_rejected(self):
        with self.assertRaises(c.InvalidCatalog):self.parse(raw=b'{"schema":1,"schema":1}')
    def test_invalid_nonfinite_and_nonutf8(self):
        for raw in [b'{', b'NaN', b'\xff']:
            with self.assertRaises(c.InvalidCatalog):self.parse(raw=raw)
    def test_byte_and_count_limits(self):
        with self.assertRaises(c.InvalidCatalog):self.parse(max_bytes=3)
        with self.assertRaises(c.InvalidCatalog):self.parse(max_records=0)
    def test_exact_utc_required(self):
        self.doc['generated_utc']='2026-11-17T00:00:00+00:00'
        with self.assertRaises(c.InvalidCatalog):self.parse()
    def test_invalid_calendar_date(self):
        self.doc['generated_utc']='2026-02-30T00:00:00Z'
        with self.assertRaises(c.InvalidCatalog):self.parse()

if __name__ == '__main__':unittest.main(verbosity=2)
