using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using WebCrawlerApp.Models;


namespace WebCrawlerApp.Services
{
    public class SearchService
    {
        // WAL modu eşzamanlı okuma/yazma için en stabil moddur
        private readonly string _dbPath = "Data Source=crawler.db;Cache=Shared;Mode=ReadWriteCreate;";
        private readonly HttpClient _httpClient = new();
        private readonly SemaphoreSlim _throttler = new(5);

        // Dictionary kullanımı silme işlemlerinde çok daha güvenlidir
        public static System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> ActiveCrawls = new();



        public int GetQueueCount()
        {
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_dbPath);
                conn.Open();
                var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT COUNT(*) FROM CrawlQueue WHERE Status = 0", conn);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }


        public SearchService()
        {
            InitDatabase();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0");
        }

        private void InitDatabase()
        {
            using var conn = new SqliteConnection(_dbPath);
            conn.Open();
            // WAL modunu aktif et (Database is locked hatasını önler)
            using (var walCmd = new SqliteCommand("PRAGMA journal_mode=WAL;", conn)) { walCmd.ExecuteNonQuery(); }

            string sql = @"
                CREATE TABLE IF NOT EXISTS CrawlQueue (
                    Url TEXT PRIMARY KEY, OriginUrl TEXT, Depth INTEGER, Status INTEGER DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS IndexResults (
                    Word TEXT, RelevantUrl TEXT, OriginUrl TEXT, Depth INTEGER, Count INTEGER
                );";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
            Console.WriteLine(">>> Veritabanı hazır ve WAL modu aktif.");
        }

        public async Task StartIndex(string origin, int k)
        {
            ActiveCrawls.Clear();
            try
            {
                using (var conn = new SqliteConnection(_dbPath))
                {
                    await conn.OpenAsync();
                    var cmd = new SqliteCommand("INSERT OR IGNORE INTO CrawlQueue (Url, OriginUrl, Depth) VALUES (@u, @o, 0)", conn);
                    cmd.Parameters.AddWithValue("@u", origin);
                    cmd.Parameters.AddWithValue("@o", origin);
                    await cmd.ExecuteNonQueryAsync();
                }
                Console.WriteLine($">>> Tarama başlatıldı: {origin} (Max Depth: {k})");
                _ = Task.Run(() => CrawlerJob(k));
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> StartIndex Hatası: {ex.Message}");
            }
        }

        private async Task CrawlerJob(int maxDepth)
        {
            while (true)
            {
                var item = GetNextFromQueue();
                if (item == null)
                {
                    await Task.Delay(2000);
                    if (GetNextFromQueue() == null)
                    {
                        Console.WriteLine(">>> Kuyruk boşaldı, Job durduruluyor.");
                        break;
                    }
                    continue;
                }

                await _throttler.WaitAsync();
                _ = Task.Run(async () => {
                    try
                    {
                        await ProcessUrl(item, maxDepth);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($">>> Job Hatası ({item.Url}): {ex.Message}");
                    }
                    finally { _throttler.Release(); }
                });
            }
        }
        private async Task ProcessUrl(CrawlTask item, int maxDepth)
        {
            string url = item.Url;

            // LİSTEYE EKLE: Eğer eklenemezse (zaten varsa) bile devam et
            ActiveCrawls.TryAdd(url, DateTime.Now);

            try
            {
                // ... Mevcut GetStringAsync ve IndexContent kodların ...
                string html = await _httpClient.GetStringAsync(url);
                IndexContent(item, html);

                if (item.Depth < maxDepth)
                {
                    var links = ExtractLinks(url, html);
                    AddLinksToQueue(links, item.OriginUrl, item.Depth + 1);
                }
                MarkAsDone(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hata] {url}: {ex.Message}");
                MarkAsDone(url); // Hata olsa da Status=1 yap ki kuyrukta takılmasın
            }
            finally
            {
                
                if (MasterJobs.TryGetValue(item.OriginUrl, out var job))
                {
                    job.DoneCount++;
                    // Eğer her şey bittiyse tamamlandı olarak işaretle
                    if (job.DoneCount >= job.TotalFound) job.IsCompleted = true;
                }
                ActiveCrawls.TryRemove(url, out _);
                _throttler.Release();
            }
        }

        private void IndexContent(dynamic item, string html)
        {
            var noScripts = Regex.Replace(html, @"<(script|style)\b[^>]*?>([\s\S]*?)<\/\1>", " ", RegexOptions.IgnoreCase);
            var cleanText = Regex.Replace(noScripts, "<.*?>", " ");
            var words = cleanText.ToLower().Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ':', '(', ')', '"' }, StringSplitOptions.RemoveEmptyEntries);

            using var conn = new SqliteConnection(_dbPath);
            conn.Open();
            using var trans = conn.BeginTransaction();

            int wordCount = 0;
            foreach (var group in words.Where(w => w.Length > 2).GroupBy(w => w))
            {
                var cmd = new SqliteCommand("INSERT INTO IndexResults (Word, RelevantUrl, OriginUrl, Depth, Count) VALUES (@w, @r, @o, @d, @c)", conn, trans);
                cmd.Parameters.AddWithValue("@w", group.Key);
                cmd.Parameters.AddWithValue("@r", (string)item.Url);
                cmd.Parameters.AddWithValue("@o", (string)item.OriginUrl);
                cmd.Parameters.AddWithValue("@d", (int)item.Depth);
                cmd.Parameters.AddWithValue("@c", group.Count());
                cmd.ExecuteNonQuery();
                wordCount++;
            }
            trans.Commit();
            Console.WriteLine($"    Indekslendi: {wordCount} benzersiz kelime.");
        }

