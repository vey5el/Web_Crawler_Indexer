using Microsoft.AspNetCore.Mvc;
using WebCrawlerApp.Models;
using WebCrawlerApp.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static WebCrawlerApp.Services.SearchService;

namespace WebCrawlerApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly SearchService _searchService;

        // Constructor: Dependency Injection ile servisimizi alıyoruz
        public HomeController(SearchService searchService)
        {
            _searchService = searchService;
        }

        // Ana Sayfa: Formların göründüğü yer
        [HttpGet]
        public IActionResult Index()
        {
            ViewBag.ActiveCrawls = SearchService.ActiveCrawls.ToList();
            ViewBag.QueueCount = _searchService.GetQueueCount();
            
            return View();
        }

        // Indexleme (Crawl) Başlatma
        [HttpPost]
        public async Task<IActionResult> StartIndex(string origin, int k)
        {
            if (!string.IsNullOrEmpty(origin))
            {
                MasterJobs.TryAdd(origin, new CrawlJobStatus { OriginUrl = origin, TotalFound = 1, DoneCount = 0 });
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _searchService.StartIndex(origin, k);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Tarama hatası: {ex.Message}");
                    }
                });

                TempData["Message"] = "Tarama arka planda başlatıldı. Veriler veritabanına işleniyor...";
            }
            return RedirectToAction("Index");
        }





        [HttpGet]
        public JsonResult GetCrawlerStatus() // IActionResult yerine JsonResult garantidir
        {
            var jobs = SearchService.MasterJobs.Values.Select(j => new {
                originUrl = j.OriginUrl,
                done = j.DoneCount,
                total = j.TotalFound,
                percent = j.TotalFound > 0 ? (int)((double)j.DoneCount / j.TotalFound * 100) : 0,
                status = j.IsCompleted ? "Active" : "Done"
            }).ToList();

            return Json(jobs);
        }

        // Arama İşlemi
        [HttpGet]
        public IActionResult Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return RedirectToAction("Index");
            }

            // Servis üzerinden Triple (relevant_url, origin, depth) sonuçlarını alıyoruz
            var results = _searchService.Search(query);

            ViewBag.Results = results;
            ViewBag.Query = query;

            return View("Index");
        }
    }
}