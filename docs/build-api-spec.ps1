<#
  build-api-spec.ps1
  Generates the Data Chronicles API Specification (.docx) via Word COM automation,
  mirroring the corporate "API Specification vX.X" template layout.

  Output: docs/DataChronicles-API-Specification-v1.0.docx
  Re-runnable. Requires Microsoft Word installed (COM). No package install needed.
#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outPath   = Join-Path $scriptDir 'DataChronicles-API-Specification-v1.0.docx'

# ---- Word COM constants ----
$wdStory                 = 6
$wdPageBreak             = 7
$wdAlignParagraphCenter  = 1
$wdAlignParagraphLeft    = 0
$wdFormatDocumentDefault = 16

Write-Host "Launching Word..."
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
$doc = $word.Documents.Add()
$sel = $word.Selection

# ---------- helpers ----------
function Go-End { $word.Selection.EndKey($wdStory) | Out-Null }

function Add-Heading([string]$text, [int]$level) {
    Go-End
    $s = $word.Selection
    $s.Style = "Heading $level"
    $s.TypeText($text)
    $s.TypeParagraph()
    $s.Style = 'Normal'
}

function Add-Para([string]$text, [bool]$bold = $false) {
    Go-End
    $s = $word.Selection
    $s.Style = 'Normal'
    $s.Font.Bold = [int]$bold
    $s.TypeText($text)
    $s.Font.Bold = 0
    $s.TypeParagraph()
}

function Add-Label([string]$label, [string]$value) {
    # "Label: value" with the label in bold
    Go-End
    $s = $word.Selection
    $s.Style = 'Normal'
    $s.Font.Bold = 1
    $s.TypeText($label)
    $s.Font.Bold = 0
    $s.TypeText($value)
    $s.TypeParagraph()
}

function Add-Json([string]$json) {
    Go-End
    $s = $word.Selection
    $s.Style = 'Normal'
    $s.ParagraphFormat.SpaceAfter = 0
    $s.Font.Name = 'Consolas'
    $s.Font.Size = 9
    $lines = $json -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $s.TypeText(($lines[$i].TrimEnd("`r")))
        if ($i -lt $lines.Count - 1) { $s.TypeText([char]11) }  # soft line break, one block
    }
    $s.TypeParagraph()
    $s.Font.Name = 'Calibri'
    $s.Font.Size = 11
    $s.ParagraphFormat.SpaceAfter = 8
}

function Add-Table([string[]]$headers, [object[]]$rows) {
    Go-End
    $range = $word.Selection.Range
    $nCols = $headers.Count
    $nRows = $rows.Count + 1
    $table = $doc.Tables.Add($range, $nRows, $nCols)
    $table.Borders.Enable = $true
    $table.Range.Font.Size = 10
    $table.Range.Font.Name = 'Calibri'
    for ($c = 0; $c -lt $nCols; $c++) {
        $cell = $table.Cell(1, $c + 1)
        $cell.Range.Text = $headers[$c]
        $cell.Range.Font.Bold = $true
        $cell.Shading.BackgroundPatternColor = 14277081  # light blue-grey
    }
    for ($r = 0; $r -lt $rows.Count; $r++) {
        for ($c = 0; $c -lt $nCols; $c++) {
            $val = [string]$rows[$r][$c]
            $table.Cell($r + 2, $c + 1).Range.Text = $val
        }
    }
    Go-End
    $word.Selection.TypeParagraph()
}

# ============================================================
# COVER PAGE
# ============================================================
$sel.ParagraphFormat.Alignment = $wdAlignParagraphCenter
1..6 | ForEach-Object { $sel.TypeParagraph() }
$sel.Font.Size = 26
$sel.Font.Bold = 1
$sel.TypeText('Cognizant / Data Chronicles')
$sel.TypeParagraph()
$sel.TypeText('API Specification')
$sel.TypeParagraph()
$sel.Font.Bold = 0
$sel.Font.Size = 11
1..10 | ForEach-Object { $sel.TypeParagraph() }
$sel.Font.Size = 12
$sel.TypeText('Version: 1.0')
$sel.TypeParagraph()
$sel.TypeText('Date: 18/06/2026')
$sel.TypeParagraph()
$sel.Font.Size = 11
$sel.ParagraphFormat.Alignment = $wdAlignParagraphLeft
$sel.InsertBreak($wdPageBreak)

