#!/usr/bin/env python3
"""Respaldo programado y restauración aislada de PostgreSQL local (INT-01)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[3]
COMPOSE = ['docker', 'compose', '--project-directory', str(ROOT)]


def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def dbexec(script, *args, **kwargs):
    return run(COMPOSE + ['exec', '-T', 'db', 'sh', '-ec', script, 'int01', *args], **kwargs)


def sql(database, query):
    return dbexec('exec psql -X -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d "$1"',
                  database, input=query.encode(), stdout=subprocess.PIPE).stdout


def manifest(database):
    tables = json.loads(sql(database, "SELECT coalesce(json_agg(x ORDER BY s,t),'[]') FROM (SELECT schemaname s, tablename t FROM pg_tables WHERE schemaname NOT IN ('pg_catalog','information_schema')) x;"))
    result = {}
    for table in tables:
        name = '.'.join('"' + table[k].replace('"', '""') + '"' for k in ('s', 't'))
        # Sorting canonical JSON preserves duplicate rows and ignores physical row order.
        data = sql(database, f"COPY (SELECT v FROM (SELECT row_to_json(r)::text v FROM {name} r) rows ORDER BY v COLLATE \"C\") TO STDOUT;")
        count = int(sql(database, f'SELECT count(*) FROM {name};'))
        result[name] = {'rows': count, 'sha256': hashlib.sha256(data).hexdigest()}
    return result


def execute(output):
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    folder = output / stamp
    folder.mkdir(parents=True, mode=0o700)
    report = {'case': 'INT-01', 'started_utc': stamp, 'status': 'FAILED',
              'retention_30_days': 'NOT_TESTED', 'schedule': 'Python timer',
              'precondition': 'No concurrent writers during source comparison'}
    restored = 'int01_' + stamp.lower()
    created = False
    try:
        source = dbexec('printf %s "$POSTGRES_DB"', stdout=subprocess.PIPE).stdout.decode()
        report['source_database'] = source
        report['restored_database'] = restored
        before = manifest(source)
        archive = folder / 'database.dump'
        with archive.open('wb') as handle:
            dbexec('exec pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc', stdout=handle)
        report['archive_bytes'] = archive.stat().st_size
        after = manifest(source)
        if before != after:
            raise RuntimeError('La base cambió durante el respaldo; repetir sin escrituras.')
        dbexec('exec createdb -U "$POSTGRES_USER" --template=template0 "$1"', restored)
        created = True
        with archive.open('rb') as handle, (folder / 'restore.log').open('wb') as log:
            dbexec('exec pg_restore -U "$POSTGRES_USER" -d "$1" --exit-on-error --single-transaction --no-owner --no-privileges --verbose',
                   restored, stdin=handle, stderr=log)
        recovered = manifest(restored)
        (folder / 'source.json').write_text(json.dumps(before, indent=2))
        (folder / 'restored.json').write_text(json.dumps(recovered, indent=2))
        if before != recovered:
            raise RuntimeError('Difieren tablas, conteos o contenido restaurado.')
        report.update(status='PASSED', tables=len(before), rows=sum(x['rows'] for x in before.values()))
        report['coverage'] = 'Table contents and successful schema/constraint restoration; roles/ACL excluded'
        if not before or not report['rows']:
            report['limitation'] = 'Base sin tablas o sin filas: recuperación comprobada con cobertura de datos limitada.'
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        try:
            if created:
                dbexec('exec dropdb -U "$POSTGRES_USER" "$1"', restored)
        finally:
            (folder / 'result.json').write_text(json.dumps(report, indent=2))
            print(json.dumps(report, ensure_ascii=False), flush=True)
            print(f'Evidencia: {folder}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--delay', type=int, default=5, help='Segundos hasta primera ejecución automática')
    parser.add_argument('--daily', action='store_true', help='Repetir cada 24 horas mientras el proceso siga activo')
    parser.add_argument('--output', type=Path, default=Path(__file__).parent / 'artifacts')
    args = parser.parse_args()
    if args.delay < 0:
        parser.error('--delay debe ser >= 0')
    os.umask(0o077)
    print(f'Programado: inicio en {args.delay}s; periodicidad: {"24 horas" if args.daily else "una ejecución"}', flush=True)
    time.sleep(args.delay)
    while True:
        start = time.monotonic()
        execute(args.output.resolve())
        if not args.daily:
            break
        time.sleep(max(0, 86400 - (time.monotonic() - start)))


if __name__ == '__main__':
    main()
