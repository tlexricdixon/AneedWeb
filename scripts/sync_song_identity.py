"""Sync SAM's existing UUID mapping to Azure SQL. No SAM changes.

Env: ANEED_SAM_MARIADB (MySQL connection string),
     ANEED_AZURE_SQL_ODBC (ODBC connection string).
Run with --write to make changes; default previews only.
"""
import argparse
import os
import re
import uuid
import pymysql
import pyodbc

def parse_mysql_connection(raw):
    pairs = dict((k.strip().lower(), v.strip()) for chunk in raw.split(";")
                 if "=" in chunk for k,v in [chunk.split("=",1)])
    return dict(host=pairs.get("server",pairs.get("host","localhost")),
                port=int(pairs.get("port","3306")),
                user=pairs.get("user id",pairs.get("uid",pairs.get("user",""))),
                password=pairs.get("password",pairs.get("pwd","")),
                database=pairs.get("database",pairs.get("initial catalog","")))

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--write",action="store_true")
    args=parser.parse_args()
    maria=os.getenv("ANEED_SAM_MARIADB")
    sql=os.getenv("ANEED_AZURE_SQL_ODBC")
    if not maria or not sql:
        raise SystemExit("Set ANEED_SAM_MARIADB and ANEED_AZURE_SQL_ODBC first")
    with pymysql.connect(**parse_mysql_connection(maria)) as con:
        with con.cursor() as cur:
            cur.execute("SELECT sam_song_id,aneed_id FROM aneed_song_map ORDER BY sam_song_id")
            records=cur.fetchall()
    rows=[(int(sid),str(uuid.UUID(str(uid)))) for sid,uid in records]
    print(f"SAM identity mappings: {len(rows)}; mode: {'WRITE' if args.write else 'PREVIEW'}")
    if not args.write:
        return
    with pyodbc.connect(sql,autocommit=False) as con:
        cur=con.cursor()
        cur.execute("SELECT OBJECT_ID(N'dbo.SongIdentity',N'U')")
        if cur.fetchone()[0] is None:
            raise SystemExit("Run scripts/azure_artwork_catalog.sql against AneedCatalog first")
        # Update known identities, then insert missing rows. Re-runs are safe.
        cur.fast_executemany=True
        cur.executemany("""
            UPDATE dbo.SongIdentity SET AneedId=?,UpdatedAt=SYSUTCDATETIME()
            WHERE SamSongId=? AND AneedId<>?
            """, [(uid,sid,uid) for sid,uid in rows])
        cur.executemany("""
            INSERT INTO dbo.SongIdentity(SamSongId,AneedId)
            SELECT ?,? WHERE NOT EXISTS
                (SELECT 1 FROM dbo.SongIdentity WHERE SamSongId=?)
            """, [(sid,uid,sid) for sid,uid in rows])
        con.commit()
        cur.execute("SELECT COUNT(*) FROM dbo.SongIdentity")
        print(f"Azure identities: {cur.fetchone()[0]}")

if __name__=="__main__":
    main()
