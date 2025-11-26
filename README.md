# Usage Measurement Service for AutoCAD
A web service that makes it possible for admins to centrally measure the usage times of AutoCAD installations in a corporate network.

## Motivation
After the switch of Autodesk products to named user licensing, it is possible that the usage statistics offered by Autodesk are no longer sufficient for the needs of some organizations. The new usage statistics are only available in the temporal resolution of a whole day. This means that it is only measured whether the Autodesk product was opened once on a certain day.

However, in some cases measurements with a higher temporal resolution are required. For example, this is the case when the departments of an organization want to share the license costs fairly among themselves based on the actual usage. Therefore a new solution is needed.

## How to operate
This web service works in tandem with a plug-in that is also provided as open-source in another repository, which is:

https://github.com/Fachstelle-Geoinformation-Winterthur/acad_usage_measurement_client

You need to install and run the plug-in, in order to use this web service.

Here is the full English translation, preserving structure and technical meaning:

# Single-file Python skript with ELK logging

* Endpoint **GET /** → `{"message": "Service works."}`
* Endpoint **GET /ping** with parameters `userName`, `domainName`, `appCode`, `version`
* **ELK logging**, including environment detection based on `Oracle.service_name`
* **Configuration via `config.ini`** in the same directory (table and column names, Oracle DSN/credentials, ELK URL)
* **Transactions** and **thread-safe** sections

## Startup

Linux:

```
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
```

Windows:

```
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```

## Usage notes

* Dependencies: `pip install -r requirements.txt`
* Start locally, e.g.: `python acad_usage_measurement.py` (port can be overridden via `PORT` environment variable)
* Logging is sent to ELK. If ELK transmission fails, a warning is written to STDOUT.
