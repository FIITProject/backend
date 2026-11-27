#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ER-диаграмма (Mermaid) из живой схемы Postgres → docs/db.md (блок между маркерами).

Запуск после миграций (локальный стек поднят):
    docker compose run --rm migrate migrate
    python3 scripts/db-diagram.py

Схему читает через `docker compose exec db psql`, поэтому ничего ставить не нужно.
"""
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOC = os.path.join(ROOT, "docs", "db.md")
BEGIN, END = "<!-- db-diagram:begin -->", "<!-- db-diagram:end -->"

QUERY = r"""
with cols as (
  select c.table_name, c.column_name, c.ordinal_position, c.is_nullable = 'YES' as nullable, c.udt_name
  from information_schema.columns c
  join information_schema.tables t on t.table_schema = c.table_schema and t.table_name = c.table_name and t.table_type = 'BASE TABLE'
  where c.table_schema = 'public' and c.table_name <> '__EFMigrationsHistory'
),
keys as (
  select tc.table_name, kcu.column_name, tc.constraint_type, tc.constraint_name
  from information_schema.table_constraints tc
  join information_schema.key_column_usage kcu on kcu.constraint_name = tc.constraint_name and kcu.table_schema = tc.table_schema
  where tc.table_schema = 'public' and tc.constraint_type in ('PRIMARY KEY', 'FOREIGN KEY')
),
fks as (
  select kcu.table_name, kcu.column_name, ccu.table_name as ref_table
  from information_schema.table_constraints tc
  join information_schema.key_column_usage kcu on kcu.constraint_name = tc.constraint_name and kcu.table_schema = tc.table_schema
  join information_schema.constraint_column_usage ccu on ccu.constraint_name = tc.constraint_name and ccu.table_schema = tc.table_schema
  where tc.table_schema = 'public' and tc.constraint_type = 'FOREIGN KEY'
),
uniq as (
  -- уникальные индексы из одной колонки (составные описаны в тексте документа)
  select t.relname as table_name, a.attname as column_name
  from pg_index i
  join pg_class t on t.oid = i.indrelid
  join pg_namespace n on n.oid = t.relnamespace and n.nspname = 'public'
  join pg_attribute a on a.attrelid = t.oid and a.attnum = i.indkey[0]
  where i.indisunique and not i.indisprimary and i.indnatts = 1
)
select json_agg(json_build_object(
  'table', c.table_name, 'column', c.column_name, 'pos', c.ordinal_position, 'nullable', c.nullable, 'type', c.udt_name,
  'pk', exists(select 1 from keys k where k.table_name = c.table_name and k.column_name = c.column_name and k.constraint_type = 'PRIMARY KEY'),
  'fk', (select f.ref_table from fks f where f.table_name = c.table_name and f.column_name = c.column_name limit 1),
  'uk', exists(select 1 from uniq u where u.table_name = c.table_name and u.column_name = c.column_name)
) order by c.table_name, c.ordinal_position)
from cols c;
"""

TYPES = {
    "int4": "int", "int8": "bigint", "int2": "smallint", "numeric": "decimal", "text": "text", "varchar": "text",
    "bool": "bool", "timestamptz": "timestamptz", "timestamp": "timestamp", "date": "date", "jsonb": "jsonb",
    "json": "json", "uuid": "uuid", "bytea": "bytes", "_int4": "int_array", "_text": "text_array",
}


def load():
    out = subprocess.run(
        ["docker", "compose", "exec", "-T", "db", "psql", "-U", "fiit", "-d", "fiit", "-Atc", QUERY],
        cwd=ROOT, check=True, capture_output=True, text=True,
    ).stdout.strip()
    return json.loads(out)


def mermaid(rows):
    tables = {}
    for r in rows:
        tables.setdefault(r["table"], []).append(r)
    lines = ["erDiagram"]
    rels = []
    for table in sorted(tables):
        lines.append(f"    {table} {{")
        for c in tables[table]:
            keys = [k for k, on in (("PK", c["pk"]), ("FK", c["fk"]), ("UK", c["uk"])) if on]
            t = TYPES.get(c["type"], re.sub(r"\W", "_", c["type"]))
            comment = ' "null"' if c["nullable"] and not c["pk"] else ""
            lines.append(f"        {t} {c['column']}{' ' + ','.join(keys) if keys else ''}{comment}")
            if c["fk"]:
                # ref ||--o{ table: у записи ref много записей table; необязательная ссылка — |o; уникальная — не больше одной (o|)
                left = "|o" if c["nullable"] else "||"
                right = "o|" if c["uk"] else "o{"
                rels.append(f"    {c['fk']} {left}--{right} {table} : {c['column']}")
        lines.append("    }")
    return "\n".join(lines + sorted(rels))


def main():
    diagram = mermaid(load())
    block = f"{BEGIN}\n```mermaid\n{diagram}\n```\n{END}"
    doc = open(DOC, encoding="utf-8").read()
    if BEGIN not in doc:
        sys.exit(f"В {DOC} нет маркеров {BEGIN} … {END}")
    doc = re.sub(re.escape(BEGIN) + r".*?" + re.escape(END), lambda _: block, doc, flags=re.S)
    open(DOC, "w", encoding="utf-8").write(doc)
    print(f"docs/db.md: {diagram.count(' {') } таблиц")


if __name__ == "__main__":
    main()
