// <copyright company="Vermessungsamt Winterthur">
// Author: Edgar Butwilowski
// Copyright (c) 2021 Vermessungsamt Winterthur. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oracle.ManagedDataAccess.Client;

namespace win.acad_usage_measurement
{
    public class Startup
    {

        public static string oraConString;
        private static readonly object syncMonitor = new object();

        internal static string acadUserTableName;
        internal static string userNameColumn;
        internal static string domainNameColumn;
        internal static string lastPingColumn;
        internal static string userTableName;
        internal static string userLoginColumn;
        internal static string userFidOrganisationColumn;
        internal static string applicationsTableName;
        internal static string appNameColumn;
        internal static string unknownValue;
        internal static string usageDataTableName;
        internal static string dateOfUsageColumn;
        internal static string applicationFidColumn;
        internal static string appVersionColumn;
        internal static string minutesColumn;
        internal static string organisationTableName;
        internal static string organisationNameColumn;
        internal static string organisationShortNameColumn;
        internal static string organisationFidColumn;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;

            // read configuration file values:
            Startup.acadUserTableName = Configuration["SchemaConfiguration:AcadUserTableName"];
            Startup.userNameColumn = Configuration["SchemaConfiguration:UserNameColumn"];
            Startup.domainNameColumn = Configuration["SchemaConfiguration:DomainNameColumn"];
            Startup.lastPingColumn = Configuration["SchemaConfiguration:LastPingColumn"];
            Startup.userTableName = Configuration["SchemaConfiguration:UserTableName"];
            Startup.userLoginColumn = Configuration["SchemaConfiguration:UserLoginColumn"];
            Startup.userFidOrganisationColumn = Configuration["SchemaConfiguration:UserFidOrganisationColumn"];
            Startup.applicationsTableName = Configuration["SchemaConfiguration:ApplicationsTableName"];
            Startup.appNameColumn = Configuration["SchemaConfiguration:AppNameColumn"];
            Startup.unknownValue = Configuration["SchemaConfiguration:UnknownValue"];
            Startup.usageDataTableName = Configuration["SchemaConfiguration:UsageDataTableName"];
            Startup.dateOfUsageColumn = Configuration["SchemaConfiguration:DateOfUsageColumn"];
            Startup.applicationFidColumn = Configuration["SchemaConfiguration:ApplicationFidColumn"];
            Startup.appVersionColumn = Configuration["SchemaConfiguration:AppVersionColumn"];
            Startup.minutesColumn = Configuration["SchemaConfiguration:MinutesColumn"];
            Startup.organisationTableName = Configuration["SchemaConfiguration:OrganisationTableName"];
            Startup.organisationNameColumn = Configuration["SchemaConfiguration:OrganisationNameColumn"];
            Startup.organisationShortNameColumn = Configuration["SchemaConfiguration:OrganisationShortNameColumn"];
            Startup.organisationFidColumn = Configuration["SchemaConfiguration:OrganisationFidColumn"];