# ============================================================
# DOCUMENT REVISION HISTORY
# ============================================================
Add-Heading 'Document Revision History' 2
Add-Table @('Date','Version','SDLC Phase / Revision Description','Prepared By','Approved By') @(
    ,@('18/06/2026','1.0','Initial draft - API specification','Mohammad Yunoos Shiddique','')
)
$sel.InsertBreak($wdPageBreak)

# ============================================================
# TABLE OF CONTENTS
# ============================================================
Add-Heading 'Table of Contents' 2
Go-End
$tocRange = $word.Selection.Range
$toc = $doc.TablesOfContents.Add($tocRange, $true, 1, 3)
Go-End
$sel.TypeParagraph()
$sel.InsertBreak($wdPageBreak)

# ============================================================
# 1 INTRODUCTION
# ============================================================
Add-Heading '1  Introduction' 1

Add-Heading '1.1  Background' 2
Add-Para 'Data Chronicles is an AI-powered ticket-categorization platform. Users upload an Excel workbook of support / operations tickets; the service cleans each description, classifies it into one of the standard categories using a zero-shot model (Hugging Face BART, with a deterministic internal fallback), and enriches each record with a severity and sentiment.'
Add-Para 'Beyond classification, the service flags duplicate tickets (comparing each new ticket against the current batch and previously uploaded history) and groups similar issues into clusters so that recurring, fundamental problems surface. Results are returned as JSON for the React UI, persisted to the database, and archived as a formatted Excel workbook (summary, per-ticket detail and issue-group sheets). A chat assistant answers natural-language questions grounded in the categorized data.'
Add-Para 'This document specifies the HTTP API exposed by the Data Chronicles backend (.NET 9). Public access is fronted by Azure API Management (APIM); the backend App Service is protected by Entra ID Easy Auth.'

Add-Heading '1.2  Assumptions and Constraints' 2
Add-Table @('#','Assumption / Constraint') @(
    @('1','Input is an Excel (.xlsx) workbook containing the columns: Description, ApplicationName, Incident, JobName. Missing required columns return HTTP 400.'),
    @('2','Maximum upload size is 20 MB per request.'),
    @('3','Classification uses Hugging Face BART when configured; otherwise a deterministic internal classifier is used. The chosen engine is reported per batch (BART / Internal / Mixed).'),
    @('4','Semantic duplicate detection and grouping use Azure AI embeddings when configured; otherwise a deterministic JobName+Category key is used.'),
    @('5','Persistence is an in-memory store for local development and Azure SQL in cloud environments.'),
    @('6','All endpoints are exposed publicly through APIM and require a subscription key. The backend additionally requires a managed-identity token from APIM.')
)
$sel.InsertBreak($wdPageBreak)

# ============================================================
# 2 API SECURITY GENERAL FRAMEWORK
# ============================================================
Add-Heading '2  API Security General Framework' 1
Add-Para 'The Data Chronicles API uses a two-hop security model:'
Add-Para 'Hop 1 - Client to APIM: Every request must include a valid APIM subscription key in the Ocp-Apim-Subscription-Key HTTP header. Requests without a valid key are rejected by the gateway with HTTP 401.'
Add-Para 'Hop 2 - APIM to backend: APIM attaches a managed-identity bearer token to the inbound request (authentication-managed-identity policy) before forwarding it to the backend App Service. The App Service Easy Auth layer validates the token audience and the allowed client application (the APIM managed identity). Direct calls to the backend that bypass APIM are rejected.'
Add-Para 'Transport is HTTPS/TLS end-to-end. Subscription keys are issued per consumer in APIM and can be regenerated independently. Backend secrets (Hugging Face token, Azure AI key, SQL connection string) are stored in Azure Key Vault and referenced by the App Service - never embedded in client requests.'
Add-Para 'The detailed, validated configuration (Easy Auth allowed audiences / client application, the managed-identity policy, and the Postman setup) is documented in docs/manual-provisioning-portal.md (Step 9 runbook).'
$sel.InsertBreak($wdPageBreak)

# ============================================================
# 3 API SPECIFICATIONS
# ============================================================
Add-Heading '3  API Specifications' 1
Add-Heading '3.1  API Details' 2

