// <copyright company="Vermessungsamt Winterthur">
// Author: Edgar Butwilowski
// Copyright (c) 2021 Vermessungsamt Winterthur. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace win.acad_usage_measurement.Controllers
{
    [Route("/")]
    [ApiController]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            object response = new
            {
                message = "Service works."
            };
            return Ok(response);
        }

    }
}
