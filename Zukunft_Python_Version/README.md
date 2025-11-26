# Ein-Datei-Python-Port mit ELK-Logging

* Endpoint **GET /** → `{"message": "Service works."}`
* Endpoint **GET /ping** mit Parametern `userName`, `domainName`, `appCode`, `version`
  – identische DB-Abläufe wie im C#-Original (Benutzer anlegen/aktualisieren, Organisation bestimmen, Minuten zählen)
* **ELK-Logging** inkl. Environment-Ableitung aus `Oracle.service_name`
* **Konfiguration über `config.ini`** im gleichen Verzeichnis (Tabellen- und Spaltennamen, Oracle-DSN/Credentials, ELK-URL)
* **Transaktionen** und **thread-sichere** Abschnitte analog zu `lock(...)` im Original

## Aufstarten

Linux:

```
python -m venv .venv
source .venv/bin/activate
pip install flask wfastcgi
```

Windows:

```
python -m venv .venv
.venv\Scripts\activate
pip install flask wfastcgi
```

## Hinweise zum Einsatz

* Abhängigkeiten: `pip install flask python-oracledb requests`
* Lokal starten z.B. mit: `python acad_usage_measurement.py` (Port via `PORT`-Env übersteuerbar)
* Für Oracle: entweder `dsn = user/pass@host:port/service` **oder** `user/password/host/port/service_name` in `config.ini`
* Logging geht **nur** an ELK (kein lokales File-Logging mehr). Bei fehlgeschlagener ELK-Übertragung wird eine Warnung auf STDOUT ausgegeben.