# ---- endpoint data model ----
$apis = @(
    @{
        Num='3.1.1'; Title='Health Check'
        Uri='/api/health'; Method='GET'
        ReqJson=$null
        ReqDesc=$null
        RespJson=@'
{
  "status": "healthy",
  "time": "2026-06-18T09:30:00Z"
}
'@
        RespDesc=@(
            @('status','Service health indicator. "healthy" when the API is up.'),
            @('time','UTC timestamp (ISO 8601) at which the health check was evaluated.')
        )
        Steps=@(
            'APIM validates the subscription key and forwards the request with a managed-identity token.',
            'The backend returns a fixed health payload without touching the database or any AI service.',
            'Used by monitoring / availability probes.'
        )
    },
    @{
        Num='3.1.2'; Title='Ask AI Assistant (Chat)'
        Uri='/api/chat'; Method='POST'
        ReqJson=@'
{
  "question": "What are the top recurring issues?",
  "batchId": "a1b2c3d4"
}
'@
        ReqDesc=@(
            @('question','Required. Natural-language question about the categorized tickets.'),
            @('batchId','Optional. Restricts the answer to a single categorization batch. When omitted, all tickets are considered.')
        )
        RespJson=@'
{
  "answer": "The most frequent category is 'Job Failure' (42%), driven by the NightlyBatch job. 7 duplicates were flagged in the latest batch."
}
'@
        RespDesc=@(
            @('answer','The assistant''s response. Generated by the configured Azure AI chat model when available, grounded in the ticket data; otherwise a deterministic rule-based summary.')
        )
        Steps=@(
            'APIM validates the subscription key and forwards the request.',
            'The backend loads the relevant tickets (all, or scoped to batchId) and builds a grounding summary.',
            'If Azure AI chat is configured, the question + grounding context are sent to the LLM and its answer is returned; otherwise a rule-based answer is produced.'
        )
    },
    @{
        Num='3.1.3'; Title='Upload and Categorize'
        Uri='/api/categorize/upload?connectionId={connectionId}'; Method='POST'
        ContentType='multipart/form-data'
        ReqJson=@'
Content-Type: multipart/form-data

Form field:
  file         (required)  The .xlsx workbook to categorize.
                           Columns: Description, ApplicationName, Incident, JobName

Query string:
  connectionId (optional)  SignalR connection id to stream progress (0-100%).
'@
        ReqDesc=@(
            @('file','Required (multipart form field). Excel (.xlsx) workbook. Must contain columns Description, ApplicationName, Incident, JobName. Max 20 MB.'),
            @('connectionId','Optional (query string). The SignalR connection id of the caller; when supplied, progress percentage events are pushed to that connection via the /progressHub hub.')
        )
        RespJson=@'
{
  "batchId": "a1b2c3d4",
  "totalRecords": 50,
  "tickets": [
    {
      "id": 1,
      "applicationName": "OrderService",
      "incident": "INC100",
      "jobName": "NightlyBatch",
      "category": "Job Failure",
      "confidence": 0.93,
      "severity": "High",
      "sentiment": "Negative",
      "source": "BART",
      "isDuplicate": false,
      "duplicateOf": null,
      "embedding": "[0.012,-0.034, ...]",
      "batchId": "a1b2c3d4",
      "createdOn": "2026-06-18T09:30:00Z"
    }
  ],
  "summary": [
    { "category": "Job Failure", "count": 21, "percentage": 42.0 }
  ],
  "source": "BART",
  "duplicateCount": 7,
  "groups": [
    {
      "signature": "Job Failure / NightlyBatch",
      "category": "Job Failure",
      "count": 5,
      "representativeIncident": "INC100"
    }
  ],
  "fileName": "test_categories_a1b2c3d4.xlsx"
}
'@
        RespDesc=@(
            @('batchId','Unique 8-character identifier for this categorization run. Used to download the output workbook.'),
            @('totalRecords','Number of input tickets processed.'),
            @('tickets[]','Array of categorized tickets (see fields below).'),
            @('tickets[].id','Database identifier.'),
            @('tickets[].applicationName','Application name (echoed from input).'),
            @('tickets[].incident','Incident identifier (echoed from input).'),
            @('tickets[].jobName','Job / task name (echoed from input).'),
            @('tickets[].category','Assigned category.'),
            @('tickets[].confidence','Classification confidence (0.0 - 1.0).'),
            @('tickets[].severity','Derived severity: High / Medium / Low.'),
            @('tickets[].sentiment','Derived sentiment: Positive / Neutral / Negative.'),
            @('tickets[].source','Engine that classified this ticket: BART or Internal.'),
            @('tickets[].isDuplicate','True if this ticket matches an earlier ticket in the batch or in history.'),
            @('tickets[].duplicateOf','Incident id of the matched existing ticket, or null.'),
            @('tickets[].embedding','JSON-serialized embedding vector (present only when Azure AI embeddings are enabled).'),
            @('tickets[].batchId','Batch this ticket belongs to.'),
            @('tickets[].createdOn','UTC creation timestamp.'),
            @('summary[]','Per-category aggregates: category, count, percentage.'),
            @('source','Batch-level engine: BART, Internal, or Mixed.'),
            @('duplicateCount','Number of tickets flagged as duplicates.'),
            @('groups[]','Recurring-issue clusters: signature, category, count, representativeIncident.'),
            @('fileName','Generated output workbook name (test_categories_{batchId}.xlsx).')
        )
        Steps=@(
            'APIM validates the subscription key and forwards the multipart request.',
            'The backend reads and validates the workbook (400 if required columns are missing).',
            'Each description is cleaned, classified (BART or internal), and scored for severity/sentiment.',
            'Duplicates are flagged and similar issues grouped (semantic via embeddings, else deterministic key).',
            'Results are persisted, an Excel workbook is generated and archived (Blob, if configured), and the full result is returned as JSON. A 500 is returned if input/output record counts diverge.'
        )
    },
    @{
        Num='3.1.4'; Title='Download Output Workbook'
        Uri='/api/categorize/download/{batchId}'; Method='GET'
        ReqJson=@'
Path parameter:
  batchId (required)  The batchId returned by the upload endpoint.
'@
        ReqDesc=@(
            @('batchId','Required (path). The 8-character batch identifier returned by /api/categorize/upload.')
        )
        RespJson=@'
HTTP/1.1 200 OK
Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
Content-Disposition: attachment; filename="test_categories_a1b2c3d4.xlsx"

<binary .xlsx file stream>
'@
        RespDesc=@(
            @('(binary)','The generated Excel workbook (Summary, Results and Issue Groups sheets). Returns 404 if no file exists for the supplied batchId.')
        )
        Steps=@(
            'APIM validates the subscription key and forwards the request.',
            'The backend looks up the archived workbook for the batchId.',
            'The .xlsx is streamed back as a file download, or 404 if not found.'
        )
    },
    @{
        Num='3.1.5'; Title='Download Sample Input'
        Uri='/api/categorize/sample'; Method='GET'
        ReqJson=$null
        ReqDesc=$null
        RespJson=@'
HTTP/1.1 200 OK
Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
Content-Disposition: attachment; filename="test_data_50.xlsx"

<binary .xlsx file stream>
'@
        RespDesc=@(
            @('(binary)','A ready-made sample workbook of 50 tickets to try the categorization workflow without supplying your own data.')
        )
        Steps=@(
            'APIM validates the subscription key and forwards the request.',
            'The backend generates a 50-row sample workbook in memory.',
            'The .xlsx is streamed back as a file download.'
        )
    }
)

