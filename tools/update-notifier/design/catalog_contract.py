"""Offline draft contract tests, NOT a production approval verifier or feed.

The caller must authenticate the catalog publisher and establish that its exact
records follow the owner's approval rule. Valid JSON/hash syntax proves no such
approval. No I/O, URLs, runtime wiring or source selection is implemented here.
"""
from dataclasses import dataclass
from datetime import datetime, timezone, timedelta
import json
import re

VERSION = re.compile(r'(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-proto\.(0|[1-9][0-9]{0,8}))?\Z')
HEX40 = re.compile(r'[0-9a-f]{40}\Z')
HEX64 = re.compile(r'[0-9a-f]{64}\Z')


class InvalidCatalog(ValueError):
    pass


def version_key(value):
    if not isinstance(value, str) or len(value) > 64:
        raise InvalidCatalog('invalid version')
    m = VERSION.fullmatch(value)
    if not m:
        raise InvalidCatalog('unsupported version')
    a, b, c = (int(m[i]) for i in (1, 2, 3))
    return a, b, c, m[4] is None, int(m[4] or 0)


def _time(value):
    if not isinstance(value, str) or not re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z', value):
        raise InvalidCatalog('timestamp must be UTC seconds')
    try:
        return datetime.strptime(value, '%Y-%m-%dT%H:%M:%SZ').replace(tzinfo=timezone.utc)
    except ValueError as e:
        raise InvalidCatalog('invalid date') from e


def _positive_integer(value):
    return type(value) is int and 0 < value <= 2**63 - 1


def _object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise InvalidCatalog('duplicate JSON property')
        result[key] = value
    return result


@dataclass(frozen=True)
class Record:
    version: str
    commit: str
    sha256: str
    size: int
    release_id: int
    asset_id: int
    approval_record_id: str


def parse_catalog(raw, *, now, max_bytes, max_records, max_age, max_lifetime, minimum_revision=None):
    """Validate a synthetic proposed catalog, not the publisher's authority.

    All size/freshness bounds are explicit caller policy. This draft chooses no
    live policy, endpoint or approval storage. Transport must bound BEFORE read.
    """
    if not isinstance(raw, bytes) or len(raw) > max_bytes:
        raise InvalidCatalog('response byte limit')
    if now.tzinfo is None or max_age <= timedelta(0) or max_lifetime <= timedelta(0):
        raise ValueError('explicit UTC-aware clock and positive policy required')
    try:
        doc = json.loads(raw.decode('utf-8'), object_pairs_hook=_object,
                         parse_constant=lambda _: (_ for _ in ()).throw(InvalidCatalog('non-finite JSON')))
    except (UnicodeError, json.JSONDecodeError, RecursionError) as e:
        raise InvalidCatalog('invalid JSON') from e
    keys = {'schema', 'revision', 'generated_utc', 'expires_utc', 'channel', 'releases'}
    if type(doc) is not dict or set(doc) != keys:
        raise InvalidCatalog('unknown or missing envelope fields')
    if type(doc['schema']) is not int or doc['schema'] != 1 or not _positive_integer(doc['revision']):
        raise InvalidCatalog('schema/revision')
    if minimum_revision is not None and (not _positive_integer(minimum_revision) or doc['revision'] < minimum_revision):
        raise InvalidCatalog('invalid revision floor or rollback')
    if doc['channel'] != 'prototype':
        raise InvalidCatalog('unsupported channel')
    generated, expires = _time(doc['generated_utc']), _time(doc['expires_utc'])
    if not generated <= now < expires or now - generated > max_age or not timedelta(0) < expires - generated <= max_lifetime:
        raise InvalidCatalog('stale, future or invalid lifetime')
    if type(doc['releases']) is not list or len(doc['releases']) > max_records:
        raise InvalidCatalog('record count')
    records, versions, assets = [], set(), set()
    fields = {'version', 'tag', 'commit', 'release_id', 'asset_id', 'asset', 'size', 'sha256', 'layout', 'approval_record_id'}
    for r in doc['releases']:
        if type(r) is not dict or set(r) != fields:
            raise InvalidCatalog('unknown or missing release fields')
        key = version_key(r['version'])
        if key[3] or r['tag'] != 'v' + r['version']:
            raise InvalidCatalog('prototype tag mismatch')
        if not isinstance(r['commit'], str) or not HEX40.fullmatch(r['commit']):
            raise InvalidCatalog('commit syntax')
        if not isinstance(r['sha256'], str) or not HEX64.fullmatch(r['sha256']):
            raise InvalidCatalog('hash syntax')
        if not all(_positive_integer(r[k]) for k in ['release_id', 'asset_id', 'size']):
            raise InvalidCatalog('identity/size')
        if r['asset'] != 'StartApp-win-x64.zip' or r['layout'] != 'portable-v1':
            raise InvalidCatalog('asset/layout')
        aid = r['approval_record_id']
        if not isinstance(aid, str) or not re.fullmatch(r'[A-Za-z0-9._-]{1,128}', aid):
            raise InvalidCatalog('approval reference syntax, not proof')
        if r['version'] in versions or r['asset_id'] in assets:
            raise InvalidCatalog('duplicate version/asset identity')
        versions.add(r['version']); assets.add(r['asset_id'])
        records.append(Record(r['version'], r['commit'], r['sha256'], r['size'], r['release_id'], r['asset_id'], aid))
    return tuple(records)


def select_newer(records, installed_version):
    current = version_key(installed_version)
    # This first draft catalog is prototype-only. Stable installations get none.
    if current[3]:
        return None
    newer = [r for r in records if version_key(r.version) > current]
    return max(newer, key=lambda r: version_key(r.version), default=None)
