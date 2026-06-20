import { useState } from 'react';
import { OutputTicket } from '../api';

const sevClass = (s: string) =>
  s === 'High' ? 'sev-high' : s === 'Low' ? 'sev-low' : 'sev-med';

// Render the per-row classification engine. "BART (cached)" means the category was reused
// from a prior duplicate, so no new BART/Hugging Face call was made (token saving).
const engineLabel = (source: string) => {
  if (source === 'BART') return <>🤗 BART</>;
  if (source === 'BART (cached)')
    return (
      <span title="Category reused from a prior duplicate — BART call skipped">
        🤗 BART <span className="muted">· cached</span>
      </span>
    );
  return <>⚙️ Internal</>;
};

export default function ResultsTable({ tickets }: { tickets: OutputTicket[] }) {
  const [query, setQuery] = useState('');
  const filtered = tickets.filter((t) =>
    [t.incident, t.jobName, t.category, t.applicationName]
      .join(' ')
      .toLowerCase()
      .includes(query.toLowerCase())
  );

  return (
    <section className="card">
      <div className="table-head">
        <h2>Categorized Data</h2>
        <input
          className="search"
          placeholder="Search incident / job / category…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>

      <div className="table-wrap">
        <table className="data-table">
          <thead>
            <tr>
              <th>Application</th>
              <th>Incident</th>
              <th>Job Name</th>
              <th>Category</th>
              <th>Confidence</th>
              <th>Severity</th>
              <th>Sentiment</th>
              <th>Source</th>
              <th>Duplicate</th>
            </tr>
          </thead>
          <tbody>
            {filtered.map((t) => (
              <tr key={t.id}>
                <td>{t.applicationName}</td>
                <td>{t.incident}</td>
                <td>{t.jobName}</td>
                <td>{t.category}</td>
                <td>{(t.confidence * 100).toFixed(0)}%</td>
                <td><span className={`badge ${sevClass(t.severity)}`}>{t.severity}</span></td>
                <td>{t.sentiment}</td>
                <td>{engineLabel(t.source)}</td>
                <td>
                  {t.isDuplicate
                    ? <span className="badge dup-yes" title={t.duplicateOf ? `Duplicate of ${t.duplicateOf}` : 'Duplicate'}>Duplicate</span>
                    : <span className="muted">—</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="muted">{filtered.length} of {tickets.length} rows</p>
    </section>
  );
}