foreach ($api in $apis) {
    Add-Heading "$($api.Num)  $($api.Title)" 3
    Add-Label 'UAT Base URL: '  '<fill-in: https://{apim-name}.azure-api.net>'
    Add-Label 'Prod Base URL: ' '<fill-in: https://{apim-name}.azure-api.net>'
    Add-Label 'URI: ' $api.Uri
    Add-Label 'HTTP Method: ' $api.Method
    if ($api.ContentType) { Add-Label 'Content-Type: ' $api.ContentType }
    Add-Para 'HTTP Header:' $true
    Add-Table @('Header','Value') @(
        @('Ocp-Apim-Subscription-Key','<your APIM subscription key>')
    )

    # Request
    Add-Para "$($api.Num).1  Request" $true
    if ($api.ReqJson) {
        Add-Para 'Structure:'
        Add-Json $api.ReqJson
        Add-Para 'Description:'
        Add-Table @('Name','Description') $api.ReqDesc
    } else {
        Add-Para 'No request body / parameters.'
    }

    # Response
    Add-Para "$($api.Num).2  Response" $true
    Add-Para 'Structure:'
    Add-Json $api.RespJson
    Add-Para 'Description:'
    Add-Table @('Name','Description') $api.RespDesc

    # Processing steps
    Add-Para 'Processing Steps:' $true
    $n = 1
    foreach ($step in $api.Steps) { Add-Para "$n. $step"; $n++ }

    $sel.InsertBreak($wdPageBreak)
}

