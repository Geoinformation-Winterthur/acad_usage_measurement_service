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


## TODO: Replace application-level locks with database-driven synchronization (Variant B)

The current implementation uses Python `threading.Lock` objects to avoid race conditions when:

* creating/updating user records (`acad_user`)
* creating/updating daily usage records (`usage_table`)
* preventing double counting of usage minutes within the same 10-minute bucket

This works, but synchronization happens **inside the Flask process**, not in the database.
To make the service more robust and fully stateless across workers, replace the lock-based logic with database-side atomic operations.

### Planned improvements

1. **Introduce proper unique constraints**

   * `acad_user`: `(user_name, domain_name)`
   * `usage_table`: `(orgfid, udate, appcode, version)`

2. **Replace SELECT→INSERT/UPDATE sequences with Oracle MERGE statements**
   Use a single atomic `MERGE` for:

   * inserting or updating the user record (including returning `fid` + last ping)
   * inserting a usage record if it doesn’t exist
   * incrementing minutes when it already exists
     This avoids duplicates without requiring Python-side locking.

3. **Move 10-minute bucket logic into the database (optional, recommended)**
   If feasible, extend `acad_user` with one additional column (e.g. `last_bucket_start`) and calculate the bucket boundary inside SQL.
   This ensures that minute increments are correctly applied even under concurrent requests.

4. **Remove all Python `Lock` objects**
   Once Oracle-side synchronization is implemented via MERGE + constraints, the service becomes:

   * thread-safe
   * multi-process-safe (IIS wfastcgi runs multiple worker processes)
   * easier to maintain

5. **Ensure transactional behaviour**
   Combine MERGE statements into small, well-defined transactions to maintain the original semantic behaviour of the .NET version.
