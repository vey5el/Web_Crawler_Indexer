# 🕸️ Web Crawler Projesi (C# .NET 8)

---

## 🎯 1. Objective

Build a single-machine web crawler in **C# .NET 8** that indexes pages up to depth `$k$` and allows concurrent searching.  
Use **Native .NET libraries only** (no third-party crawlers).

---

## 🧰 2. Technical Stack

- ⚙️ **Runtime:** .NET 8 (Console Application)
- 🔄 **Concurrency:**  
  - `System.Threading.Channels` (for back-pressure)  
  - `Task` (for worker pool)
- 🌐 **Networking:** `HttpClient`
- 🔍 **Parsing:**  
  - `Regex`  
  - or `string.IndexOf` / `Substring`
- 💾 **Storage:** Flat-file JSON (`System.Text.Json`)

---

## 🧠 3. Core Functional Specs

### 📥 3.1 The Indexer (`index`)

#### Inputs
- `string originUrl`
- `int maxDepth`

#### 🚦 Back-Pressure
- Implement a `BoundedChannel<CrawlTask>` with a capacity (e.g., 1000)
- If the queue is full, the link extractor must wait (**async blocking**)

#### 👷 Worker Pool
- Spawn a fixed number of workers (e.g., 10)

#### ⚙️ Logic
- Maintain a `ConcurrentDictionary<string, byte>` as visited set
- Extract links only if `currentDepth < maxDepth`
- Store results in `ConcurrentDictionary<string, CrawlResult>`

#### 💾 Persistence
- Save to `index.json` every `$N$` pages or at completion

---

### 🔎 3.2 The Searcher (`search`)

#### Input
- `string query`

#### Output
- List of tuples:
  - `(relevantUrl, originUrl, depth)`

#### 🔄 Concurrency
- Must iterate while Indexer is writing (thread-safe)

#### 🎯 Relevancy
```csharp
content.Contains(query, StringComparison.OrdinalIgnoreCase)
```

---

### 🧩 3.3 State & Models

```csharp
public class CrawlTask
{
    public string Url { get; set; }
    public string Origin { get; set; }
    public int Depth { get; set; }
}

public class IndexEntry
{
    public string Url { get; set; }
    public string Origin { get; set; }
    public int Depth { get; set; }
    public string Content { get; set; }
}
```

---

## 🖥️ 4. CLI & Observability

### 📊 Dashboard Output

- 🟢 **Status:** Indexing / Idle
- 📈 **Metrics:**
  - Total Indexed
  - Queue Depth
  - Active Workers

### ⌨️ Interactive Search
- User can type queries while crawler is running

---

## 🚀 Bonus Ideas

- 🔐 Domain filtering
- 🧹 HTML cleanup before indexing
- 🗂️ Simple ranking (TF-like scoring)
- 💡 Highlight matched keywords in results

---

> ✨ Clean, concurrent, and fully native .NET crawler design.