# ---- SignalR hub (real-time progress) ----
Add-Heading '3.1.6  Real-time Progress (SignalR Hub)' 3
Add-Label 'URL: ' '/progressHub  (WebSocket; wss:// in cloud, ws:// locally)'
Add-Label 'Protocol: ' 'SignalR over WebSocket'
Add-Para 'Description:' $true
Add-Para 'Clients open a SignalR connection to /progressHub and pass their connection id as the connectionId query parameter to /api/categorize/upload. While the batch is processed, the server pushes progress events to that connection.'
Add-Para 'Event:' $true
Add-Table @('Event','Direction','Payload','Description') @(
    ,@('progress','Server -> Client','int (0-100)','Categorization progress percentage for the in-flight upload.')
)
Add-Para 'Processing Steps:' $true
Add-Para '1. Client connects to /progressHub and obtains its connection id.'
Add-Para '2. Client listens for the "progress" event.'
Add-Para '3. Client calls /api/categorize/upload?connectionId={id}.'
Add-Para '4. Server emits progress (0-100) to that connection as tickets are processed.'
$sel.InsertBreak($wdPageBreak)

# ---- Postman Collection ----
Add-Heading 'Postman Collection' 2
Add-Para 'To call the APIs from Postman:'
Add-Para '1. Set the request URL to the APIM gateway base URL + the endpoint URI (e.g. https://{apim-name}.azure-api.net/api/health).'
Add-Para '2. Add a header Ocp-Apim-Subscription-Key with your APIM subscription key. No bearer token is needed from the client - APIM injects the managed-identity token to the backend.'
Add-Para '3. For the upload endpoint, set the body to form-data with a key "file" of type File and select an .xlsx workbook.'
Add-Para 'Detailed, screenshot-level Postman guidance is in docs/manual-provisioning-portal.md (Step 9 runbook).'

# ---- Swagger Screenshots ----
Add-Heading 'Swagger Screenshots' 2
Add-Para 'The backend exposes interactive OpenAPI/Swagger documentation at /swagger (e.g. http://localhost:5279/swagger when running locally). Paste current Swagger UI screenshots here.'
$sel.InsertBreak($wdPageBreak)

# ---- 3.2 / 3.3 Flow details ----
Add-Heading '3.2  API Flow Details for Categorize and Download' 2
Add-Para '1. Client downloads a sample (GET /api/categorize/sample) or prepares an .xlsx with the required columns.'
Add-Para '2. (Optional) Client opens a SignalR connection to /progressHub for live progress.'
Add-Para '3. Client uploads the workbook (POST /api/categorize/upload), receiving a CategorizationResult with a batchId, per-ticket results, summary, duplicate count and issue groups.'
Add-Para '4. Client downloads the formatted output workbook (GET /api/categorize/download/{batchId}).'

Add-Heading '3.3  API Flow Details for Chat Assistant' 2
Add-Para '1. After at least one batch has been categorized, the client sends a question (POST /api/chat), optionally scoped to a batchId.'
Add-Para '2. The backend grounds the question in the stored ticket data and returns an answer (LLM-generated when Azure AI is configured, otherwise rule-based).'

# ============================================================
# Finalize: update TOC + fields, save
# ============================================================
Write-Host "Updating table of contents..."
$doc.TablesOfContents.Item(1).Update()
$doc.Fields.Update() | Out-Null

Write-Host "Saving to $outPath ..."
if (Test-Path $outPath) { Remove-Item $outPath -Force }
$savePath = [string]$outPath
$doc.SaveAs2($savePath, $wdFormatDocumentDefault)
$doc.Close()
$word.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sel)  | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc)  | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
[GC]::Collect(); [GC]::WaitForPendingFinalizers()

$fi = Get-Item $outPath
Write-Host ("DONE: {0} ({1:N0} bytes)" -f $fi.FullName, $fi.Length)
