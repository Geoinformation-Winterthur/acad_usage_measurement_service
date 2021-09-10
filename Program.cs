// <copyright company="Vermessungsamt Winterthur">
// Author: Edgar Butwilowski
// Copyright (c) 2021 Vermessungsamt Winterthur. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace win.acad_usage_measurement
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                    .UseIISIntegration()
                    .UseStartup<Startup>();
                });

    }
}