            string oraUser = Configuration["OraLogin:user"];
            string oraPassword = Configuration["OraLogin:password"];
            string tnsAlias = Configuration["OraLogin:tnsalias"];
            Startup.oraConString = "User Id=" + oraUser + ";Password=" + oraPassword +
                            ";Data Source=" + tnsAlias;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.Configure<IISServerOptions>(options =>
            {
                options.AutomaticAuthentication = false;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            createDbSchema();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }
            // app.UseStaticFiles();

            app.UseRouting();

            // app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        private static void createDbSchema()
        {

            // public static OracleConnection oraCon;


            using (OracleConnection oraCon = new OracleConnection(oraConString))
            {
                oraCon.Open();

                using (OracleCommand oraComm = oraCon.CreateCommand())
                {
                    try
                    {
                        oraComm.CommandText = "CREATE TABLE " + acadUserTableName + "(fid NUMBER(10,0) NOT NULL, " +
                            userNameColumn + " VARCHAR2(255), " + domainNameColumn + " VARCHAR2(255), " + lastPingColumn + " DATE, " +
                            "CONSTRAINT " + acadUserTableName + "_P PRIMARY KEY (fid), " +
                            "CONSTRAINT " + acadUserTableName + "_U UNIQUE (" + userNameColumn +", " + domainNameColumn + "))";
                        oraComm.ExecuteNonQuery();

                        oraComm.CommandText = "CREATE SEQUENCE " + acadUserTableName + "_seq START WITH 1";
                        oraComm.ExecuteNonQuery();

                        oraComm.CommandText = "CREATE OR REPLACE TRIGGER " + acadUserTableName + "_bir BEFORE INSERT " +
                            "ON " + acadUserTableName + " FOR EACH ROW " +
                            "BEGIN SELECT " + acadUserTableName + "_seq.NEXTVAL INTO :new.fid FROM dual; END;";
                        oraComm.ExecuteNonQuery();

                    }
                    catch (System.Exception ex)
                    {
                        // do nothing
                    }

                    try
                    {
                        oraComm.CommandText = "CREATE TABLE " + applicationsTableName + "(" +
                                "fid NUMBER(10,0) NOT NULL, " +
                                appNameColumn + " VARCHAR2(255), " +
                                "CONSTRAINT " + applicationsTableName + "_P PRIMARY KEY (fid))";
                        oraComm.ExecuteNonQuery();
                    }
                    catch (System.Exception ex)
                    {
                        // do nothing
                    }



                    lock (Startup.syncMonitor)
                    {
                        oraComm.CommandText = "SELECT count(*) FROM " + applicationsTableName;
                        bool hasAppEntries = false;
                        using (OracleDataReader oraReader = oraComm.ExecuteReader())
                        {
                            if (oraReader.Read())
                            {
                                int countAppEntries = oraReader.GetInt32(0);
                                if (countAppEntries != 0)
                                {
                                    hasAppEntries = true;
                                }
                            }
                        }
                        if (!hasAppEntries)
                        {
                            using (OracleTransaction trans = oraCon.BeginTransaction())
                            {
                                try
                                {
                                    oraComm.CommandText = "INSERT INTO " + applicationsTableName + "(" +
                                         "fid, " + appNameColumn + ") VALUES(" +
                                         "0, '" + unknownValue + "')";
                                    oraComm.ExecuteNonQuery();
                                    oraComm.CommandText = "INSERT INTO " + applicationsTableName + "(" +
                                         "fid, " + appNameColumn + ") VALUES(" +
                                         "1, 'AutoCAD')";
                                    oraComm.ExecuteNonQuery();
                                    oraComm.CommandText = "INSERT INTO " + applicationsTableName + "(" +
                                         "fid, " + appNameColumn + ") VALUES(" +
                                         "2, 'AutoCAD Map')";
                                    oraComm.ExecuteNonQuery();
                                    oraComm.CommandText = "INSERT INTO " + applicationsTableName + "(" +
                                         "fid, " + appNameColumn + ") VALUES(" +
                                         "3, 'Civil 3D')";
                                    oraComm.ExecuteNonQuery();
                                    trans.Commit();
                                }
                                catch (System.Exception ex)
                                {
                                    trans.Rollback();
                                }
                            }
                        }
                    }

                    try
                    {
                        oraComm.CommandText = "CREATE TABLE " + usageDataTableName + "(" +
                         "fid NUMBER(10,0) NOT NULL, " + dateOfUsageColumn + " DATE, " +
                         applicationFidColumn + " NUMBER(10,0), " + appVersionColumn + " VARCHAR2(255), " +
                         minutesColumn + " NUMBER(*,0), " + organisationFidColumn + " NUMBER(10,0), " +
                         "CONSTRAINT " + usageDataTableName + "_P PRIMARY KEY (fid), " +
                         "CONSTRAINT fk_" + applicationFidColumn + " FOREIGN KEY(" + applicationFidColumn + ") REFERENCES " + 
                            applicationsTableName + "(fid), " +
                         "CONSTRAINT fk_" + organisationFidColumn + " FOREIGN KEY(" + organisationFidColumn + ") REFERENCES " +
                            organisationTableName + "(fid))";
                        oraComm.ExecuteNonQuery();

                        oraComm.CommandText = "CREATE SEQUENCE " + usageDataTableName + "_seq START WITH 1";
                        oraComm.ExecuteNonQuery();

                        oraComm.CommandText = "CREATE OR REPLACE TRIGGER " + usageDataTableName + "_bir BEFORE INSERT "+
                            "ON " + usageDataTableName + " FOR EACH ROW " +
                            "BEGIN SELECT " + usageDataTableName + "_seq.NEXTVAL INTO :new.fid FROM dual; END;";
                        oraComm.ExecuteNonQuery();


                    }
                    catch (System.Exception ex)
                    {
                        // do nothing
                    }

                    try
                    {
                        oraComm.CommandText = "INSERT INTO " + organisationTableName + "(FID, " 
                            + organisationNameColumn + ", " + organisationShortNameColumn + ") " +
                            "VALUES(-1, '" + unknownValue + "', '" + unknownValue + "')";
                        oraComm.ExecuteNonQuery();
                    }
                    catch (System.Exception ex)
                    {
                        // do nothing
                    }

                    oraCon.Close();
                    oraCon.Dispose();
                }
            }


        }

    }
}
