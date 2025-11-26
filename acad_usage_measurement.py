#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Copyright (c) 2021 Geoinformation Winterthur. All rights reserved.
Author: Edgar Butwilowski

A web service that makes it possible for admins to centrally measure
the usage times of AutoCAD installations in a corporate network.

- Webframework: Flask
- DB-Treiber: python-oracledb (Thin Mode)
- Logging: Versand an ELK

Voraussetzungen (pip):
    pip install flask python-oracledb requests urllib3

Konfiguration: config.ini im gleichen Verzeichnis.
"""

from __future__ import annotations

import os
import sys
import json
import typing as t
from datetime import datetime, timezone, date
from pathlib import Path
import threading
import configparser

import requests
try:
    import urllib3  # optional, nur um InsecureRequestWarning zu unterdrücken
except Exception:  # pragma: no cover - optional dependency
    urllib3 = None

try:
    import oracledb  # python-oracledb
except Exception as e:  # pragma: no cover
    print("Fehler: python-oracledb ist nicht installiert. Bitte mit 'pip install python-oracledb' nachinstallieren.")
    raise

# HIER Thick-Mode initialisieren
try:
    oracledb.init_oracle_client(lib_dir=r"E:\Oracle\product\19.3.0\client_x64\bin")
except Exception as e:
    print(f"Fehler bei init_oracle_client: {e}", file=sys.stderr)

from flask import Flask, jsonify, request, abort, make_response

# -----------------------------------------------------------------------------
# Konfiguration
# -----------------------------------------------------------------------------
SCRIPT_PATH = Path(__file__).resolve()
CFG = configparser.ConfigParser()
CFG.read(SCRIPT_PATH.parent / "config.ini")

# Lokales Logfile für fehlerhafte Benutzer-Kürzel (personenbezogene Daten nur hier)
LOCAL_ISSUE_LOG = SCRIPT_PATH.parent / "adsk_usage_measurement.log"


def _cfg_get(section: str, option: str, default: str | None = None, *, required: bool = False) -> str:
    if CFG.has_option(section, option):
        return CFG.get(section, option)
    if default is not None and not required:
        return default
    if required:
        raise RuntimeError(f"Konfiguration fehlt: [{section}] {option}")
    return ""


# Oracle-Verbindungsparameter
ORA_USER = _cfg_get("Oracle", "user", None)
ORA_PASSWORD = _cfg_get("Oracle", "password", None)
ORA_HOST = _cfg_get("Oracle", "host", None)
ORA_PORT = _cfg_get("Oracle", "port", "1521")
ORA_SERVICE = _cfg_get("Oracle", "service_name", None)
ORA_DSN = _cfg_get("Oracle", "dsn", None)

# Tabellen- und Spaltennamen
TBL_ACAD_USER = _cfg_get("Tables", "acad_user_table", required=True)
COL_USER_NAME = _cfg_get("Tables", "user_name_column", required=True)
COL_DOMAIN_NAME = _cfg_get("Tables", "domain_name_column", required=True)
COL_LAST_PING = _cfg_get("Tables", "last_ping_column", required=True)

TBL_USERS = _cfg_get("Tables", "user_table", required=True)
COL_USER_LOGIN = _cfg_get("Tables", "user_login_column", required=True)
COL_USER_ORGFID = _cfg_get("Tables", "user_orgfid_column", required=True)

TBL_USAGE = _cfg_get("Tables", "usage_table", required=True)
COL_USAGE_DATE = _cfg_get("Tables", "usage_date_column", required=True)
COL_USAGE_APPCODE = _cfg_get("Tables", "usage_appcode_column", required=True)
COL_USAGE_VERSION = _cfg_get("Tables", "usage_version_column", required=True)
COL_USAGE_MINUTES = _cfg_get("Tables", "usage_minutes_column", required=True)
COL_USAGE_ORGFID = _cfg_get("Tables", "usage_orgfid_column", required=True)

UNKNOWN_VERSION = _cfg_get("Service", "unknown_version_value", "unknown")

ELK_URL = _cfg_get("ELK", "url", required=True)
ELK_VERIFY_SSL = _cfg_get("ELK", "verify_ssl", "true").strip().lower() in {"1", "true", "yes", "y"}

if not ELK_VERIFY_SSL and urllib3 is not None:
    try:
        urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)
    except Exception:
        pass

HOSTNAME = os.uname().nodename if hasattr(os, "uname") else os.getenv("COMPUTERNAME", "unknown-host")

# Erkennen, ob wir voraussichtlich unter WSGI (z.B. IIS/wfastcgi) laufen
RUN_CONTEXT = "wsgi" if os.environ.get("WSGI_HANDLER") else "standalone"

# -----------------------------------------------------------------------------
# Lokales Logging für fehlerhafte Benutzer-Kürzel (nicht an ELK senden!)
# -----------------------------------------------------------------------------
def log_local_issue(message: str, user_name: str | None = None, domain_name: str | None = None) -> None:
    """Schreibt personenbezogene Problemfälle in ein lokales Logfile neben dem Skript."""
    try:
        ts = datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
        parts = [ts, message]
        if user_name is not None:
            parts.append(f"user={user_name}")
        if domain_name is not None:
            parts.append(f"domain={domain_name}")
        line = " | ".join(parts)
        with LOCAL_ISSUE_LOG.open("a", encoding="utf-8") as f:
            f.write(line + "\n")
    except Exception as e:
        # Nur auf STDERR ausgeben, nicht weiter eskalieren
        print(f"Warnung: Schreiben ins lokale Logfile fehlgeschlagen: {e}", file=sys.stderr)


# -----------------------------------------------------------------------------
# ELK-Logging (anonymisiert)
# -----------------------------------------------------------------------------
def elk_log(level: str, message: str, details: t.Optional[t.List[str]] = None) -> None:
    """Versendet strukturierte Logs an den ELK-Server.

    Wichtig: Keine personenbezogenen Daten (Login-Kürzel, Domain-Login etc.)
    in 'details' übergeben.
    """
    environment = (
        "test" if _cfg_get("Oracle", "service_name", "").lower().startswith("tgis") else "production"
    )
    payload: dict[str, t.Any] = {
        "@timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "host": HOSTNAME,
        "environment": environment,
        "service": "acad_usage_measurement",
        "level": level,
        "message": message,
        "script_dir": str(SCRIPT_PATH.parent),
        "script_file": SCRIPT_PATH.name,
        "run_context": RUN_CONTEXT,
    }
    if details:
        payload["details"] = details
    try:
        requests.post(ELK_URL, json=payload, timeout=10, verify=ELK_VERIFY_SSL)
    except Exception as e:  # bewusst nicht erneut raisen
        print(f"Warnung: Log-Sendung an ELK fehlgeschlagen: {e}")


# Einmaliges Startup-Log im WSGI-Betrieb (z.B. unter IIS/wfastcgi)
if RUN_CONTEXT == "wsgi":
    try:
        elk_log("INFO", "Service-Start (Import im WSGI-Kontext)")
    except Exception:
        # Logging darf den Import nicht verhindern
        pass

# -----------------------------------------------------------------------------
# Oracle-Verbindung
# -----------------------------------------------------------------------------

def _build_dsn() -> str:
    # Wenn DSN voll angegeben ist, diese verwenden (kann auch user/pass enthalten)
    if ORA_DSN:
        return ORA_DSN
    if not (ORA_HOST and ORA_PORT and ORA_SERVICE):
        raise RuntimeError("Unvollständige Oracle-DSN. Entweder [Oracle] dsn oder host/port/service_name angeben.")
    return f"{ORA_HOST}:{ORA_PORT}/{ORA_SERVICE}"


def get_connection() -> oracledb.Connection:
    dsn = _build_dsn()
    if "/" in dsn and "@" in dsn and ORA_USER is None:
        # dsn im Format user/pass@host:port/service -> Direktverbindung
        conn = oracledb.connect(dsn=dsn)
    else:
        if not (ORA_USER and ORA_PASSWORD):
            raise RuntimeError("Oracle-Credentials fehlen: [Oracle] user/password")
        conn = oracledb.connect(user=ORA_USER, password=ORA_PASSWORD, dsn=dsn)
    return conn


# -----------------------------------------------------------------------------
# Thread-Synchronisation
# -----------------------------------------------------------------------------
lock_update_last_ping = threading.Lock()
lock_update_minutes = threading.Lock()


# -----------------------------------------------------------------------------
# Flask-App und Routen
# -----------------------------------------------------------------------------
app = Flask(__name__)
app.url_map.strict_slashes = False

# -----------------------------------------------------------------------------
# Routen
# -----------------------------------------------------------------------------
@app.get("/")
def index():
    return jsonify({"message": "Service works."})

@app.get("/ping")
def ping():
    try:
        # Rohwerte für lokale Logs sichern (personenbezogen nur lokal!)
        # Case-insensitive Parameter (unterstützt z.B. userName UND username)
        raw_user_name = (
            request.args.get("userName", type=str)
            or request.args.get("username", type=str)
        )
        raw_domain_name = (
            request.args.get("domainName", type=str)
            or request.args.get("domainname", type=str)
        )
        # version kann z.B. "version" oder "Version" heissen
        version = (
            request.args.get("version", type=str)
            or request.args.get("Version", type=str)
        )

        app_code = request.args.get("appCode", type=int)
        if app_code is None:
            app_code = request.args.get("appcode", type=int)

        if not raw_user_name:
            # Keine DB-Operation, daher nur Warnung an ELK
            elk_log("WARN", "Ping: Kein Benutzername übergeben")
            return make_response(("No user name provided", 400))

        if not raw_domain_name:
            # Benutzer-Kürzel lokal loggen, aber nicht an ELK schicken
            log_local_issue("Ping ohne Domänenname", user_name=raw_user_name, domain_name=None)
            elk_log("WARN", "Ping: Kein Domänenname übergeben")
            return make_response(("No domain name provided", 400))

        if app_code is None:
            # Ebenfalls lokal mitschreiben, um fehlerhafte Calls nachvollziehen zu können
            log_local_issue("Ping ohne appCode", user_name=raw_user_name, domain_name=raw_domain_name)
            elk_log("WARN", "Ping: Kein appCode übergeben")
            return make_response(("No app code provided", 400))

        # Ab hier nur noch mit bereinigten/kleingeschriebenen Werten arbeiten
        user_name = raw_user_name.lower()
        domain_name = raw_domain_name.lower()
        version = (version or UNKNOWN_VERSION).lower()

        now_local = datetime.now()  # analog zu DateTime.Now in .NET
        user_is_new = False
        user_fid = -1
        org_fid = -1
        last_ping_from_db = now_local

        with get_connection() as con:
            con.autocommit = False

            # 1) Benutzer suchen bzw. neu anlegen + last_ping setzen
            with lock_update_last_ping:
                with con.cursor() as cur:
                    cur.execute(
                        f"SELECT fid, {COL_LAST_PING} FROM {TBL_ACAD_USER} "
                        f"WHERE {COL_USER_NAME}=:username AND {COL_DOMAIN_NAME}=:domainname",
                        username=user_name, domainname=domain_name,
                    )
                    row = cur.fetchone()
                    if row:
                        user_fid = int(row[0])
                        last_ping_from_db = row[1]
                        # last ping aktualisieren
                        cur.execute(
                            f"UPDATE {TBL_ACAD_USER} SET {COL_LAST_PING}=:lp WHERE fid=:fid",
                            lp=now_local, fid=user_fid,
                        )
                    else:
                        # neuen Benutzer einfügen
                        cur.execute(
                            f"INSERT INTO {TBL_ACAD_USER} ({COL_USER_NAME}, {COL_DOMAIN_NAME}, {COL_LAST_PING}) "
                            f"VALUES (:username, :domainname, :lp)",
                            username=user_name, domainname=domain_name, lp=now_local,
                        )
                        user_is_new = True
                        # Personenbezogenes nur lokal loggen, um neue Benutzer-Kürzel zu erkennen
                        log_local_issue(
                            "Neuer Benutzer in TBL_ACAD_USER angelegt",
                            user_name=user_name,
                            domain_name=domain_name,
                        )
                con.commit()

            # 2) Organisation bestimmen
            with con.cursor() as cur:
                cur.execute(
                    f"SELECT {COL_USER_ORGFID} FROM {TBL_USERS} WHERE {COL_USER_LOGIN}=:username",
                    username=user_name,
                )
                row = cur.fetchone()
                if row:
                    org_fid = int(row[0])
                else:
                    org_fid = -1
                    # Benutzer-Kürzel ohne Organisation lokal loggen
                    log_local_issue(
                        "Keine Organisation zum Benutzer gefunden",
                        user_name=user_name,
                        domain_name=domain_name,
                    )
                    # Anonymisierte Info an ELK (ohne Benutzer-Kürzel)
                    elk_log("WARN", "Keine Organisation zum Benutzer gefunden", [
                        f"orgfid={org_fid}",
                        f"appCode={app_code}",
                        f"version={version}",
                    ])

            # 3) Prüfen, ob ausserhalb des gleichen 10-Minuten-Buckets
            def _same_10min_bucket(a: datetime, b: datetime) -> bool:
                return (
                    a.year == b.year
                    and a.month == b.month
                    and a.day == b.day
                    and a.hour == b.hour
                    and (a.minute // 10) == (b.minute // 10)
                )

            is_not_same_bucket = not _same_10min_bucket(now_local, last_ping_from_db)

            if user_is_new or is_not_same_bucket:
                # Auf Tagesgranularität reduzieren (Mitternacht)
                today = date(year=now_local.year, month=now_local.month, day=now_local.day)

                # 3a) Sicherstellen, dass ein Usage-Eintrag existiert
                with lock_update_minutes:
                    with con.cursor() as cur:
                        cur.execute(
                            f"SELECT {COL_USAGE_MINUTES} FROM {TBL_USAGE} "
                            f"WHERE {COL_USAGE_ORGFID}=:orgfid AND {COL_USAGE_DATE}=:udate "
                            f"AND {COL_USAGE_APPCODE}=:app AND {COL_USAGE_VERSION}=:ver",
                            orgfid=org_fid, udate=today, app=app_code, ver=version,
                        )
                        row = cur.fetchone()
                        if not row:
                            cur.execute(
                                f"INSERT INTO {TBL_USAGE} ("
                                f"{COL_USAGE_DATE}, {COL_USAGE_APPCODE}, {COL_USAGE_VERSION}, {COL_USAGE_MINUTES}, {COL_USAGE_ORGFID}) "
                                f"VALUES (:udate, :app, :ver, 0, :orgfid)",
                                udate=today, app=app_code, ver=version, orgfid=org_fid,
                            )
                    con.commit()

                # 3b) +10 Minuten addieren
                with con.cursor() as cur:
                    cur.execute(
                        f"UPDATE {TBL_USAGE} SET {COL_USAGE_MINUTES} = (10 + {COL_USAGE_MINUTES}) "
                        f"WHERE {COL_USAGE_DATE}=:udate AND {COL_USAGE_ORGFID}=:orgfid "
                        f"AND {COL_USAGE_APPCODE}=:app AND {COL_USAGE_VERSION}=:ver",
                        udate=today, orgfid=org_fid, app=app_code, ver=version,
                    )
                con.commit()

        # Wenn wir bis hier gekommen sind, wurden alle DB-Operationen erfolgreich durchgeführt.
        # Jetzt (und nur dann) eine anonyme Erfolgs-Meldung an ELK schicken.
        org_detail = (
            f"orgfid={org_fid}"
            if org_fid != -1
            else "orgfid=unknown"
        )

        elk_log("INFO", "Ping verarbeitet", [
            org_detail,
            f"appCode={app_code}",
            f"version={version}",
            ("new_user" if user_is_new else "existing_user"),
        ])
        return ("", 204)  # analog zu Ok() ohne Body

    except Exception as e:
        # Fehlerlog an ELK – ohne personenbezogene Daten
        elk_log("ERROR", "Ping fehlgeschlagen", [e.__class__.__name__])
        return make_response(("Internal Server Error", 500))

@app.route("/", defaults={"path": ""})
@app.route("/<path:path>")
def router(path: str):
    """
    Zentraler Router für alle Pfade unter IIS.
    Entscheidet anhand des Pfad-Strings, ob Healthcheck oder Ping.
    """
    # Pfad normalisieren: führende/trailing Slashes entfernen
    norm = path.strip("/")

    # Fälle: Root / Healthcheck
    if norm == "" or norm == "adsk_usage_statistics_py":
        return index()

    # Fälle: alles, was auf "ping" endet, an die Ping-Logik weitergeben
    if norm.endswith("ping"):
        return ping()

    # Default: ebenfalls Healthcheck zurückgeben
    return index()



# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
if __name__ == "__main__":
    port = int(os.getenv("PORT", "5000"))
    elk_log("INFO", f"Service-Start im Standalone-Modus auf Port {port}")
    app.run(host="0.0.0.0", port=port)  # Produktiv besser über WSGI/ASGI (gunicorn/uvicorn)
