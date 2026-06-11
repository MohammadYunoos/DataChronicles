# Data Chronicles — AI-Powered Ticket Categorization

A working web application that ingests an Excel file of application incidents, categorizes
each ticket with a zero-shot LLM (Facebook **BART-large-MNLI**), and returns a downloadable
Excel report plus an in-browser summary, charts, and an AI assistant — implementing the
*Upgrade to Architect — Data Chronicles* brief.

```
React (Vite + TS)  ──/api──►  .NET 9 API  ──►  Hugging Face BART (zero-shot)
   upload · charts            controllers       └ offline keyword fallback
   chat · progress            EF Core (DB)
                              SignalR (progress)
                              Blob archive (optional)
```

> **Runs locally out of the box.** Every cloud dependency (Entra ID auth, Azure SQL,
> Blob Storage, App Insights, Hugging Face) is **optional and config-driven**. With no
> configuration the app uses an in-memory DB and a built-in offline classifier, so you can
> demo the full flow with zero Azure setup. Add credentials to switch on the real services.

---

## Prerequisites
- **.NET SDK 9** (`dotnet --version`)
- **Node.js 16+** and npm (tested on Node 17 / npm 8)

## Run locally

### 1. Backend (`http://localhost:5279`)
```powershell
cd backend/DataChronicles.Api
dotnet run
```
Swagger UI: <http://localhost:5279/swagger>

### 2. Frontend (`http://localhost:5173`)
```powershell
cd frontend
npm install
npm run dev
```
Open <http://localhost:5173>. The Vite dev server proxies `/api` and `/progressHub`
to the backend automatically.

### Try it
1. Click **“No file? Download a sample input”** (or use your own `.xlsx`).
2. Choose the file → **Upload and Predict**.
3. Watch the progress bar, see the success popup, the **pie-chart summary**, and the
   categorized data table.
4. Click **“download the categorized file”** for the Excel report (Summary sheet first).
5. Ask the **AI Assistant**: *“How many tickets were categorized?”*, *“Most common
   category?”*, *“Severity breakdown”*, *“Any duplicates?”*.

---

## Features implemented
| Requirement | Where |
|---|---|
| Excel upload → categorize → downloadable Excel | `CategorizationController`, `ExcelInputReader/OutputWriter` |
| Zero-shot classification (BART) + offline fallback | `ZeroShotClassifierService` |
| Description cleaning / preprocessing | `TicketProcessingService.CleanDescription` |
| Summary tab as first view + pie chart | `ExcelOutputWriter`, `SummaryView.tsx` |
| Severity prioritization + sentiment | `TextAnalysisService` |
| Duplicate / recurring-issue detection | `ChatService` |
| Completion popup + chat on results | `App.tsx`, `ChatPanel.tsx` |
| Validation-1 (must be non-empty Excel w/ columns) | `ExcelInputReader` |
| Validation-2 (input rows == output rows) | `CategorizationController.Upload` |
| Real-time progress | SignalR `ProgressHub` + frontend |
| DB persistence | EF Core `DataChroniclesDbContext` |
| Blob archival | `BlobStorageService` (optional) |

---

## Enabling the real Azure / AI services
Edit `backend/DataChronicles.Api/appsettings.json` (placeholders are ignored when blank):

| Setting | Effect |
|---|---|
| `HuggingFace:Token` | Use the live **facebook/bart-large-mnli** model instead of the offline classifier |

> **Note on Hugging Face access:** the live model lives at
> `https://router.huggingface.co/hf-inference/models/facebook/bart-large-mnli`.
> On networks where this host is blocked by a corporate proxy (e.g. Zscaler categorizes
> it under "Generative AI and ML Applications" and returns HTTP 403), the app **fails fast
> and automatically falls back to the offline classifier** — categorization still completes.
> To use live BART, run from an unrestricted network (or request a proxy exception) with a
> valid token set in `appsettings.Development.json`.

| `ConnectionStrings:Sql` | Persist to **Azure SQL** instead of in-memory |
| `AzureStorage:BlobConnectionString` | Archive each output file to **Blob Storage** |
| `ApplicationInsights:ConnectionString` | Send telemetry to **App Insights** |
| `Auth:Enabled: true` + `AzureAd:*` | Require **Entra ID** bearer tokens on the API |

---

## API
| Method | Route | Purpose |
|---|---|---|
| GET | `/api/health` | Health check |
| GET | `/api/categorize/sample` | Download a sample input workbook |
| POST | `/api/categorize/upload` | Upload + categorize (returns JSON result) |
| GET | `/api/categorize/download/{batchId}` | Download generated Excel |
| POST | `/api/chat` | Ask the data-grounded assistant |

## Project layout
```
backend/DataChronicles.Api/
  Program.cs                 # DI wiring, optional auth/AI/DB/blob
  Controllers/               # Categorization, Chat
  Services/                  # Classifier, processing, Excel I/O, chat, blob, DB
  Hubs/ProgressHub.cs        # SignalR progress
  Models/Tickets.cs          # Input/Output/Summary models
frontend/
  src/App.tsx                # Upload, progress, toast, layout
  src/components/            # SummaryView (pie), ResultsTable, ChatPanel
  src/api.ts                 # Typed API client
  vite.config.ts             # Dev proxy → :5279
```