        public List<IndexEntry> Search(string query)
        {
            var results = new List<IndexEntry>();
            try
            {
                using var conn = new SqliteConnection(_dbPath);
                conn.Open();
                var cmd = new SqliteCommand("SELECT RelevantUrl, OriginUrl, Depth, Count FROM IndexResults WHERE Word = @q ORDER BY Count DESC", conn);
                cmd.Parameters.AddWithValue("@q", query.ToLower());
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new IndexEntry { RelevantUrl = reader.GetString(0), OriginUrl = reader.GetString(1), Depth = reader.GetInt32(2), WordCount = reader.GetInt32(3) });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($">>> Arama Hatası: {ex.Message}");
            }
            return results;
        }
        public class CrawlTask
        {
            public string Url { get; set; }
            public string OriginUrl { get; set; }
            public int Depth { get; set; }
        }



        public class CrawlJobStatus
        {
            public string OriginUrl { get; set; } = "";
            public int TotalFound { get; set; }  // Bulunan toplam link sayısı
            public int DoneCount { get; set; }   // İşlemi biten link sayısı
            public bool IsCompleted { get; set; } = false;
        }

        private CrawlTask? GetNextFromQueue()
        {
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_dbPath);
                conn.Open();

                // 1. Önce bekleyen bir linki seç ve durumunu HEMEN '2' (In Progress) yap
                // Bu işlem SQLite'ın 'UPDATE ... RETURNING' veya kilit mekanizmasıyla en güvenli hale gelir
                using var trans = conn.BeginTransaction();

                var selectCmd = new Microsoft.Data.Sqlite.SqliteCommand(
                    "SELECT Url, OriginUrl, Depth FROM CrawlQueue WHERE Status = 0 LIMIT 1", conn, trans);

                using var reader = selectCmd.ExecuteReader();
                if (reader.Read())
                {
                    var task = new CrawlTask
                    {
                        Url = reader.GetString(0),
                        OriginUrl = reader.GetString(1),
                        Depth = reader.GetInt32(2)
                    };

                    // 2. Seçilen linki rezerve et
                    var updateCmd = new Microsoft.Data.Sqlite.SqliteCommand(
                        "UPDATE CrawlQueue SET Status = 2 WHERE Url = @u", conn, trans);
                    updateCmd.Parameters.AddWithValue("@u", task.Url);
                    updateCmd.ExecuteNonQuery();

                    trans.Commit();
                    return task;
                }
            }
            catch { }
            return null;
        }


        // Tüm görevleri hafızada tutan liste
        public static ConcurrentDictionary<string, CrawlJobStatus> MasterJobs = new();

        private string NormalizeUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string normalized = uri.GetLeftPart(UriPartial.Path).ToLower().TrimEnd('/');
                return normalized;
            }
            catch { return url.ToLower().TrimEnd('/'); }
        }


        private void MarkAsDone(string url)
        {
            try
            {
                using var conn = new SqliteConnection(_dbPath);
                conn.Open();
                var cmd = new SqliteCommand("UPDATE CrawlQueue SET Status = 1 WHERE Url = @u", conn);
                cmd.Parameters.AddWithValue("@u", url);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
        private void AddLinksToQueue(List<string> links, string origin, int depth)
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(_dbPath);
            conn.Open();
            using var trans = conn.BeginTransaction();

            foreach (var link in links)
            {
                // Hiç dokunmadan olduğu gibi sorguluyoruz
                var checkCmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT COUNT(*) FROM CrawlQueue WHERE Url = @u", conn, trans);
                checkCmd.Parameters.AddWithValue("@u", link);
                if (Convert.ToInt32(checkCmd.ExecuteScalar()) == 0)
                {
                    var cmd = new Microsoft.Data.Sqlite.SqliteCommand("INSERT INTO CrawlQueue (Url, OriginUrl, Depth, Status) VALUES (@u, @o, @d, 0)", conn, trans);
                    cmd.Parameters.AddWithValue("@u", link);
                    cmd.Parameters.AddWithValue("@o", origin);
                    cmd.Parameters.AddWithValue("@d", depth);
                    cmd.ExecuteNonQuery();
                }
            }
            if (MasterJobs.TryGetValue(origin, out var job))
            {
                job.TotalFound += links.Count;
            }
            trans.Commit();
        }

        private List<string> ExtractLinks(string baseUrl, string html)
        {
            var found = new List<string>();
            var matches = Regex.Matches(html, @"href=[""'](?<url>.*?)[""']", RegexOptions.IgnoreCase);
            Uri baseUri = new Uri(baseUrl);

            foreach (Match m in matches)
            {
                try
                {
                    string rawLink = m.Groups["url"].Value;
                    if (string.IsNullOrWhiteSpace(rawLink) || rawLink.StartsWith("#")) continue;

                    Uri resolvedUri = new Uri(baseUri, rawLink);

                    // Sadece aynı domain ve http/s kontrolü yap, slash temizliği YAPMA
                    if (resolvedUri.Host == baseUri.Host && resolvedUri.Scheme.StartsWith("http") && !IsStatic(resolvedUri.AbsoluteUri))
                    {
                        found.Add(resolvedUri.AbsoluteUri); // AbsoluteUri olduğu gibi (slash dahil) gelir
                    }
                }
                catch { }
            }
            return found.Distinct().ToList();
        }

        private bool IsStatic(string u) => u.EndsWith(".css") || u.EndsWith(".js") || u.EndsWith(".png") || u.EndsWith(".jpg") || u.EndsWith(".ico");
    }
}