import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import {
  CategorizationResult,
  askAssistant,
  downloadUrl,
  sampleUrl,
  uploadAndCategorize,
} from './api';
import SummaryView from './components/SummaryView';
import ResultsTable from './components/ResultsTable';
import ChatPanel from './components/ChatPanel';

export default function App() {
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState(0);
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<CategorizationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const connectionId = useRef<string | undefined>(undefined);

  // Live progress over SignalR (best-effort; categorization works without it).
  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl('/progressHub')
      .withAutomaticReconnect()
      .build();

    conn.on('progress', (p: number) => setProgress(p));
    conn
      .start()
      .then(() => {
        connectionId.current = conn.connectionId ?? undefined;
      })
      .catch(() => {
        /* progress is optional */
      });

    return () => {
      conn.stop();
    };
  }, []);

  const upload = async () => {
    if (!file) {
      setError('Please choose an Excel (.xlsx) file first.');
      return;
    }
    setBusy(true);
    setError(null);
    setProgress(0);
    setResult(null);
    try {
      const res = await uploadAndCategorize(file, connectionId.current);
      setResult(res);
      setProgress(100);
      setToast(`✅ Categorization complete — ${res.totalRecords} tickets classified.`);
    } catch (e: any) {
      const msg = e?.response?.data?.error ?? 'Upload failed. Please try again.';
      setError(msg);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page">
      <header className="hero">
        <h1>Upload Excel File for Ticket Categorization</h1>
        <p className="subtitle">Data Chronicles — AI-Powered Search Engine for Application Issue Categorization</p>
      </header>

      <div className="grid">
        {/* Upload card */}
        <section className="card upload-card">
          <input
            id="file"
            type="file"
            accept=".xlsx,.xlsm"
            disabled={busy}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          <button className="primary" onClick={upload} disabled={busy || !file}>
            {busy ? 'Processing…' : 'Upload and Predict'}
          </button>

          {busy && (
            <div className="progress">
              <div className="bar" style={{ width: `${progress}%` }} />
              <span>{progress}%</span>
            </div>
          )}

          {result && (
            <a className="download-link" href={downloadUrl(result.batchId)}>
              Click here to download the categorized file
            </a>
          )}

          <a className="sample-link" href={sampleUrl()}>
            No file? Download a sample input
          </a>

          {error && <p className="error">{error}</p>}
        </section>

        {/* AI assistant */}
        <ChatPanel
          ask={(q) => askAssistant(q, result?.batchId)}
          ready={!!result}
        />
      </div>

      {result && (
        <>
          <SummaryView result={result} />
          <ResultsTable tickets={result.tickets} />
        </>
      )}

      {toast && (
        <div className="toast" onClick={() => setToast(null)}>
          {toast} <span className="toast-close">✕</span>
        </div>
      )}
    </div>
  );
}
