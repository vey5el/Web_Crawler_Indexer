# 🕸️ Crawler Engine: High-Performance Web Indexer

A modern, asynchronous web crawler built with ASP.NET Core MVC and SQLite. This project demonstrates advanced concepts like Back Pressure management, Atomic Queue processing, and Real-time Progress tracking.

---

## 🚀 Key Features

- **Asynchronous Crawling**: Uses `Task.Run` and `HttpClient` to fetch pages without blocking the UI.
- **Back Pressure Control**: Implements `SemaphoreSlim` to limit concurrent outgoing requests (default: 5), preventing server bans and CPU spikes.
- **SQLite Triple Indexing**: Stores data in a "Triple" format: `(Word, RelevantUrl, OriginUrl, Depth)`.
- **Real-time Dashboard**: A dynamic AJAX-powered UI that shows active jobs, progress bars, and queue status without page refreshes.
- **Atomic Queue Management**: Prevents duplicate processing of the same URL across multiple threads using a "Select & Lock" database pattern.
- **Search Engine**: Instant search functionality that ranks results based on word frequency and page depth.

---

## 🛠️ Tech Stack

- **Backend**: .NET 8 / ASP.NET Core MVC  
- **Database**: SQLite (`Microsoft.Data.Sqlite`)  
- **Frontend**: Bootstrap 5, Vanilla JavaScript (AJAX / Fetch API)  
- **Architecture**: Service-Oriented Architecture (SOA) with a Singleton Search Service  

---

## ⚙️ Installation & Setup

### 1. Clone the repository

```bash
git clone https://github.com/yourusername/CrawlerEngine.git
```

### 2. Install Dependencies

```bash
dotnet restore
```

### 3. Run the Application

```bash
dotnet run
```

### 4. Database

The system will automatically create a `crawler.db` file in your execution directory on the first run.  
No SQL Server installation is required.

---

## 📖 How It Works

1. **Input**: Enter a Starting URL (Origin) and a Depth (`k`).
2. **Discovery**: The crawler visits the origin, extracts all internal links, and adds them to the CrawlQueue with `Depth + 1`.
3. **Indexing**: For every page visited, the engine cleans the HTML, tokenizes words, and stores the frequency count in the `IndexResults` table.
4. **Concurrency**: 5 worker threads pull from the queue simultaneously. They "lock" a row by setting `Status = 2` so no two workers process the same page.
5. **Search**: Users can search for terms while the crawler is still running (Active Indexing).
