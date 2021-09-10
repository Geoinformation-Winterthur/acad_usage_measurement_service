// <copyright company="Vermessungsamt Winterthur">
// Author: Edgar Butwilowski
// Copyright (c) 2021 Vermessungsamt Winterthur. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using System;

namespace win.acad_usage_measurement.Controllers
{
    [Route("/[controller]")]
    [ApiController]
    public class PingController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public PingController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        private static readonly object pingSyncMonitorUpdateLastPing = new object();
        private static readonly object pingSyncMonitorUpdateMinutes = new object();
        // GET /ping?username=test&domainname=test
        [HttpGet]
        public IActionResult Ping(string userName, string domainName, int appCode, string version)
        {
            try
            {
                if (userName == null || userName.Length == 0)
                {
                    return BadRequest("No user name provided");
                }
                userName = userName.ToLower();

                if (domainName == null || domainName.Length == 0)
                {
                    return BadRequest("No domain name provided");
                }
                domainName = domainName.ToLower();

                if (version == null || version.Length == 0)
                {
                    version = Startup.unknownValue;
                }
                version = version.ToLower();

                using (OracleConnection oraCon = new OracleConnection(Startup.oraConString))
                {
                    oraCon.Open();


                    DateTime currentDate = DateTime.Now;
                    bool userIsNewlyCreated = false;

                    int userFid = -1;
                    int orgFid = -1;

                    DateTime lastPingFromDb = currentDate;

                    lock (PingController.pingSyncMonitorUpdateLastPing)
                    {
                        using (OracleCommand selectUserFid = oraCon.CreateCommand())
                        {
                            selectUserFid.BindByName = true; // only relevant if Parameters.Add is used
                            selectUserFid.CommandText = "SELECT fid, " + Startup.lastPingColumn +
                                    " FROM " + Startup.acadUserTableName + 
                                    " WHERE " + Startup.userNameColumn + "=:username" +
                                    " AND " + Startup.domainNameColumn + "=:domainname";
                            selectUserFid.Parameters.Add(new OracleParameter("username", userName));
                            selectUserFid.Parameters.Add(new OracleParameter("domainname", domainName));
                            using (OracleDataReader oraReader = selectUserFid.ExecuteReader())
                            {
                                if (oraReader.Read())
                                {
                                    userFid = oraReader.GetInt32(0);
                                    lastPingFromDb = oraReader.GetDateTime(1);
                                }
                            }
                        }

                        if (userFid == -1)
                        {
                            using (OracleCommand insertNewUser = oraCon.CreateCommand())
                            {
                                // user does not exist, so create a new one:
                                insertNewUser.BindByName = true;
                                insertNewUser.CommandText = "INSERT INTO " + Startup.acadUserTableName + 
                                    "(" + Startup.userNameColumn + ", " + Startup.domainNameColumn + ", " +
                                    Startup.lastPingColumn + ") " +
                                    "VALUES( :username, :domainname, :last_ping)";
                                insertNewUser.Parameters.Add(new OracleParameter("username", userName));
                                insertNewUser.Parameters.Add(new OracleParameter("domainname", domainName));
                                insertNewUser.Parameters.Add(new OracleParameter("last_ping", currentDate));
                                insertNewUser.ExecuteNonQuery();
                                userIsNewlyCreated = true;
                            }
                        }
                        else
                        {
                            // update last ping time:
                            using (OracleCommand updateLastPing = oraCon.CreateCommand())
                            {
                                updateLastPing.CommandText = "UPDATE " + Startup.acadUserTableName + 
                                    " SET " + Startup.lastPingColumn + "=:last_ping " +
                                    "WHERE fid=:fid";
                                updateLastPing.Parameters.Add(new OracleParameter("last_ping", currentDate));
                                updateLastPing.Parameters.Add(new OracleParameter("fid", userFid));
                                updateLastPing.ExecuteNonQuery();
                            }
                        }
                    }


                    using (OracleCommand oraComm = oraCon.CreateCommand())
                    {
                        oraComm.BindByName = true;
                        oraComm.CommandText = "SELECT " + Startup.userFidOrganisationColumn +
                            " FROM " + Startup.userTableName +
                            " WHERE " + Startup.userLoginColumn + "=:username";
                        oraComm.Parameters.Add(new OracleParameter("username", userName));
                        using (OracleDataReader oraReader = oraComm.ExecuteReader())
                        {
                            if (oraReader.Read())
                            {
                                orgFid = oraReader.GetInt32(0);
                            }
                        }

                        bool isNotInSame10Min = currentDate.Year != lastPingFromDb.Year || currentDate.Month != lastPingFromDb.Month ||
                                currentDate.Day != lastPingFromDb.Day || currentDate.Hour != lastPingFromDb.Hour ||
                                (currentDate.Minute / 10) != (lastPingFromDb.Minute / 10);

                        if (userIsNewlyCreated || isNotInSame10Min)
                        {
                            // ping came in NOT in the same 10 minutes
                            // so additional 10 minutes can be added:
                            DateTime currentDateCleared = currentDate.Date; // clear time portion

                            bool hasMinutesEntry = false;

                            lock (PingController.pingSyncMonitorUpdateMinutes)
                            {
                                oraComm.CommandText = "SELECT " + Startup.minutesColumn +
                                  " FROM " + Startup.usageDataTableName +
                                  " WHERE " + Startup.organisationFidColumn + "=:organisation_fid" +
                                  " AND " + Startup.dateOfUsageColumn +"=:datum" +
                                  " AND " + Startup.applicationFidColumn + "=:appCode" +
                                  " AND " + Startup.appVersionColumn + "=:version";
                                oraComm.Parameters.Add(new OracleParameter("organisation_fid", orgFid));
                                oraComm.Parameters.Add(new OracleParameter("datum", currentDateCleared));
                                oraComm.Parameters.Add(new OracleParameter("appCode", appCode));
                                oraComm.Parameters.Add(new OracleParameter("version", version));

                                using (OracleDataReader oraReader = oraComm.ExecuteReader())
                                {
                                    if (oraReader.Read())
                                    {
                                        hasMinutesEntry = true;
                                    }
                                }

                                if (!hasMinutesEntry)
                                {
                                    // no entry for minutes so far, so set a new entry:
                                    oraComm.CommandText = "INSERT INTO " + Startup.usageDataTableName +
                                        "(" + Startup.dateOfUsageColumn + ", " + Startup.applicationFidColumn + ", " +
                                        Startup.appVersionColumn + ", " + Startup.minutesColumn + ", " +
                                        Startup.organisationFidColumn + ") " +
                                        "VALUES(:datum, :appCode, :version, 0, :orgfid)";
                                    oraComm.Parameters.Add(new OracleParameter("datum", currentDateCleared));
                                    oraComm.Parameters.Add(new OracleParameter("appCode", appCode));
                                    oraComm.Parameters.Add(new OracleParameter("version", version));
                                    oraComm.Parameters.Add(new OracleParameter("orgfid", orgFid));
                                    oraComm.ExecuteNonQuery();
                                }
                            }

                            // add 10 minutes:
                            oraComm.CommandText = "UPDATE " + Startup.usageDataTableName +
                                " SET " + Startup.minutesColumn + "= (10 + " + Startup.minutesColumn + ")" +
                                " WHERE " + Startup.dateOfUsageColumn + "=:datum" +
                                " AND "+ Startup.organisationFidColumn + "=:orgfid" +
                                " AND " + Startup.applicationFidColumn + "=:appcode"+
                                " AND " + Startup.appVersionColumn + "=:version";
                            oraComm.Parameters.Add(new OracleParameter("datum", currentDateCleared));
                            oraComm.Parameters.Add(new OracleParameter("orgfid", orgFid));
                            oraComm.Parameters.Add(new OracleParameter("appcode", appCode));
                            oraComm.Parameters.Add(new OracleParameter("version", version));
                            oraComm.ExecuteNonQuery();

                        }
                    }
                    oraCon.Close();
                    oraCon.Dispose();
                }

                return Ok();

            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return Problem();
            }

        }

    }
}
